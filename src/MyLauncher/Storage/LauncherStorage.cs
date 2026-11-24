using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using MyLauncher.Models.Shortcut;

namespace MyLauncher.Storage;

// ショートカットのコピーと登録情報を保存する。
internal class LauncherStorage
{
    // ユーザーごとのアプリデータフォルダーと登録ファイルのパス。
    private static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MyLauncher");

    // launcher.jsonのパス
    private static readonly string LauncherJsonPath = Path.Combine(DataFolder, "launcher.json");

    // 既存の登録情報を読み込み、ショートカットを1件追加する。
    public void AddShortcut(string sourcePath, ShortcutEntry entry)
    {
        string destination = Path.Combine(DataFolder, entry.Target.Path);
        string folder = Path.GetDirectoryName(destination)
            ?? throw new IOException("ショートカットの保存先フォルダーを取得できません。");
        string temporaryJsonPath = Path.Combine(DataFolder, $"launcher-{entry.Id}.tmp");
        Directory.CreateDirectory(DataFolder);

        // 読み込みから保存完了まで、同時書き込みを防ぐ。
        using var saveLock = new FileStream(
            LauncherJsonPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        // 既存のJSONを読み込み、初回なら空の登録一覧を作る。
        JsonObject launcher = File.Exists(LauncherJsonPath)
            ? JsonNode.Parse(File.ReadAllText(LauncherJsonPath)) as JsonObject
                ?? throw new JsonException("launcher.json の形式が正しくありません。")
            : new JsonObject
            {
                ["schemaVersion"] = 1,
                ["entries"] = new JsonArray()
            };

        if (launcher["entries"] is not JsonArray entries)
        {
            throw new JsonException("launcher.json に entries 配列がありません。");
        }

        // 従来と同じキー名で登録情報を追加する。
        entries.Add(JsonSerializer.SerializeToNode(entry, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));

        // コピーと一時JSONの書き込みが成功したら、正式なJSONを置き換える。
        try
        {
            Directory.CreateDirectory(folder);
            File.Copy(sourcePath, destination);
            File.WriteAllText(temporaryJsonPath, JsonSerializer.Serialize(
                launcher, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryJsonPath, LauncherJsonPath, overwrite: true);
        }
        catch
        {
            // 保存に失敗したら、今回作ったファイルとフォルダーを片付ける。
            try
            {
                File.Delete(temporaryJsonPath);

                var copy = new FileInfo(destination);
                if (copy.Exists)
                {
                    copy.IsReadOnly = false;
                    copy.Delete();
                }

                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder);
                }
            }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
            {
                // 片付けにも失敗した場合は、最初の保存エラーを呼び出し元に伝える。
            }

            throw;
        }
    }

    // 保存済みのショートカットを登録順に読み込む。
    public IReadOnlyList<ShortcutEntry> LoadShortcuts()
    {
        // 初回起動など、保存ファイルがまだなければ空の一覧を返す。
        if (!File.Exists(LauncherJsonPath))
        {
            return Array.Empty<ShortcutEntry>();
        }

        // JSONを読み込み、登録一覧を取り出す。
        JsonObject launcher = JsonNode.Parse(File.ReadAllText(LauncherJsonPath)) as JsonObject
            ?? throw new JsonException("launcher.json の形式が正しくありません。");

        if (launcher["entries"] is not JsonArray entries)
        {
            throw new JsonException("launcher.json に entries 配列がありません。");
        }

        // 保存時と同じキー名の規則で、JSONをC#の型に戻す。
        return entries.Deserialize<ShortcutEntry[]>(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }) ?? throw new JsonException("ショートカットの一覧を読み込めませんでした。");
    }

    // 管理フォルダー内にあるショートカットのフルパスを求める。
    public string GetShortcutPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new IOException("ショートカットの保存先が無効です。");
        }

        // パスを正規化して、shortcuts フォルダー内にあることを確認する。
        string fullPath       = Path.GetFullPath(relativePath, DataFolder);
        string shortcutFolder = Path.Combine(DataFolder, "shortcuts") + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(shortcutFolder, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("ショートカットの保存先が管理フォルダーの外にあります。");
        }

        return fullPath;
    }
}