using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace MyLauncher;

// 一覧の表示名とアイコン、起動する保存ファイルを保持する。
internal sealed class ShortcutEntry(string filePath, string displayName) : INotifyPropertyChanged
{
    // 【.NET標準】Lazy で要求されたときにだけアイコンを取得し、結果を再利用する。
    private Lazy<BitmapSource?> _icon = new(() => ShortcutIcon.Load(filePath));

    public string FilePath { get; } = filePath;
    public string DisplayName { get; } = displayName;
    public BitmapSource? Icon => _icon.Value;

    // 【.NET標準】INotifyPropertyChanged でアイコンの更新をWPFに通知する。
    public event PropertyChangedEventHandler? PropertyChanged;

    // 【自作】リンク先を変更したとき、次の表示に使うアイコンを取得し直す。
    internal void RefreshIcon()
    {
        _icon = new Lazy<BitmapSource?>(() => ShortcutIcon.Load(FilePath));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
    }
}
