namespace MyLauncher;

// 一覧に表示する名前と、アプリが保存したショートカットの実ファイルパス。
internal sealed record ShortcutEntry(string FilePath, string DisplayName);
