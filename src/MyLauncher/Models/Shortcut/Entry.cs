namespace MyLauncher.Models.Shortcut;

// ショートカット1件の登録情報。
internal sealed record ShortcutEntry
(
    string         Id,
    string         Name,
    ShortcutTarget Target,
    DateTime       RegisteredAt,
    ShortcutLayout Layout,
    ShortcutIcon   Icon
);

// 起動対象の種類と保存先の相対パス。
internal sealed record ShortcutTarget(string Type, string Path);

// 項目の位置とサイズ。
internal sealed record ShortcutLayout(double X, double Y, double Width, double Height);

// アイコンの表示方法。
internal sealed record ShortcutIcon(string Type);
