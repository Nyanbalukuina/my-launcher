using MyLauncher.Models.Shortcut;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using MyLauncher.Storage;

namespace MyLauncher.Services.Shortcut;

internal class RegistrationService
{
    private readonly LauncherStorage _launcherStorage = new();

    public void Register(string sourcePath)
    {

        // 【.NET標準】GetExtension と File.Exists でショートカットファイルか確認する。
        string extension = Path.GetExtension(sourcePath);
        if (!File.Exists(sourcePath)
            || (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".url", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // 登録IDを発行し、JSONに記録する相対パスを作る。
        string id           = Guid.NewGuid().ToString();
        string relativePath = $"shortcuts/{id}/{Path.GetFileName(sourcePath)}";

        // 保存前に整形
        var entry = new ShortcutEntry(
            Id           : id,
            Name         : Path.GetFileNameWithoutExtension(sourcePath),
            Target       : new ShortcutTarget("shortcut", relativePath),
            RegisteredAt : DateTime.UtcNow,
            Layout       : new ShortcutLayout(X: 0, Y: 0, Width: 112, Height: 96),
            Icon         : new ShortcutIcon("auto"));

        // LauncherStorage呼び出し
        _launcherStorage.AddShortcut(sourcePath, entry);
    }
}
