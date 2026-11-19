using System.IO;
using System.Windows;

namespace MyLauncher;

// 【自作】.lnk はファイル・フォルダーのパス、.url はURLを受け取る。
public partial class ShortcutTargetWindow : Window
{
    private readonly bool _isUrl;

    // 保存ボタンで確定した入力を呼び出し元に返す。
    internal string Target { get; private set; } = string.Empty;

    // 【自作】ショートカットの種類に応じた入力欄に、現在のリンク先を表示する。
    internal ShortcutTargetWindow(string displayName, string currentTarget, bool isUrl)
    {
        // 【WPF自動生成】InitializeComponent でXAMLの入力欄を初期化する。
        InitializeComponent();
        _isUrl = isUrl;
        Title = $"{displayName} のパスを変更";
        TargetLabel.Content = isUrl ? "リンク先のURL" : "リンク先のファイルまたはフォルダーのパス";
        TargetInput.Text = currentTarget;
        // 【WPF標準】SelectAll で現在のリンク先をそのまま上書きできるようにする。
        TargetInput.SelectAll();
    }

    // 【自作】入力の形式を確認し、有効な場合だけダイアログを確定する。
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        // 【.NET標準】Trim で前後の空白を除き、ファイルパスの前後にある引用符も取り除く。
        string target = TargetInput.Text.Trim();
        if (!_isUrl)
        {
            target = target.Trim('"');
        }

        // 【.NET標準】絶対URLか、実在する絶対パスかを確認する。
        bool validTarget = _isUrl
            ? Uri.IsWellFormedUriString(target, UriKind.Absolute)
            : Path.IsPathFullyQualified(target) && (File.Exists(target) || Directory.Exists(target));
        if (!validTarget)
        {
            // 入力に問題があればエラーを表示し、ダイアログを開いたままにする。
            ValidationMessage.Text = _isUrl
                ? "https:// などで始まる有効なURLを入力してください。"
                : "存在するファイルまたはフォルダーの絶対パスを入力してください。";
            return;
        }

        // 【WPF標準】DialogResult で入力が確定したことを呼び出し元へ伝える。
        Target = target;
        DialogResult = true;
    }
}
