using System.Windows;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MyLauncher;

// MainWindow.xaml に対応するメインウィンドウ。
public partial class MainWindow : Window
{
    // XAML からウィンドウを初期化する。
    public MainWindow()
    {
        InitializeComponent();
    }

    // 【自作】ドロップされたファイルをコピーして保存
    private void OnDrop(object sender, DragEventArgs e)
    {
        // 【WPF標準】GetData でファイルのフルパスを配列として受け取る。
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        foreach (string sourcePath in paths)
        {
            // 【.NET標準】GetExtension と File.Exists でショートカットファイルか確認する。
            string extension = Path.GetExtension(sourcePath);
            if (!File.Exists(sourcePath)
                || (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".url", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // 【自作】コピーとJSONへの登録を行う。
            try
            {
                RegisterShortcut(sourcePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // 【WPF標準】保存に失敗した理由を表示する。
                MessageBox.Show(ex.Message, "登録できませんでした");
            }
        }
    }

    // 【自作】ショートカットをコピーし、launcher.json に登録情報を追加する。
    private static void RegisterShortcut(string sourcePath)
    {
        // 【.NET標準】ユーザーごとのアプリデータフォルダーを用意する。
        string dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyLauncher");
        Directory.CreateDirectory(dataFolder);

        string jsonPath = Path.Combine(dataFolder, "launcher.json");

        // 【.NET標準】別の起動ウィンドウから同時にJSONを書き換えないようにする。
        using var saveLock = new FileStream(
            jsonPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        // 【.NET標準】既存のJSONを読み込み、初回なら空の登録一覧を作る。
        JsonObject launcher = File.Exists(jsonPath)
            ? JsonNode.Parse(File.ReadAllText(jsonPath)) as JsonObject
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

        // 【.NET標準】既存項目の一番下を求め、新しい項目が重ならないようにする。
        double y = 0;
        foreach (JsonNode? item in entries)
        {
            if (item is JsonObject shortcut && shortcut["layout"] is JsonObject layout)
            {
                double top = layout["y"]?.GetValue<double>() ?? 0;
                double height = layout["height"]?.GetValue<double>() ?? 0;
                y = Math.Max(y, top + height + 8);
            }
        }

        // 【.NET標準】登録IDを発行し、コピー先とJSONに記録する相対パスを作る。
        string id = Guid.NewGuid().ToString();
        string folder = Path.Combine(dataFolder, "shortcuts", id);
        string destination = Path.Combine(folder, Path.GetFileName(sourcePath));
        string relativePath = Path.GetRelativePath(dataFolder, destination).Replace('\\', '/');

        // 【.NET標準】登録情報をJSONの項目に変換して配列に追加する。
        entries.Add(JsonSerializer.SerializeToNode(new
        {
            id,
            name = Path.GetFileNameWithoutExtension(sourcePath),
            target = new { type = "shortcut", path = relativePath },
            registeredAt = DateTime.UtcNow,
            layout = new { x = 0, y, width = 112, height = 96 },
            icon = new { type = "auto" }
        }));

        // 【.NET標準】コピーと一時JSONの書き込みが成功してから、正式なJSONに置き換える。
        string temporaryJsonPath = Path.Combine(dataFolder, $"launcher-{id}.tmp");
        try
        {
            Directory.CreateDirectory(folder);
            File.Copy(sourcePath, destination);
            File.WriteAllText(temporaryJsonPath, JsonSerializer.Serialize(
                launcher, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryJsonPath, jsonPath, overwrite: true);
        }
        catch
        {
            // 【.NET標準】失敗時は、今回作った一時ファイルと登録コピーを片付ける。
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
}
