using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace MyLauncher;

// MainWindow.xaml に対応するメインウィンドウ。
public partial class MainWindow : Window
{
    // 【.NET標準】ObservableCollection は項目の追加を一覧に通知する。
    private readonly ObservableCollection<ShortcutEntry> _shortcuts = [];
    private readonly ShortcutStore _shortcutStore;

    // 直前にマウスで押された項目を覚え、別の行への連続クリックと区別する。
    private ShortcutEntry? _previousMouseEntry;

    // 【.NET標準】GetFolderPath と Path.Combine でユーザーごとの保存先を組み立てる。
    public MainWindow() : this(new ShortcutStore(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MyLauncher", "Shortcuts")))
    {
    }

    // 保存済みの登録を読み込み、表示名順に並べる。
    internal MainWindow(ShortcutStore shortcutStore)
    {
        _shortcutStore = shortcutStore;
        // 【WPF自動生成】XAMLを読み込み、ShortcutList などの画面要素を初期化する。
        InitializeComponent();
        // 【WPF標準】GetDefaultView で一覧の表示順を設定できるビューを取得する。
        ICollectionView shortcutsView = CollectionViewSource.GetDefaultView(_shortcuts);
        // 【WPF標準】SortDescriptions.Add で表示名の昇順を指定する。
        shortcutsView.SortDescriptions.Add(new SortDescription(nameof(ShortcutEntry.DisplayName), ListSortDirection.Ascending));
        ShortcutList.ItemsSource = shortcutsView;

        // 【自作】ShortcutStore.Load で保存済みの登録を読む。
        try
        {
            IReadOnlyList<ShortcutEntry> entries = _shortcutStore.Load(out int failed);
            foreach (ShortcutEntry entry in entries)
            {
                // 【.NET標準】Add で項目を追加すると、コレクションが変更を通知する。
                _shortcuts.Add(entry);
            }

            // 読み込めなかった登録情報があれば、件数を画面に表示する。
            if (failed > 0)
            {
                ShortcutStatus.Text = $"登録情報を読み込めなかった項目があります（{failed} 件）。";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 保存先を開けない場合は画面に表示する。
            ShortcutStatus.Text = "ショートカットの保存先を読み込めませんでした。";
        }
    }

    // 【自作】WPF標準の DragOver イベントにXAMLから結び付ける。
    private void OnDragOver(object sender, DragEventArgs e)
    {
        // 【.NET標準】HasFlag と Any、【WPF標準】GetData、【自作】IsSupported でコピー可能か判定する。
        e.Effects = e.AllowedEffects.HasFlag(DragDropEffects.Copy)
                    && e.Data.GetData(DataFormats.FileDrop) is string[] paths
                    && paths.Any(ShortcutStore.IsSupported)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    // 【自作】WPF標準の Drop イベントにXAMLから結び付ける。
    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        // 【.NET標準】HasFlag でコピー可否を調べ、【WPF標準】GetData でドロップされたパスを受け取る。
        if (!e.AllowedEffects.HasFlag(DragDropEffects.Copy)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        // 一括登録の結果を種類ごとに数える。
        int saved = 0;
        int skipped = 0;
        int failed = 0;
        foreach (string path in paths)
        {
            // 【自作】IsSupported で非対応の項目を読み飛ばす。
            if (!ShortcutStore.IsSupported(path))
            {
                skipped++;
                continue;
            }

            // 【自作】Import で保存し、【.NET標準】Add で一覧に項目を追加する。
            try
            {
                _shortcuts.Add(_shortcutStore.Import(path));
                saved++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 保存失敗を数え、次のファイルの処理へ進む。
                failed++;
            }
        }

        // コピー結果をドロップ元と画面に伝える。
        e.Effects = saved > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        ShowStatus($"登録 {saved} 件 / 対象外 {skipped} 件 / 保存失敗 {failed} 件");
    }

    // 【自作】同じ項目を左ダブルクリックしたときに起動する。
    private void OnShortcutMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 【WPF標準】ContainerFromElement でクリックされた要素を一覧の行に対応付ける。
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(ShortcutList, source) is not ListBoxItem { DataContext: ShortcutEntry entry })
        {
            _previousMouseEntry = null;
            return;
        }

        // 【WPF標準】ClickCount と【.NET標準】ReferenceEquals で同じ項目への2回目のクリックを確認する。
        bool isDoubleClick = e.ClickCount == 2 && ReferenceEquals(_previousMouseEntry, entry);
        _previousMouseEntry = entry;
        if (isDoubleClick)
        {
            // 【自作】ダブルクリックを処理済みにして、保存したショートカットを開く。
            e.Handled = true;
            LaunchShortcut(entry);
        }
    }

    // 【自作】Enter または Space で選択中の項目を起動する。
    private void OnShortcutKeyDown(object sender, KeyEventArgs e)
    {
        // 【WPF標準】SelectedItem と IsRepeat で押し始めたときだけ選択項目を取得する。
        if (e.Key is Key.Enter or Key.Space && !e.IsRepeat && ShortcutList.SelectedItem is ShortcutEntry entry)
        {
            // 【自作】マウスと同じ起動処理を使う。
            e.Handled = true;
            LaunchShortcut(entry);
        }
    }

    // 【自作】対応する保存済みショートカットを開く。
    private void LaunchShortcut(ShortcutEntry entry)
    {
        // 【.NET標準】File.Exists で登録後に保存ファイルが消えていないか確認する。
        if (!File.Exists(entry.FilePath))
        {
            ShowStatus($"「{entry.DisplayName}」の保存ファイルが見つかりません。");
            return;
        }

        // 【.NET標準】Process.Start にショートカットを渡し、引数や作業フォルダーの解決をWindowsに任せる。
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = entry.FilePath,
                UseShellExecute = true
            });
            ShowStatus(string.Empty);
        }
        catch (Win32Exception exception)
        {
            // Windowsシェルが返したエラーを表示し、別の項目を引き続き開けるようにする。
            ShowStatus($"「{entry.DisplayName}」を開けませんでした。{exception.Message}");
        }
    }

    // 【自作】右クリックメニューから選んだ登録をその場で削除する。
    private void OnDeleteShortcutClick(object sender, RoutedEventArgs e)
    {
        // 【WPF標準】DataContext から右クリックした登録を取得する。
        if (sender is not MenuItem { DataContext: ShortcutEntry entry })
        {
            return;
        }

        // 【自作】Delete で登録コピーを削除し、【.NET標準】Remove で一覧にも反映する。
        try
        {
            _shortcutStore.Delete(entry);
            _shortcuts.Remove(entry);
            ShowStatus($"「{entry.DisplayName}」の登録を削除しました。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 【自作】削除できなかった理由を表示する。
            ShowStatus($"「{entry.DisplayName}」を削除できませんでした。{exception.Message}");
        }
    }

    // 【自作】現在のリンク先を表示し、確定した変更を登録コピーへ保存する。
    private void OnChangeTargetClick(object sender, RoutedEventArgs e)
    {
        // 【WPF標準】DataContext から右クリックした登録を取得する。
        if (sender is not MenuItem { DataContext: ShortcutEntry entry })
        {
            return;
        }

        // 【自作】現在のリンク先を読み取り、【WPF標準】ShowDialog で変更入力を受け付ける。
        try
        {
            string currentTarget = _shortcutStore.ReadTarget(entry);
            var dialog = new ShortcutTargetWindow(entry.DisplayName, currentTarget,
                string.Equals(Path.GetExtension(entry.FilePath), ".url", StringComparison.OrdinalIgnoreCase)) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Target == currentTarget)
            {
                return;
            }

            // 【自作】ChangeTarget で登録コピーのリンク先を書き換える。
            _shortcutStore.ChangeTarget(entry, dialog.Target);
            ShowStatus($"「{entry.DisplayName}」のリンク先を変更しました。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            // 【自作】リンク先を読み取り・変更できなかった理由を表示する。
            ShowStatus($"「{entry.DisplayName}」のリンク先を変更できませんでした。{exception.Message}");
        }
    }

    // 【自作】登録や起動の結果を画面に表示する。
    private void ShowStatus(string message)
    {
        ShortcutStatus.Text = message;
        // 【WPF標準】FromElement と RaiseAutomationEvent で支援技術にも結果を通知する。
        UIElementAutomationPeer.FromElement(ShortcutStatus)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
