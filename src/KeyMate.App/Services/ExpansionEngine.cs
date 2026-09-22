using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using KeyMate.Core;
using KeyMate.Native;

namespace KeyMate.Services;

public sealed class ExpansionEngine : IDisposable
{
    private readonly Thread hookThread;
    private readonly Win32.HookProc keyboardCallback, mouseCallback;
    private Dispatcher? dispatcher;
    private nint keyboardHook, mouseHook;
    private long generation;
    private int busy;
    private volatile bool disposed;
    private volatile bool paused;
    private Snippet[] snippets = [];
    private Settings settings = new();
    private Pending? pending;
    private readonly HashSet<ushort> consumeKeyUp = [];
    private readonly bool diagnosticMode;
    public event Action<string>? Replaced;
    public event Action<string>? Status;
    private sealed record Pending(ushort Key, nint Foreground, nint Focus, long Generation, long Started);
    public bool Paused { get => paused; set { paused = value; Interlocked.Increment(ref generation); } }
    public ExpansionEngine(bool diagnosticMode = false)
    {
        this.diagnosticMode = diagnosticMode;
        keyboardCallback = OnKeyboard; mouseCallback = OnMouse;
        using var ready = new ManualResetEventSlim();
        Exception? error = null;
        hookThread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            try
            {
                keyboardHook = Win32.SetWindowsHookEx(13, keyboardCallback, Win32.GetModuleHandle(null), 0);
                mouseHook = Win32.SetWindowsHookEx(14, mouseCallback, Win32.GetModuleHandle(null), 0);
                if (keyboardHook == 0 || mouseHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch (Exception ex) { error = ex; }
            ready.Set();
            if (error is null) Dispatcher.Run();
            if (keyboardHook != 0) Win32.UnhookWindowsHookEx(keyboardHook);
            if (mouseHook != 0) Win32.UnhookWindowsHookEx(mouseHook);
        }) { IsBackground = true, Name = "KeyMate keyboard" };
        hookThread.SetApartmentState(ApartmentState.STA); hookThread.Start(); ready.Wait();
        if (error is not null) throw error;
    }
    public void Configure(IEnumerable<Snippet> entries, Settings value)
    { Volatile.Write(ref snippets, entries.ToArray()); Volatile.Write(ref settings, value); Interlocked.Increment(ref generation); }

    private nint OnKeyboard(int code, nint wParam, nint lParam)
    {
        if (code < 0 || disposed) return Win32.CallNextHookEx(keyboardHook, code, wParam, lParam);
        var key = Marshal.PtrToStructure<Win32.Kbd>(lParam);
        if (key.Extra == Win32.OwnInput) return Win32.CallNextHookEx(keyboardHook, code, wParam, lParam);
        var injected = (key.Flags & 0x10) != 0;
        if (injected && !(diagnosticMode && key.Extra == Win32.TestInput))
        { Interlocked.Increment(ref generation); CancelPending(); return Win32.CallNextHookEx(keyboardHook, code, wParam, lParam); }
        var vk = (ushort)key.Vk;
        var up = wParam == 0x101 || wParam == 0x105;
        if (up && consumeKeyUp.Remove(vk)) return 1;
        if (up && pending?.Key == vk) return 1;
        if (up) return Win32.CallNextHookEx(keyboardHook, code, wParam, lParam);
        if (pending is not null) CancelPending();
        var version = Interlocked.Increment(ref generation);
        var config = Volatile.Read(ref settings);
        if (!paused && config.Enabled && !Win32.HasModifiers() &&
            ((vk == 0x20 && config.Space) || (vk == 0x0D && config.Enter) || (vk == 0x09 && config.Tab)))
        {
            var fg = Win32.GetForegroundWindow(); var focus = Win32.FocusWindow(fg);
            Win32.GetWindowThreadProcessId(fg, out var pid);
            if (fg != 0 && focus != 0 && (diagnosticMode || pid != Environment.ProcessId) && Interlocked.CompareExchange(ref busy, 1, 0) == 0)
            {
                var request = new Pending(vk, fg, focus, version, Environment.TickCount64);
                if (vk != 0x20)
                {
                    pending = request;
                    var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(180), DispatcherPriority.Send, (_, _) => { }, dispatcher!);
                    timer.Tick += (_, _) => { timer.Stop(); if (pending == request) { Interlocked.Increment(ref generation); CancelPending(); } };
                    timer.Start();
                }
                _ = ProbeAsync(request, config, Volatile.Read(ref snippets));
                if (vk != 0x20) return 1;
            }
        }
        return Win32.CallNextHookEx(keyboardHook, code, wParam, lParam);
    }
    private bool Valid(Pending p) => !disposed && !paused && Volatile.Read(ref settings).Enabled &&
        Interlocked.Read(ref generation) == p.Generation && Environment.TickCount64 - p.Started < 240 &&
        Win32.GetForegroundWindow() == p.Foreground && Win32.FocusWindow(p.Foreground) == p.Focus;
    private async Task ProbeAsync(Pending request, Settings config, Snippet[] entries)
    {
        ProbeResult? result = null;
        try
        {
            // Space must reach the target and commit the IME before inspecting text.
            await Task.Delay(request.Key == 0x20 ? 35 : 10).ConfigureAwait(false);
            if (Valid(request)) result = TextProbe.Read(request.Foreground, request.Focus, request.Key, entries, config, diagnosticMode, () => Valid(request));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (diagnosticMode) Status?.Invoke("Probe: " + ex.GetType().Name); }
        finally
        {
            Interlocked.Exchange(ref busy, 0);
            if (!disposed && dispatcher is not null)
            {
                var captured = result;
                _ = dispatcher.BeginInvoke(() => Complete(request, captured));
            }
        }
    }
    private void Complete(Pending request, ProbeResult? result)
    {
        if (result is not null && Valid(request) && !Win32.HasModifiers())
        {
            if (pending == request)
            {
                pending = null;
                if (Win32.GetAsyncKeyState(request.Key) < 0) consumeKeyUp.Add(request.Key);
            }
            if (Win32.Replace(result.DeleteCount, result.Replacement)) Replaced?.Invoke(result.Snippet.Shortcut);
            else Status?.Invoke("입력을 전달하지 못했습니다. 대상 앱의 권한을 확인해 주세요.");
        }
        else if (pending == request) CancelPending();
    }
    private void CancelPending()
    {
        var p = pending; pending = null;
        if (p is null) return;
        if (Win32.GetAsyncKeyState(p.Key) < 0) consumeKeyUp.Add(p.Key);
        // Never replay a held delimiter into a different app or a newly focused control.
        if (Win32.GetForegroundWindow() == p.Foreground && Win32.FocusWindow(p.Foreground) == p.Focus) Win32.Tap(p.Key);
    }
    private nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && wParam is not 0x200)
        { Interlocked.Increment(ref generation); CancelPending(); }
        return Win32.CallNextHookEx(mouseHook, code, wParam, lParam);
    }
    public void Dispose()
    {
        if (disposed) return;
        dispatcher?.Invoke(() => { CancelPending(); disposed = true; dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); });
        hookThread.Join(1000);
        GC.KeepAlive(keyboardCallback); GC.KeepAlive(mouseCallback);
    }
}
