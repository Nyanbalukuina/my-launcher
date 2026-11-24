using System.IO;
using System.Text.Json;
using System.Windows;
using MyLauncher.Services.Shortcut;
using MyLauncher.Storage;

namespace MyLauncher;

// ドロップを受け付け、保存済みの一覧を表示する。
public partial class MainWindow : Window
{
    private readonly RegistrationService _registrationService = new();
    private readonly LauncherStorage     _launcherStorage     = new();

    // XAMLを初期化してから、保存済みの一覧を表示する。
    public MainWindow()
    {
        InitializeComponent();
        RefreshShortcutList();
    }

    // ドロップされたファイルを登録し、一覧を更新する。
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        // 1件ずつ登録し、失敗したファイルはエラーを表示する。
        foreach (string sourcePath in paths)
        {
            try
            {
                _registrationService.Register(sourcePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                MessageBox.Show(this, ex.Message, "登録できませんでした");
            }
        }

        RefreshShortcutList();
    }

    // 保存済みの登録情報を読み込み、ListBoxに渡す。
    private void RefreshShortcutList()
    {
        try
        {
            ShortcutList.ItemsSource = _launcherStorage.LoadShortcuts();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, ex.Message, "一覧を読み込めませんでした");
        }
    }
}