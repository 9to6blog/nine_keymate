using System.IO;
using System.Windows;
using KeyMate.Core;
using KeyMate.Services;

namespace KeyMate;

public partial class App : System.Windows.Application
{
    private Mutex? mutex;
    private EventWaitHandle? showEvent;
    private RegisteredWaitHandle? showWait;
    public Repository Repository { get; private set; } = null!;
    public ExpansionEngine Engine { get; private set; } = null!;
    private TrayService? tray;
    public Settings Settings { get; private set; } = new();
    public static new App Current => (App)System.Windows.Application.Current;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        mutex = new Mutex(true, "Local\\KeyMate.Singleton", out var first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting("Local\\KeyMate.Show").Set(); } catch (WaitHandleCannotBeOpenedException) { }
            Shutdown(); return;
        }
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyMate");
            Repository = new Repository(Path.Combine(folder, "keymate.db"));
            Settings = Repository.LoadSettings() with { StartWithWindows = StartupService.IsEnabled };
            ThemeService.Apply(Settings.Theme);
            Engine = new ExpansionEngine(); Engine.Configure(Repository.List(), Settings);
            var window = new MainWindow(); MainWindow = window;
            tray = new TrayService(window, Engine);
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\KeyMate.Show");
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => Dispatcher.BeginInvoke(ShowMain), null, -1, false);
            if (!e.Args.Contains("--tray")) window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("KeyMate를 시작하지 못했습니다.\n" + ex.Message, "KeyMate", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    public void ShowMain() { MainWindow.Show(); MainWindow.WindowState = WindowState.Normal; MainWindow.Activate(); }
    public void SaveSettings(Settings value)
    {
        if (StartupService.IsEnabled != value.StartWithWindows) StartupService.SetEnabled(value.StartWithWindows);
        Repository.SaveSettings(value); Settings = value; ThemeService.Apply(value.Theme); RefreshEngine();
    }
    public void RefreshEngine() => Engine.Configure(Repository.List(), Settings);
    protected override void OnExit(ExitEventArgs e)
    {
        showWait?.Unregister(null); showEvent?.Dispose(); tray?.Dispose(); Engine?.Dispose(); mutex?.Dispose();
        base.OnExit(e);
    }
}
