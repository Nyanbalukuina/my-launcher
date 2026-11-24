using System.Diagnostics;
using System.IO;
using MyLauncher.Models.Shortcut;
using MyLauncher.Storage;

namespace MyLauncher.Services.Shortcut;

// 保存済みのショートカットをWindows経由で開く。
internal sealed class LaunchService
{
    private readonly LauncherStorage _launcherStorage = new();

    // 登録情報から保存したコピーを探す。
    public void Launch(ShortcutEntry entry)
    {
        if (entry.Target is not { Type: "shortcut" } target)
        {
            throw new IOException("起動できるショートカットの登録情報がありません。");
        }

        // 対応する拡張子のファイルが保存されていることを確認する。
        string shortcutPath = _launcherStorage.GetShortcutPath(target.Path);

        string extension    = Path.GetExtension(shortcutPath);
        if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("起動できるファイルは .lnk と .url です。");
        }

        if (!File.Exists(shortcutPath))
        {
            throw new FileNotFoundException("保存済みのショートカットが見つかりません。");
        }

        // Windowsシェルに、保存したショートカットを渡す。
        using var process = Process.Start(new ProcessStartInfo(shortcutPath)
        {
            UseShellExecute = true
        });
    }
}