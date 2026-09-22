using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using KeyMate.Core;
using KeyMate.Services;

internal static class Program
{
    private const uint Marker = 0x4B4D5453;
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public Union Data; }
    [StructLayout(LayoutKind.Explicit)] private struct Union { [FieldOffset(0)] public Kbd Key; [FieldOffset(0)] public Mouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct Kbd { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, Input[] input, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadKeyboardLayout(string id, uint flags);
    [DllImport("user32.dll")] private static extern nint ActivateKeyboardLayout(nint layout, uint flags);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    private static readonly List<string> report = [];
    private static void Tap(ushort key)
    {
        var scan = (ushort)MapVirtualKey(key, 0);
        Input[] keys = [new() { Type = 1, Data = new() { Key = new() { Vk = key, Scan = scan, Extra = Marker } } }, new() { Type = 1, Data = new() { Key = new() { Vk = key, Scan = scan, Flags = 2, Extra = Marker } } }];
        if (SendInput(2, keys, Marshal.SizeOf<Input>()) != 2) throw new Exception("SendInput failed");
    }
    [STAThread] private static int Main(string[] args)
    {
        var app = new System.Windows.Application();
        var box = new TextBox { AcceptsReturn = true, MinHeight = 150, FontSize = 24, Margin = new Thickness(20) };
        var password = new PasswordBox { Margin = new Thickness(20) };
        var other = new TextBox { Margin = new Thickness(20) };
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = "KeyMate integration test · temporary isolated input" }); panel.Children.Add(box); panel.Children.Add(password); panel.Children.Add(other);
        var window = new Window { Title = "KeyMate integration test", Content = panel, Width = 700, Height = 410, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var failed = 0;
        window.Loaded += async (_, _) =>
        {
            using var engine = new ExpansionEngine(true);
            engine.Status += message => Console.WriteLine("DIAGNOSTIC " + message);
            var entries = new[] { new Snippet { Shortcut = "ㅈㅅ", Expansion = "안녕하세요." }, new Snippet { Shortcut = "/sig", Expansion = "감사합니다.\n홍길동 드림" }, new Snippet { Shortcut = "!x", Expansion = "expanded" } };
            var settings = new Settings { ExcludedApps = "", Enter = true, Tab = true };
            engine.Configure(entries, settings);
            async Task Reset(string text)
            {
                SetForegroundWindow(new WindowInteropHelper(window).Handle); window.Activate(); box.Focus();
                box.IsReadOnly = false; box.Text = text; box.CaretIndex = text.Length; await Task.Delay(300);
            }
            async Task Check(string name, Func<Task<bool>> test)
            {
                try { if (!await test()) throw new Exception("Unexpected result: " + box.Text.Replace("\r", "\\r").Replace("\n", "\\n")); report.Add("PASS " + name); }
                catch (Exception ex) { report.Add("FAIL " + name + ": " + ex.Message); failed++; }
                Console.WriteLine(report[^1]);
            }
            await Check("Committed Korean + Space in real WPF input", async () => { await Reset("ㅈㅅ"); Tap(32); await Task.Delay(450); return box.Text == "안녕하세요. "; });
            await Check("ASCII + Space", async () => { await Reset("!x"); Tap(32); await Task.Delay(450); return box.Text == "expanded "; });
            await Check("Word boundary prevents false match", async () => { await Reset("prefix!x"); Tap(32); await Task.Delay(350); return box.Text == "prefix!x "; });
            await Check("Multiline Unicode expansion", async () => { await Reset("/sig"); Tap(32); await Task.Delay(500); return box.Text.Replace("\r\n", "\n") == "감사합니다.\n홍길동 드림 "; });
            await Check("Pause passes Space unchanged", async () => { engine.Paused = true; await Reset("!x"); Tap(32); await Task.Delay(300); var ok = box.Text == "!x "; engine.Paused = false; return ok; });
            await Check("Fast subsequent input cancels stale replacement", async () => { await Reset("!x"); Tap(32); Tap(0x41); await Task.Delay(350); return box.Text.StartsWith("!x "); });
            await Check("Password is not replaced", async () => { password.Password = "!x"; password.Focus(); await Task.Delay(150); Tap(0x23); Tap(32); await Task.Delay(350); return password.Password == "!x "; });
            await Check("Read-only input is skipped", async () => { await Reset("!x "); box.IsReadOnly = true; Tap(32); await Task.Delay(350); return box.Text == "!x "; });
            await Check("App exclusion passes unchanged", async () => { engine.Configure(entries, settings with { ExcludedApps = System.Diagnostics.Process.GetCurrentProcess().ProcessName }); await Reset("!x"); Tap(32); await Task.Delay(350); var ok = box.Text == "!x "; engine.Configure(entries, settings); return ok; });
            await Check("Disabled snippet passes unchanged", async () => { engine.Configure(entries.Select(x => x with { Enabled = false }), settings); await Reset("!x"); Tap(32); await Task.Delay(350); var ok = box.Text == "!x "; engine.Configure(entries, settings); return ok; });
            // Switch only this temporary test window's input language. Restore it afterwards.
            var original = InputLanguageManager.Current.CurrentInputLanguage;
            try
            {
                InputLanguageManager.Current.CurrentInputLanguage = System.Globalization.CultureInfo.GetCultureInfo("en-US");
                ActivateKeyboardLayout(LoadKeyboardLayout("00000409", 0), 0);
                Console.WriteLine("LAYOUT English " + GetKeyboardLayout(0).ToString("X"));
                await Check("Enter expands and consumes submit key", async () => { await Reset("!x"); Tap(13); await Task.Delay(400); return box.Text == "expanded"; });
                await Check("Tab expands and keeps focus", async () => { await Reset("!x"); Tap(9); await Task.Delay(400); return box.Text == "expanded" && box.IsKeyboardFocused; });
                await Check("Unmatched Enter is replayed once", async () => { await Reset("no-match"); Tap(13); await Task.Delay(400); return box.Text.Replace("\r\n", "\n") == "no-match\n"; });
                await Check("Unmatched Tab moves focus", async () => { await Reset("no-match"); Tap(9); await Task.Delay(400); return password.IsKeyboardFocused; });
                InputLanguageManager.Current.CurrentInputLanguage = System.Globalization.CultureInfo.GetCultureInfo("ko-KR");
                ActivateKeyboardLayout(LoadKeyboardLayout("00000412", 0), 0);
                Console.WriteLine("LAYOUT Korean " + GetKeyboardLayout(0).ToString("X"));
                await Check("Korean IME physical w,t + Space", async () =>
                {
                    await Reset(""); InputMethod.SetPreferredImeState(box, InputMethodState.On); InputMethod.Current.ImeState = InputMethodState.On;
                    Console.WriteLine("IME " + InputMethod.Current.ImeState + " " + InputLanguageManager.Current.CurrentInputLanguage);
                    await Task.Delay(300); Tap(0x57); await Task.Delay(100); Tap(0x54); await Task.Delay(100); Tap(32); await Task.Delay(650);
                    return box.Text == "안녕하세요. ";
                });
            }
            catch (Exception ex) { report.Add("FAIL Input language setup: " + ex.Message); failed++; }
            finally { InputLanguageManager.Current.CurrentInputLanguage = original; }
            var path = args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "integration-results.txt");
            File.WriteAllLines(path, report); app.Shutdown(failed == 0 ? 0 : 1);
        };
        app.Run(window); return failed == 0 ? 0 : 1;
    }
}
