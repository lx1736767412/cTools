using System.IO;
using System.Windows;
using System.Windows.Input;
using cTools.App.Services;
using cTools.Core.Models;
using WinForms = System.Windows.Forms;

namespace cTools.App;

public partial class CustomEntryWindow : Window
{
    public sealed record KindOption(EntryKind Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly CustomEntry _entry;

    public CustomEntryWindow(CustomEntry? existing = null)
    {
        InitializeComponent();
        _entry = existing?.Clone() ?? new CustomEntry();
        SourceInitialized += (_, _) => WindowEffects.Apply(this, ThemeService.IsCurrentDark());
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Cancel_Click(this, new RoutedEventArgs());
            e.Handled = true;
        };

        var kinds = new[] { EntryKind.App, EntryKind.Shortcut, EntryKind.File, EntryKind.Folder, EntryKind.Url, EntryKind.Command }
            .Select(kind => new KindOption(kind, LabelFor(kind)))
            .ToList();
        KindBox.ItemsSource = kinds;

        NameBox.Text = _entry.Name;
        TargetBox.Text = _entry.Target;
        ArgumentsBox.Text = _entry.Arguments ?? "";
        WorkingDirectoryBox.Text = _entry.WorkingDirectory ?? "";
        IconBox.Text = _entry.IconPath ?? "";
        KeywordsBox.Text = string.Join(", ", _entry.Keywords);
        KindBox.SelectedItem = kinds.FirstOrDefault(option => option.Value == _entry.Kind) ?? kinds[0];
        EnabledBox.IsChecked = _entry.Enabled;
        RunAsAdminBox.IsChecked = _entry.RunAsAdmin;
    }

    public CustomEntry Entry => _entry;

    private static string LabelFor(EntryKind kind) => kind switch
    {
        EntryKind.App => "应用",
        EntryKind.Shortcut => "快捷方式",
        EntryKind.File => "文件",
        EntryKind.Folder => "文件夹",
        EntryKind.Url => "网址",
        EntryKind.Command => "命令",
        _ => kind.ToString()
    };

    private void BrowseTarget_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要启动的文件",
            Filter = "所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        TargetBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = EntryKindDetector.DefaultNameFor(dialog.FileName);
        KindBox.SelectedItem = ((List<KindOption>)KindBox.ItemsSource)
            .FirstOrDefault(option => option.Value == EntryKindDetector.Infer(dialog.FileName));
    }

    private void BrowseTargetFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog { Description = "选择文件夹" };
        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;
        TargetBox.Text = dialog.SelectedPath;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = EntryKindDetector.DefaultNameFor(dialog.SelectedPath);
        KindBox.SelectedItem = ((List<KindOption>)KindBox.ItemsSource)
            .FirstOrDefault(option => option.Value == EntryKind.Folder);
    }

    private void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog { Description = "选择工作目录" };
        if (dialog.ShowDialog() == WinForms.DialogResult.OK) WorkingDirectoryBox.Text = dialog.SelectedPath;
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择图标",
            Filter = "图标或程序|*.ico;*.exe;*.dll;*.png;*.jpg|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) == true) IconBox.Text = dialog.FileName;
    }

    private void ClearIcon_Click(object sender, RoutedEventArgs e) => IconBox.Text = "";

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var target = TargetBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(target))
        {
            MessageBox.Show(this, "名称和目标不能为空。", "cTools", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!EntryKindDetector.IsWebUrl(target))
        {
            var looksLikeCommand = KindBox.SelectedItem is KindOption { Value: EntryKind.Command };
            if (!looksLikeCommand && !File.Exists(target) && !Directory.Exists(target))
            {
                var answer = MessageBox.Show(this,
                    "目标路径当前不存在，仍然保存吗？", "cTools",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
            }
        }

        _entry.Name = name;
        _entry.Target = target;
        _entry.Arguments = string.IsNullOrWhiteSpace(ArgumentsBox.Text) ? null : ArgumentsBox.Text.Trim();
        _entry.WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectoryBox.Text) ? null : WorkingDirectoryBox.Text.Trim();
        _entry.IconPath = string.IsNullOrWhiteSpace(IconBox.Text) ? null : IconBox.Text.Trim();
        _entry.Kind = KindBox.SelectedItem is KindOption kind ? kind.Value : EntryKind.App;
        _entry.Keywords = KeywordsBox.Text
            .Split(new[] { ',', '，', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _entry.RunAsAdmin = RunAsAdminBox.IsChecked == true;
        _entry.Enabled = EnabledBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
