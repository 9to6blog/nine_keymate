using Microsoft.Win32;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace KeyMate.Services;

internal static class StartupService
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsEnabled { get { using var k = Registry.CurrentUser.OpenSubKey(Key); return k?.GetValue("KeyMate") is string; } }
    public static void SetEnabled(bool enabled)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        if (enabled) k.SetValue("KeyMate", $"\"{Environment.ProcessPath}\" --tray");
        else k.DeleteValue("KeyMate", false);
    }
}

internal static class ThemeService
{
    public static void Apply(string theme)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var dark = theme == "Dark" || theme == "System" && key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        var colors = new Dictionary<string, string>
        {
            ["CanvasBrush"] = dark ? "#151C29" : "#F6F8FC", ["SurfaceBrush"] = dark ? "#1D2738" : "#FFFFFF",
            ["SidebarBrush"] = dark ? "#182131" : "#F0F4FB", ["TextBrush"] = dark ? "#EEF3FF" : "#14213A",
            ["MutedBrush"] = dark ? "#9AAAC3" : "#76839A", ["LineBrush"] = dark ? "#303E54" : "#E7ECF4",
            ["SoftAccentBrush"] = dark ? "#263E65" : "#E5EFFF"
        };
        foreach (var (name, hex) in colors) App.Current.Resources[name] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }
}

internal sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    public TrayService(MainWindow window, ExpansionEngine engine)
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/keymate.ico"));
        using var stream = resource.Stream;
        icon = new() { Text = "KeyMate · 자주 쓰는 말을, 더 빠르게", Icon = new System.Drawing.Icon(stream), Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("대치 항목 열기", null, (_, _) => App.Current.ShowMain());
        menu.Items.Add("설정", null, (_, _) => { App.Current.ShowMain(); window.Navigate("settings"); });
        menu.Items.Add("백업 및 복원", null, (_, _) => { App.Current.ShowMain(); window.Navigate("backup"); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        var pause = new Forms.ToolStripMenuItem("일시 정지") { CheckOnClick = true };
        pause.CheckedChanged += (_, _) => { engine.Paused = pause.Checked; window.UpdateStatus(); icon.Text = pause.Checked ? "KeyMate · 일시 정지" : "KeyMate · 자동 치환 실행 중"; };
        menu.Items.Add(pause); menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => App.Current.Quit());
        icon.ContextMenuStrip = menu; icon.DoubleClick += (_, _) => App.Current.ShowMain();
    }
    public void Dispose() { icon.Visible = false; icon.ContextMenuStrip?.Dispose(); icon.Icon?.Dispose(); icon.Dispose(); }
}
