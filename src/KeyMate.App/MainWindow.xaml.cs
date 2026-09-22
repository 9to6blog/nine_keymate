using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using KeyMate.Core;
using Microsoft.Win32;

namespace KeyMate;

public partial class MainWindow : Window
{
    private bool loading = true;
    private bool practicing;
    private List<Snippet> snippets = [];
    public MainWindow()
    {
        InitializeComponent(); LoadSettings(); Reload(); Navigate("entries"); loading = false;
        App.Current.Engine.Replaced += shortcut => Dispatcher.BeginInvoke(() => SetStatus($"‘{shortcut}’ 문구를 치환했습니다."));
        App.Current.Engine.Status += message => Dispatcher.BeginInvoke(() => SetStatus(message));
    }
    private void Reload() { snippets = App.Current.Repository.List(); Filter(); App.Current.RefreshEngine(); }
    private void Filter()
    {
        if (SnippetList is null) return;
        var query = SearchBox.Text.Trim();
        var rows = snippets.Where(s => s.Shortcut.Contains(query, StringComparison.OrdinalIgnoreCase) || s.Expansion.Contains(query, StringComparison.OrdinalIgnoreCase) || s.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        SnippetList.ItemsSource = rows;
        CountText.Text = $"총 {snippets.Count}개 항목  ·  {snippets.Count(s => s.Enabled)}개 사용 중";
        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchPlaceholder.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) => Filter();
    public void Navigate(string page)
    {
        EntriesPage.Visibility = page == "entries" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        BackupPage.Visibility = page == "backup" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == "about" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var b in new[] { EntriesNav, SettingsNav, BackupNav, AboutNav })
        { b.SetResourceReference(BackgroundProperty, (string)b.Tag == page ? "SoftAccentBrush" : "SidebarBrush"); b.FontWeight = (string)b.Tag == page ? FontWeights.SemiBold : FontWeights.Normal; }
        UpdateStatus();
    }
    private void Nav_Click(object sender, RoutedEventArgs e) => Navigate((string)((Button)sender).Tag);
    private void Add_Click(object sender, RoutedEventArgs e) => Edit(null);
    private void Edit(Snippet? snippet)
    {
        var editor = new EditorWindow(snippet, snippets) { Owner = this };
        if (editor.ShowDialog() == true && editor.Result is { } result)
            Run(() => { App.Current.Repository.Save(result); Reload(); SetStatus("대치 항목을 저장했습니다."); });
    }
    private void More_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender; var snippet = (Snippet)button.DataContext;
        var menu = new ContextMenu();
        var edit = new MenuItem { Header = "수정" }; edit.Click += (_, _) => Edit(snippet); menu.Items.Add(edit);
        var delete = new MenuItem { Header = "삭제" }; delete.Click += (_, _) =>
        {
            if (MessageBox.Show(this, $"‘{snippet.Shortcut}’ 항목을 삭제할까요?", "항목 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                Run(() => { App.Current.Repository.Delete(snippet.Id); Reload(); SetStatus("항목을 삭제했습니다."); });
        };
        menu.Items.Add(delete); menu.PlacementTarget = button; menu.IsOpen = true;
    }
    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        var box = (CheckBox)sender; var snippet = (Snippet)box.DataContext;
        Run(() => { App.Current.Repository.Save(snippet with { Enabled = box.IsChecked == true }); Reload(); });
    }
    private void LoadSettings()
    {
        loading = true; var s = App.Current.Settings;
        EnabledSetting.IsChecked = s.Enabled; StartupSetting.IsChecked = s.StartWithWindows;
        SpaceSetting.IsChecked = s.Space; EnterSetting.IsChecked = s.Enter; TabSetting.IsChecked = s.Tab;
        ExcludedSetting.Text = s.ExcludedApps;
        ThemeSetting.SelectedIndex = s.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 }; loading = false;
    }
    private void SaveSettings()
    {
        if (loading) return;
        Run(() =>
        {
            App.Current.SaveSettings(new Settings { Enabled = EnabledSetting.IsChecked == true, StartWithWindows = StartupSetting.IsChecked == true,
                Space = SpaceSetting.IsChecked == true, Enter = EnterSetting.IsChecked == true, Tab = TabSetting.IsChecked == true,
                ExcludedApps = ExcludedSetting.Text, Theme = (string)((ComboBoxItem)ThemeSetting.SelectedItem).Tag });
            UpdateStatus(); SetStatus("설정을 저장했습니다.");
        });
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => SaveSettings();
    private void Excluded_LostFocus(object sender, RoutedEventArgs e) => SaveSettings();
    private void Theme_Changed(object sender, SelectionChangedEventArgs e) => SaveSettings();
    public void UpdateStatus()
    {
        var enabled = App.Current.Settings.Enabled && !App.Current.Engine.Paused;
        EngineStatus.Text = enabled ? "●  자동 치환 켜짐" : "Ⅱ  자동 치환 멈춤";
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "KeyMate 백업 (*.json)|*.json", FileName = "KeyMate-backup.json", DefaultExt = ".json" };
        if (dialog.ShowDialog(this) == true) Run(() => { App.Current.Repository.Export(dialog.FileName); SetStatus("백업 파일을 저장했습니다."); });
    }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "KeyMate 백업 (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, "같은 단축어는 백업 내용으로 갱신됩니다. 가져올까요?", "백업 가져오기", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        Run(() => { var count = App.Current.Repository.Import(dialog.FileName); Reload(); SetStatus($"{count}개 항목을 가져왔습니다."); });
    }
    private void Playground_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space && e.ImeProcessedKey != Key.Space) return;
        if (practicing || loading || !App.Current.Settings.Enabled || !App.Current.Settings.Space || App.Current.Engine.Paused) return;
        var caret = Playground.CaretIndex; var text = Playground.Text;
        if (caret == 0 || text[caret - 1] != ' ') return;
        var match = Rules.Match(text[..(caret - 1)], snippets); if (match is null) return;
        practicing = true;
        var replacement = Rules.Expand(match, DateTime.Now) + " "; var start = caret - match.Shortcut.Length - 1;
        Playground.Select(start, match.Shortcut.Length + 1); Playground.SelectedText = replacement;
        Playground.Select(start + replacement.Length, 0); practicing = false;
    }
    private void SetStatus(string text) => StatusText.Text = text;
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "KeyMate", MessageBoxButton.OK, MessageBoxImage.Warning); LoadSettings(); }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (App.Current.Exiting) return;
        SaveSettings();
        // Explicit Shutdown must still terminate the app; normal Close only hides it.
        if (System.Windows.Threading.Dispatcher.CurrentDispatcher.HasShutdownStarted) return;
        e.Cancel = true; Hide();
    }
}
