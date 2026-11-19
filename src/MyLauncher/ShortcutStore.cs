using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MyLauncher;

// ショートカットをアプリ専用フォルダーに保存する。
internal sealed class ShortcutStore(string directory)
{
    // 登録情報はコピーしたショートカットと同じフォルダーに置く。
    private const string MetadataFileName = "entry.json";

    // JSONには元のファイル名と表示名を保存する。
    private sealed record ShortcutMetadata(string FileName, string DisplayName);

    // 【自作】拡張子が .lnk または .url か判定する。
    internal static bool IsSupported(string path)
    {
        // 【.NET標準】GetExtension で拡張子を取り出し、Equals で大文字・小文字を区別せず比較する。
        string extension = Path.GetExtension(path);
        return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".url", StringComparison.OrdinalIgnoreCase);
    }

    // 【自作】完了済みの登録を一覧として読み込む。
    internal IReadOnlyList<ShortcutEntry> Load(out int failed)
    {
        // 【.NET標準】List に読み込めた登録を集め、失敗件数は別に数える。
        var entries = new List<ShortcutEntry>();
        failed = 0;
        string[] itemDirectories;
        // 【.NET標準】GetDirectories で登録ごとのフォルダーを取得する。
        try
        {
            itemDirectories = Directory.GetDirectories(directory);
        }
        catch (DirectoryNotFoundException)
        {
            // 初回起動などで保存先がなければ、空の一覧を返す。
            return entries;
        }

        foreach (string itemDirectory in itemDirectories)
        {
            // 【.NET標準】TryParseExact でランチャーが作成した登録フォルダーだけを読み込む。
            if (!Guid.TryParseExact(Path.GetFileName(itemDirectory), "N", out _))
            {
                continue;
            }

            string? shortcutPath;
            // 【.NET標準】EnumerateFiles と FirstOrDefault で、【自作】IsSupported に合うファイルを探す。
            try
            {
                shortcutPath = Directory.EnumerateFiles(itemDirectory).FirstOrDefault(IsSupported);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 個別フォルダーの読み込み失敗を数えて次へ進む。
                failed++;
                continue;
            }

            // コピーの途中で中断し、正式なファイルがない登録は読み飛ばす。
            if (shortcutPath is null)
            {
                continue;
            }

            // 【自作】CreateEntry で元の名前を初期値にし、ReadOrCreateMetadata でJSONを読むか作る。
            ShortcutEntry entry = CreateEntry(shortcutPath);
            try
            {
                entry = ReadOrCreateMetadata(itemDirectory, entry);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // JSONを読めない登録も、元の名前で一覧に残す。
                failed++;
            }

            // 【.NET標準】List.Add で読み込み結果に項目を追加する。
            entries.Add(entry);
        }

        return entries;
    }

    // 【自作】登録ごとに専用フォルダーを作り、コピーと登録情報を保存する。
    internal ShortcutEntry Import(string sourcePath)
    {
        // 【.NET標準】Guid.NewGuid で登録先の名前を作り、Path.Combine と CreateDirectory で保存先を用意する。
        string itemDirectory = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        string fileName = Path.GetFileName(sourcePath);
        string destinationPath = Path.Combine(itemDirectory, fileName);
        string temporaryPath = Path.Combine(itemDirectory, "import.tmp");
        string metadataPath = Path.Combine(itemDirectory, MetadataFileName);
        Directory.CreateDirectory(itemDirectory);

        // 【自作】CreateEntry にコピー先を渡し、登録直後から保存したファイルを開けるようにする。
        ShortcutEntry entry = CreateEntry(destinationPath);
        // 【.NET標準】File.Copy と File.Move の間に【自作】WriteMetadata でJSONを保存する。
        try
        {
            File.Copy(sourcePath, temporaryPath);
            WriteMetadata(metadataPath, new ShortcutMetadata(fileName, entry.DisplayName));
            File.Move(temporaryPath, destinationPath);
            return entry;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 【.NET標準】File.Delete と Directory.Delete で今回の登録分だけを片付ける。
            try
            {
                File.Delete(metadataPath);
                File.Delete(temporaryPath);
                Directory.Delete(itemDirectory);
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
                // 後片付けに失敗しても、元の保存エラーを呼び出し元へ返す。
            }

            throw;
        }
    }

    // 【自作】登録単位で一覧から取り除き、保存コピーとJSONを片付ける。
    internal void Delete(ShortcutEntry entry)
    {
        // 【自作】GetItemDirectory で削除対象がアプリの管理フォルダーにあることを確認する。
        string itemDirectory = GetItemDirectory(entry);
        // 【.NET標準】Directory.Move で登録フォルダーを読み込み対象から一度に外す。
        string deletedPath = Path.Combine(directory, $".deleted-{Guid.NewGuid():N}");
        Directory.Move(itemDirectory, deletedPath);
        DeleteTemporaryDirectory(deletedPath);
    }

    // 【自作】保存したショートカットの現在のリンク先を取得する。
    internal string ReadTarget(ShortcutEntry entry)
    {
        GetItemDirectory(entry);
        return AccessTarget(entry.FilePath);
    }

    // 【自作】一時コピーを更新し、内容を検証してから登録コピーと入れ替える。
    internal void ChangeTarget(ShortcutEntry entry, string target)
    {
        // 【自作】GetItemDirectory で保存先を確認し、【.NET標準】CreateDirectory で作業場所を確保する。
        string itemDirectory = GetItemDirectory(entry);
        string editDirectory = Directory.CreateDirectory(Path.Combine(itemDirectory, $".edit-{Guid.NewGuid():N}")).FullName;
        string editedPath = Path.Combine(editDirectory, $"target{Path.GetExtension(entry.FilePath)}");

        try
        {
            // 【.NET標準】File.Copy で作業用にコピーし、【自作】AccessTarget でリンク先を更新する。
            File.Copy(entry.FilePath, editedPath);
            ClearReadOnly(editedPath);
            AccessTarget(editedPath, target);

            // 【自作】保存し直したファイルを読み戻し、指定したリンク先と一致することを確認する。
            string savedTarget = AccessTarget(editedPath);
            if (!TargetsMatch(editedPath, target, savedTarget))
            {
                throw new IOException("ショートカットに指定したリンク先を保存できませんでした。");
            }

            // 【自作】管理コピーを更新可能にし、【.NET標準】File.Move で同じボリューム内で置き換える。
            ClearReadOnly(entry.FilePath);
            File.Move(editedPath, entry.FilePath, overwrite: true);
        }
        finally
        {
            // 【自作】作業用フォルダーを片付ける。
            DeleteTemporaryDirectory(editDirectory);
        }
    }

    // 【自作】URLとファイルパスをそれぞれWindowsが保存した表記に合わせて比較する。
    private static bool TargetsMatch(string shortcutPath, string requested, string saved)
    {
        // 【.NET標準】GetExtension で種類を判定し、Uri.Compare でURL全体を比較する。
        if (Path.GetExtension(shortcutPath).Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(requested, UriKind.Absolute, out Uri? requestedUri)
                && Uri.TryCreate(saved, UriKind.Absolute, out Uri? savedUri)
                && Uri.Compare(requestedUri, savedUri, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.Ordinal) == 0;
        }

        // 【.NET標準】GetFullPath で表記を揃え、末尾の区切り文字や大文字・小文字の違いを許容する。
        return Path.IsPathFullyQualified(saved)
            && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(requested)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(saved)), StringComparison.OrdinalIgnoreCase);
    }

    // 【自作】Windows標準の WScript.Shell で .lnk と .url を読み書きする。
    private static string AccessTarget(string path, string? target = null)
    {
        // 【.NET標準】File.Exists で、既存の登録コピーが開けることを確かめる。
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("保存ファイルが見つかりません。", path);
        }

        // 【.NET標準】GetTypeFromProgID と CreateInstance でWindowsのCOMオブジェクトを取得する。
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windowsのショートカット編集機能を利用できません。");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            // 【Windows標準】CreateShortcut で保存済みのショートカットを開く。
            dynamic shortcut = shell.CreateShortcut(path);
            try
            {
                // 【Windows標準】TargetPath を変更し、Save で既存のショートカットに保存する。
                if (target is not null)
                {
                    shortcut.TargetPath = target;
                    shortcut.Save();
                }

                return (string?)shortcut.TargetPath ?? string.Empty;
            }
            finally
            {
                // 【.NET標準】ReleaseComObject でショートカットのCOM参照を解放する。
                Marshal.ReleaseComObject((object)shortcut);
            }
        }
        finally
        {
            // 【.NET標準】ReleaseComObject でWindowsシェルのCOM参照を解放する。
            Marshal.ReleaseComObject((object)shell);
        }
    }

    // 【自作】変更・削除の対象を、管理フォルダー直下の登録用フォルダーに限定する。
    private string GetItemDirectory(ShortcutEntry entry)
    {
        // 【.NET標準】GetFullPath と GetDirectoryName で保存先との位置関係を確認する。
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string? itemDirectory = Path.GetDirectoryName(Path.GetFullPath(entry.FilePath));
        if (itemDirectory is null
            || !string.Equals(Path.GetDirectoryName(itemDirectory), root, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileName(itemDirectory), "N", out _)
            || (File.GetAttributes(itemDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("管理フォルダー内の登録を確認できませんでした。");
        }

        return itemDirectory;
    }

    // 【自作】アプリが管理するファイルの読み取り専用属性を解除する。
    private static void ClearReadOnly(string path)
    {
        // 【.NET標準】GetAttributes と SetAttributes で読み取り専用のフラグだけを変える。
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
    }

    // 【自作】作業中または削除済みのフォルダーを片付ける。
    private static void DeleteTemporaryDirectory(string path)
    {
        // 【.NET標準】Directory.Delete で処理済みのファイルをまとめて取り除く。
        try
        {
            // 【.NET標準】EnumerateFiles で直下のファイルを取得し、【自作】ClearReadOnly で削除可能にする。
            foreach (string filePath in Directory.EnumerateFiles(path))
            {
                ClearReadOnly(filePath);
            }

            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 作業用フォルダーは一覧の読み込み対象外なので、片付け失敗で確定済みの結果は変えない。
        }
    }

    // 【自作】ファイル名から初期表示名を作る。
    private static ShortcutEntry CreateEntry(string path)
    {
        // 【.NET標準】GetFileName と GetFileNameWithoutExtension で元の名前と拡張子を除いた名前を得る。
        string fileName = Path.GetFileName(path);
        string displayName = Path.GetFileNameWithoutExtension(fileName);
        return new ShortcutEntry(path, string.IsNullOrWhiteSpace(displayName) ? fileName : displayName);
    }

    // 【自作】旧形式ではJSONを新規作成し、既存のJSONは検証して使う。
    private static ShortcutEntry ReadOrCreateMetadata(string itemDirectory, ShortcutEntry original)
    {
        // 【.NET標準】Path.Combine と GetFileName でJSONの保存先と実ファイル名を取得する。
        string path = Path.Combine(itemDirectory, MetadataFileName);
        string fileName = Path.GetFileName(original.FilePath);
        // 【.NET標準】File.ReadAllText と JsonSerializer.Deserialize で登録情報を復元する。
        try
        {
            ShortcutMetadata? metadata = JsonSerializer.Deserialize<ShortcutMetadata>(File.ReadAllText(path));
            // 実ファイル名との一致を確認し、【.NET標準】IsNullOrWhiteSpace で空の表示名を検出する。
            if (metadata is null || metadata.FileName != fileName || string.IsNullOrWhiteSpace(metadata.DisplayName))
            {
                throw new JsonException("ショートカットの登録情報が不正です。");
            }

            // 起動先には保存フォルダーで見つけた実ファイルパスを使う。
            return original with { DisplayName = metadata.DisplayName };
        }
        catch (FileNotFoundException)
        {
            // 【自作】WriteMetadata で旧形式の登録にJSONを追加する。
            WriteMetadata(path, new ShortcutMetadata(fileName, original.DisplayName));
            return original;
        }
    }

    // 【自作】JSONを一時ファイルに書き、完了後に正式なファイル名にする。
    private static void WriteMetadata(string path, ShortcutMetadata metadata)
    {
        // 【.NET標準】Guid.NewGuid で一時ファイル名の重複を避ける。
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        // 【.NET標準】Serialize でJSONに変換し、WriteAllText と Move で保存する。
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(metadata));
            File.Move(temporaryPath, path);
        }
        // 【.NET標準】File.Exists で別のインスタンスが先に保存したかを確かめる。
        catch (IOException) when (File.Exists(path))
        {
            // 別のインスタンスが先に旧形式の登録情報を保存した場合は、その内容を残す。
        }
        finally
        {
            // 【.NET標準】File.Delete で残った一時ファイルを片付ける。
            File.Delete(temporaryPath);
        }
    }
}
