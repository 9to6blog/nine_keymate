using System.Runtime.InteropServices;

namespace KeyMate.Native;

internal static class Win32
{
    internal const uint OwnInput = 0x4B4D4154;
    internal const uint TestInput = 0x4B4D5453;
    internal delegate nint HookProc(int code, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] internal struct Kbd { public uint Vk, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiInfo
    { public int Size; public uint Flags; public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; public Rect CaretRect; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion
    { [FieldOffset(0)] public Keyboard Keyboard; [FieldOffset(0)] public Mouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] internal struct Keyboard { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Mouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    internal static nint FocusWindow(nint foreground)
    {
        var info = new GuiInfo { Size = Marshal.SizeOf<GuiInfo>() };
        return GetGUIThreadInfo(GetWindowThreadProcessId(foreground, out _), ref info) ? info.Focus : 0;
    }
    internal static bool HasModifiers() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(vk => GetAsyncKeyState(vk) < 0);
    internal static Input Key(ushort vk, bool up = false) => new() { Type = 1, Data = new() { Keyboard = new() { Vk = vk, Flags = up ? 2u : 0, Extra = OwnInput } } };
    internal static bool Tap(ushort vk) => Send([Key(vk), Key(vk, true)]);
    internal static bool Replace(int backspaces, string value)
    {
        var inputs = new List<Input>();
        for (var i = 0; i < backspaces; i++) { inputs.Add(Key(8)); inputs.Add(Key(8, true)); }
        foreach (var c in value.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (c == '\n')
            {
                // Unicode VK_PACKET control characters are ignored by WPF editors.
                // Shift+Enter is the standard soft line break in document/chat editors.
                inputs.Add(Key(0x10)); inputs.Add(Key(0x0D)); inputs.Add(Key(0x0D, true)); inputs.Add(Key(0x10, true));
                continue;
            }
            var character = c;
            inputs.Add(new() { Type = 1, Data = new() { Keyboard = new() { Scan = character, Flags = 4, Extra = OwnInput } } });
            inputs.Add(new() { Type = 1, Data = new() { Keyboard = new() { Scan = character, Flags = 6, Extra = OwnInput } } });
        }
        return Send(inputs.ToArray());
    }
    internal static bool Send(Input[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
}
