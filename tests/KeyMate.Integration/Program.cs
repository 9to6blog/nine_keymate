using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Automation;
using System.Diagnostics;
using KeyMate.Core;
using KeyMate.Services;

internal static class Program
{
    [ComImport, Guid("71c6e74c-0f28-11d8-a82a-00065b84435c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IProfileManager
    {
        [PreserveSig] int ActivateProfile(uint type, ushort language, in Guid clsid, in Guid profile, nint layout, uint flags);
    }
    private const uint Marker = 0x4B4D5453;
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public Union Data; }
    [StructLayout(LayoutKind.Explicit)] private struct Union { [FieldOffset(0)] public Kbd Key; [FieldOffset(0)] public Mouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct Kbd { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, Input[] input, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);
    [DllImport("user32.dll")] private static extern nint ActivateKeyboardLayout(nint layout, uint flags);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    private static readonly List<string> report = [];
    private static nint testWindow;
    private static nint[] LoadedLayouts()
    {
        var layouts = new nint[GetKeyboardLayoutList(0, null)];
        var count = GetKeyboardLayoutList(layouts.Length, layouts);
        return layouts.Take(count).ToArray();
    }
    private static bool UseExistingLayout(ushort language)
    {
        var layout = LoadedLayouts().FirstOrDefault(hkl => ((long)hkl & 0xffff) == language);
        if (layout == 0) return false;
        ActivateKeyboardLayout(layout, 0);
        return GetKeyboardLayout(0) == layout;
    }
    private static void Tap(ushort key)
    {
        if (GetForegroundWindow() != testWindow) throw new Exception($"Test window lost foreground ({GetForegroundWindow():X} vs {testWindow:X}); input was not sent");
        var scan = (ushort)MapVirtualKey(key, 0);
        Input[] keys = [new() { Type = 1, Data = new() { Key = new() { Vk = key, Scan = scan, Extra = Marker } } }, new() { Type = 1, Data = new() { Key = new() { Vk = key, Scan = scan, Flags = 2, Extra = Marker } } }];
        if (SendInput(2, keys, Marshal.SizeOf<Input>()) != 2) throw new Exception("SendInput failed");
    }
    [STAThread] private static int Main(string[] args)
    {
        var originalForeground = GetForegroundWindow();
        var app = new System.Windows.Application();
        var box = new TextBox { AcceptsReturn = true, MinHeight = 150, FontSize = 24, Margin = new Thickness(20) };
        AutomationProperties.SetAutomationId(box, "ExternalInput");
        var password = new PasswordBox { Margin = new Thickness(20) };
        var other = new TextBox { Margin = new Thickness(20) };
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = "KeyMate integration test · temporary isolated input" }); panel.Children.Add(box); panel.Children.Add(password); panel.Children.Add(other);
        var window = new Window { Title = "KeyMate integration test", Content = panel, Width = 700, Height = 410, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
        var failed = 0;
        if (args.Contains("--host"))
        {
            window.Loaded += (_, _) =>
            {
                box.Text = "ㅈㅅ"; box.CaretIndex = 2; box.Focus();
            };
            app.Run(window); return 0;
        }
        window.Loaded += async (_, _) =>
        {
            testWindow = new WindowInteropHelper(window).Handle;
            var originalLayout = GetKeyboardLayout(0);
            var originalLayouts = LoadedLayouts().OrderBy(hkl => (long)hkl).ToArray();
            var originalImeState = InputMethod.Current.ImeState;
            var originalConversion = InputMethod.Current.ImeConversionMode;
            // Never load a new layout: on Windows 8+ that can leave a US keyboard
            // in the user's session even after this test process exits.
            if (!UseExistingLayout(0x0409)) InputMethod.SetPreferredImeState(box, InputMethodState.Off);
            using var engine = new ExpansionEngine(true);
            engine.Status += message => Console.WriteLine("DIAGNOSTIC " + message);
            var entries = new[] { new Snippet { Shortcut = "ㅈㅅ", Expansion = "안녕하세요." }, new Snippet { Shortcut = "안녕", Expansion = "반갑습니다." }, new Snippet { Shortcut = "/sig", Expansion = "감사합니다.\n홍길동 드림" }, new Snippet { Shortcut = "!x", Expansion = "expanded" } };
            var settings = new Settings { ExcludedApps = "", Enter = true, Tab = true };
            engine.Configure(entries, settings);
            async Task Reset(string text)
            {
                var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                var ownThread = GetCurrentThreadId();
                var attached = foregroundThread != 0 && foregroundThread != ownThread && AttachThreadInput(ownThread, foregroundThread, true);
                try { SetForegroundWindow(testWindow); window.Activate(); box.Focus(); }
                finally { if (attached) AttachThreadInput(ownThread, foregroundThread, false); }
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
            // Use only layouts that were already installed; skip unavailable coverage.
            try
            {
                if (UseExistingLayout(0x0409))
                {
                    await Check("Enter expands and consumes submit key", async () => { await Reset("!x"); Tap(13); await Task.Delay(400); return box.Text == "expanded"; });
                    await Check("Tab expands and keeps focus", async () => { await Reset("!x"); Tap(9); await Task.Delay(400); return box.Text == "expanded" && box.IsKeyboardFocused; });
                }
                else
                {
                    report.Add("SKIP Enter expansion: no existing US keyboard (not added)");
                    report.Add("SKIP Tab expansion: no existing US keyboard (not added)");
                }
                await Check("Unmatched Enter is replayed once", async () => { await Reset("no-match"); Tap(13); await Task.Delay(400); return box.Text.Replace("\r\n", "\n") == "no-match\n"; });
                await Check("Unmatched Tab moves focus", async () => { await Reset("no-match"); Tap(9); await Task.Delay(400); return password.IsKeyboardFocused; });
                if (!UseExistingLayout(0x0412)) throw new InvalidOperationException("The Korean IME must already be available; this test does not install keyboards.");
                var profileManager = (IProfileManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("33c53a50-f456-4884-b049-85fd643ecfed"))!)!;
                var hr = profileManager.ActivateProfile(1, 0x412, new Guid("a028ae76-01b1-46c2-99c4-acd9858ae02f"), new Guid("b5fe1f02-d5f2-4445-9c03-c568f23c99a1"), 0, 0);
                Console.WriteLine("TSF activation " + hr.ToString("X"));
                Marshal.ReleaseComObject(profileManager);
                Console.WriteLine("LAYOUT Korean " + GetKeyboardLayout(0).ToString("X"));
                await Check("Korean IME physical w,t + Space", async () =>
                {
                    await Reset(""); InputMethod.SetPreferredImeState(box, InputMethodState.On); InputMethod.Current.ImeState = InputMethodState.On;
                    InputMethod.SetPreferredImeConversionMode(box, ImeConversionModeValues.Native); InputMethod.Current.ImeConversionMode = ImeConversionModeValues.Native;
                    Console.WriteLine("IME " + InputMethod.Current.ImeState + " " + InputLanguageManager.Current.CurrentInputLanguage);
                    await Task.Delay(300); Tap(0x57); await Task.Delay(100); Tap(0x54); await Task.Delay(100); Tap(32); await Task.Delay(650);
                    return box.Text == "안녕하세요. ";
                });
                await Check("Korean IME composed syllables + Space", async () =>
                {
                    await Reset("");
                    foreach (var key in new ushort[] { 0x44, 0x4B, 0x53, 0x53, 0x55, 0x44 }) { Tap(key); await Task.Delay(75); }
                    Tap(32); await Task.Delay(600); return box.Text == "반갑습니다. ";
                });
                await Check("Korean Enter is passed through without expansion", async () =>
                { await Reset("ㅈㅅ"); Tap(13); await Task.Delay(400); return box.Text.Replace("\r\n", "\n") == "ㅈㅅ\n"; });
            }
            catch (Exception ex) { report.Add("FAIL Input language setup: " + ex.Message); failed++; }
            finally
            {
                InputMethod.SetPreferredImeState(box, originalImeState);
                InputMethod.SetPreferredImeConversionMode(box, originalConversion);
                ActivateKeyboardLayout(originalLayout, 0);
                InputMethod.Current.ImeState = originalImeState; InputMethod.Current.ImeConversionMode = originalConversion;
            }
            using (var child = Process.Start(new ProcessStartInfo("dotnet")
            {
                ArgumentList = { typeof(Program).Assembly.Location, "--host" }, UseShellExecute = false, CreateNoWindow = true
            })!)
            {
                try
                {
                    for (var i = 0; i < 30 && child.MainWindowHandle == 0; i++) { await Task.Delay(100); child.Refresh(); }
                    if (child.MainWindowHandle == 0) throw new Exception("External input host did not start");
                    testWindow = child.MainWindowHandle;
                    var foreignThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                    var ownThread = GetCurrentThreadId();
                    var attached = foreignThread != ownThread && AttachThreadInput(ownThread, foreignThread, true);
                    try { SetForegroundWindow(testWindow); } finally { if (attached) AttachThreadInput(ownThread, foreignThread, false); }
                    async Task<string> ReadExternal() => await Task.Run(() =>
                    {
                        var input = AutomationElement.FromHandle(testWindow).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "ExternalInput"));
                        return ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
                    });
                    await Check("Cross-process Korean + Space", async () => { await Task.Delay(250); Tap(32); await Task.Delay(500); return await ReadExternal() == "안녕하세요. "; });
                    await Check("Cross-process multiline expansion", async () =>
                    {
                        await Task.Run(() =>
                        {
                            var input = AutomationElement.FromHandle(testWindow).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "ExternalInput"));
                            ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).SetValue("/sig"); input.SetFocus();
                        });
                        Tap(0x23); await Task.Delay(100); Tap(32); await Task.Delay(500);
                        return (await ReadExternal()).Replace("\r\n", "\n") == "감사합니다.\n홍길동 드림 ";
                    });
                }
                catch (Exception ex) { report.Add("FAIL External input host: " + ex.Message); failed++; }
                finally { child.CloseMainWindow(); if (!child.WaitForExit(2000)) child.Kill(); testWindow = new WindowInteropHelper(window).Handle; }
            }
            await Check("Installed keyboard layouts remain unchanged", () => Task.FromResult(originalLayouts.SequenceEqual(LoadedLayouts().OrderBy(hkl => (long)hkl))));
            var path = args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "integration-results.txt");
            File.WriteAllLines(path, report); app.Shutdown(failed == 0 ? 0 : 1);
        };
        app.Run(window); SetForegroundWindow(originalForeground); return failed == 0 ? 0 : 1;
    }
}
