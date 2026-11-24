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

    // 指定IDの登録をまとめて削除し、後片付けに失敗した件数を返す。
    public int DeleteShortcuts(IReadOnlyCollection<string> ids)
    {
        if (ids.Count == 0 || !Directory.Exists(DataFolder))
        {
            return 0;
        }

        // 登録時と同じロックを取得してから、最新のJSONを読む。
        using var saveLock = new FileStream(
            LauncherJsonPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        if (!File.Exists(LauncherJsonPath))
        {
            return 0;
        }

        JsonObject launcher = JsonNode.Parse(File.ReadAllText(LauncherJsonPath)) as JsonObject
            ?? throw new JsonException("launcher.json の形式が正しくありません。");

        if (launcher["entries"] is not JsonArray entries)
        {
            throw new JsonException("launcher.json に entries 配列がありません。");
        }

        // 配列の後ろから削除し、コピーの削除に必要な情報を控える。
        var selectedIds = ids.ToHashSet(StringComparer.Ordinal);
        var removed = new List<(string Id, JsonObject Entry)>();
        for (int index = entries.Count - 1; index >= 0; index--)
        {
            if (entries[index] is JsonObject entry
                && entry["id"] is JsonValue idValue
                && idValue.TryGetValue<string>(out var id)
                && id is not null
                && selectedIds.Contains(id))
            {
                removed.Add((id, entry));
                entries.RemoveAt(index);
            }
        }

        if (removed.Count == 0)
        {
            return 0;
        }

        // 一時ファイルへの書き込みに成功してから、正式なJSONを置き換える。
        string temporaryPath = Path.Combine(DataFolder, $"launcher-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, launcher.ToJsonString(
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, LauncherJsonPath, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 一時ファイルを消せない場合も、最初の保存エラーを伝える。
            }

            throw;
        }

        // 一部のコピーを消せなくても、残りの後片付けを続ける。
        int cleanupFailures = 0;
        foreach (var (id, entry) in removed)
        {
            try
            {
                DeleteShortcutCopy(id, entry);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // コピーがすでに消えていれば後片付けは不要。
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                cleanupFailures++;
            }
        }

        return cleanupFailures;
    }

    // 対象IDの専用フォルダーにある、登録時のコピーだけを削除する。
    private void DeleteShortcutCopy(string id, JsonObject entry)
    {
        if (!Guid.TryParseExact(id, "D", out _)
            || entry["target"] is not JsonObject target
            || target["path"] is not JsonValue pathValue
            || !pathValue.TryGetValue<string>(out var relativePath)
            || string.IsNullOrWhiteSpace(relativePath))
        {
            throw new IOException("保存済みコピーの場所を確認できません。");
        }

        // コピー先が対象IDのフォルダー直下にあることを確認する。
        string path            = GetShortcutPath(relativePath);
        string shortcutsFolder = Path.Combine(DataFolder, "shortcuts");
        string entryFolder     = Path.Combine(shortcutsFolder, id);

        string extension = Path.GetExtension(path);
        if (!string.Equals(Path.GetDirectoryName(path), entryFolder, StringComparison.OrdinalIgnoreCase)
            || (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".url", StringComparison.OrdinalIgnoreCase)))
        {
            throw new IOException("保存済みコピーの場所が登録情報と一致しません。");
        }

        // リンク経由で別の場所を削除しないよう、フォルダーとファイルを確認する。
        if ((File.GetAttributes(shortcutsFolder) & FileAttributes.ReparsePoint) != 0
            || (File.GetAttributes(entryFolder) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("保存先フォルダーが別の場所を参照しています。");
        }

        if (File.Exists(path))
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("保存済みコピーが別の場所を参照しています。");
            }

            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(path);
        }

        // 専用フォルダーが空になった場合だけ片付ける。
        if (!Directory.EnumerateFileSystemEntries(entryFolder).Any())
        {
            Directory.Delete(entryFolder);
        }
    }
}