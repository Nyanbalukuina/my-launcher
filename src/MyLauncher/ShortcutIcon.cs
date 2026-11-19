using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MyLauncher;

// Windowsシェルからショートカットのアイコンを取得する。
public static class ShortcutIcon
{
    // 【Windows標準】InternetShortcut のCOMクラスを識別する。
    private static readonly Guid InternetShortcutClassId = new("FBF23B40-E3F0-101B-8488-00AA003E56F8");

    // 【.NET標準】Lazy でアイコン取得用のSTAスレッドを最初の取得時に用意する。
    private static readonly Lazy<Dispatcher> IconDispatcher = new(StartIconDispatcher);

    // 【Windows標準】LoadIconW で取得する共通アイコンはWindowsが管理する。
    public static BitmapSource? Default { get; } = CreateBitmap(LoadIcon(0, 32512));

    // 【WPF標準】Invoke でシェルのアイコン取得をSTAスレッドに任せる。
    internal static BitmapSource? Load(string path) => IconDispatcher.Value.Invoke(() => ReadIcon(path));

    // 【自作】シェルが必要とするSTAとメッセージループを画面とは別に用意する。
    private static Dispatcher StartIconDispatcher()
    {
        // 【.NET標準】TaskCompletionSource でスレッドの初期化が完了したことを知らせる。
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            // 【WPF標準】CurrentDispatcher を取得し、Run でシェル用のイベントを処理する。
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Shortcut icons" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }

    // 【自作】保存した .lnk／.url がWindowsで表示するアイコンを読み取る。
    private static BitmapSource? ReadIcon(string path)
    {
        // 【Windows標準】CoInitializeEx でシェル用スレッドのCOMを初期化する。
        const uint apartmentThreaded = 2;
        const int changedApartmentMode = unchecked((int)0x80010106);
        int initialization = CoInitializeEx(0, apartmentThreaded);
        if (initialization < 0 && initialization != changedApartmentMode)
        {
            return Default;
        }

        try
        {
            // 【自作】.url はWindowsの専用ハンドラーに個別指定のアイコンを問い合わせる。
            if (Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase)
                && LoadInternetShortcutIcon(path) is { } internetIcon)
            {
                return internetIcon;
            }

            // 【Windows標準】SHGetFileInfoW に保存ファイルを渡し、個別に設定されたアイコンを取得する。
            const uint iconFlag = 0x100;
            if (SHGetFileInfo(path, 0, out ShellFileInfo info, (uint)Marshal.SizeOf<ShellFileInfo>(), iconFlag) == 0)
            {
                return Default;
            }

            try
            {
                // 【自作】WPFで表示できる画像に変換する。
                return CreateBitmap(info.Icon) ?? Default;
            }
            finally
            {
                // 【Windows標準】シェルが返したアイコンハンドルを解放する。
                if (info.Icon != 0)
                {
                    DestroyIcon(info.Icon);
                }
            }
        }
        finally
        {
            // 【Windows標準】この呼び出しで初期化に成功した分だけCOMを解放する。
            if (initialization >= 0)
            {
                CoUninitialize();
            }
        }
    }

    // 【自作】インターネットショートカット用のシェル拡張でアイコンを取得する。
    private static BitmapSource? LoadInternetShortcutIcon(string path)
    {
        // 【.NET標準】GetTypeFromCLSID でWindowsのインターネットショートカットを取得する。
        if (Type.GetTypeFromCLSID(InternetShortcutClassId) is not { } shortcutType)
        {
            return null;
        }

        try
        {
            // 【.NET標準】CreateInstance でCOMオブジェクトを作成する。
            object shortcut = Activator.CreateInstance(shortcutType)!;
            try
            {
                // 【Windows標準】IPersistFile.Load と GetIconLocation で保存済み .url のアイコンを調べる。
                ((IPersistFile)shortcut).Load(path, 0);
                var location = new StringBuilder(260);
                const uint forShell = 2;
                if (shortcut is not IExtractIcon extractor
                    || extractor.GetIconLocation(forShell, location, location.Capacity, out int index, out _) != 0)
                {
                    return null;
                }

                // 【Windows標準】Extract に大きいアイコンと小さいアイコンのサイズを指定する。
                const uint iconSizes = 32 | (16 << 16);
                int result = extractor.Extract(location.ToString(), unchecked((uint)index), out nint large, out nint small, iconSizes);
                try
                {
                    // 【自作】利用できるアイコンをWPF用の画像に変換する。
                    return result == 0 ? CreateBitmap(large != 0 ? large : small) : null;
                }
                finally
                {
                    // 【Windows標準】取得したアイコンハンドルをそれぞれ解放する。
                    if (large != 0) DestroyIcon(large);
                    if (small != 0 && small != large) DestroyIcon(small);
                }
            }
            finally
            {
                // 【.NET標準】ReleaseComObject で専用ハンドラーへの参照を解放する。
                Marshal.ReleaseComObject(shortcut);
            }
        }
        catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException)
        {
            // 専用ハンドラーで読み込めない場合は通常のシェル取得へ進む。
            return null;
        }
    }

    // 【WPF標準】CreateBitmapSourceFromHIcon で画像化し、Freeze で画面のスレッドへ渡せるようにする。
    private static BitmapSource? CreateBitmap(nint icon)
    {
        if (icon == 0)
        {
            return null;
        }

        BitmapSource bitmap = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        bitmap.Freeze();
        return bitmap;
    }

    // 【Windows標準】SHGetFileInfoW が書き込む構造体のメモリ配置を定義する。
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        internal nint Icon;
        internal int IconIndex;
        internal uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] internal string TypeName;
    }

    // 【Windows標準】インターネットショートカットが公開する Unicode 版のアイコン抽出APIを定義する。
    [ComImport, Guid("000214FA-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IExtractIcon
    {
        [PreserveSig]
        int GetIconLocation(uint flags, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder location, int capacity,
            out int index, out uint resultFlags);

        [PreserveSig]
        int Extract([MarshalAs(UnmanagedType.LPWStr)] string location, uint index, out nint large, out nint small, uint size);
    }

    // 【Windows標準】アイコン取得とハンドル解放に使う関数を宣言する。
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);

    [DllImport("user32.dll", EntryPoint = "LoadIconW", ExactSpelling = true)]
    private static extern nint LoadIcon(nint instance, nint resourceName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);

    // 【Windows標準】バックグラウンドのスレッドでCOMを初期化・解放する関数を宣言する。
    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint flags);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
