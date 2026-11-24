using System.IO;
using System.Text.Json;
using System.Windows;
using MyLauncher.Services.Shortcut;
using MyLauncher.Storage;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using MyLauncher.Models.Shortcut;

namespace MyLauncher;

// ドロップを受け付け、保存済みの一覧を表示する。
public partial class MainWindow : Window
{
    private readonly RegistrationService _registrationService = new();
    private readonly LauncherStorage     _launcherStorage     = new();
    private readonly LaunchService _launchService = new();

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

    // クリックされた項目の登録情報を取得する。
    private void OnShortcutDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { Content: ShortcutEntry entry })
        {
            return;
        }

        // 起動を依頼し、失敗した場合は項目名と理由を表示する。
        try
        {
            _launchService.Launch(entry);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException or Win32Exception)
        {
            MessageBox.Show(this, $"{entry.Name}\n{ex.Message}", "ショートカットを起動できませんでした");
        }
    }

    // 修飾キー付きの右クリックなどでも、メニューを開いた項目を選択に含める。
    // メニューを開いた項目と現在の選択状態をそろえる。
    private void OnShortcutMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        if (menu.PlacementTarget is ListBoxItem { IsSelected: false } item)
        {
            ShortcutList.UnselectAll();
            item.IsSelected = true;
        }

        // 選択がちょうど1件のときだけ名前を変更できる。
        MenuItem? renameItem = menu.Items.OfType<MenuItem>()
            .FirstOrDefault(item => Equals(item.Tag, "Rename"));
        if (renameItem is not null)
        {
            renameItem.IsEnabled = ShortcutList.SelectedItems.Count == 1;
        }
    }

    // 選択中の1件について新しい表示名を入力してもらう。
    private void OnRenameShortcut(object sender, RoutedEventArgs e)
    {
        if (ShortcutList.SelectedItems.Count != 1
            || ShortcutList.SelectedItem is not ShortcutEntry entry)
        {
            return;
        }

        var dialog = new RenameShortcutWindow(entry.Name) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.NewName == entry.Name)
        {
            return;
        }

        // 表示名を保存し、一覧に反映する。
        try
        {
            _launcherStorage.RenameShortcut(entry.Id, dialog.NewName);
            RefreshShortcutList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, ex.Message, "名前を変更できませんでした");
        }
    }

    // 選択したショートカットの削除を一度だけ確認する。
    private void OnDeleteShortcuts(object sender, RoutedEventArgs e)
    {
        ShortcutEntry[] selected = ShortcutList.SelectedItems.OfType<ShortcutEntry>().ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        string message = selected.Length == 1
            ? $"「{selected[0].Name}」を削除しますか？"
            : $"選択した {selected.Length} 件を削除しますか？";

        if (MessageBox.Show(this, message, "ショートカットの削除",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            != MessageBoxResult.Yes)
        {
            return;
        }

        // 選択したIDをまとめて渡し、保存後の一覧を表示する。
        try
        {
            string[] ids = selected.Select(entry => entry.Id).ToArray();
            int cleanupFailures = _launcherStorage.DeleteShortcuts(ids);
            RefreshShortcutList();

            if (cleanupFailures > 0)
            {
                MessageBox.Show(this,
                    $"一覧から削除しましたが、保存フォルダーの後片付けに {cleanupFailures} 件失敗しました。",
                    "ショートカットの削除");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, ex.Message, "削除できませんでした");
        }
    }
}