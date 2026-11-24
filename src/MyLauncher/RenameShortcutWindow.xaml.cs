using System.Windows;

namespace MyLauncher;

// 現在の名前を表示し、入力された新しい名前を返す。
public partial class RenameShortcutWindow : Window
{
    public string NewName => NameTextBox.Text.Trim();

    // 最初から名前を打ち替えられるよう、現在の名前を選択する。
    public RenameShortcutWindow(string currentName)
    {
        InitializeComponent();
        NameTextBox.Text = currentName;
        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    // 空白だけの名前は保存せず、入力画面を開いたままにする。
    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (NewName.Length == 0)
        {
            MessageBox.Show(this, "表示名を入力してください。");
            NameTextBox.Focus();
            return;
        }

        DialogResult = true;
    }
}