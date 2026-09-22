using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using KeyMate.Core;
using KeyMate.Native;

namespace KeyMate.Services;

internal sealed record ProbeResult(Snippet Snippet, string Before, int DeleteCount, string Replacement);

internal static class TextProbe
{
    // Called only from an MTA worker. No document text is persisted or logged.
    public static ProbeResult? Read(nint foreground, nint focus, ushort trigger, Snippet[] snippets, Settings settings,
        bool allowOwnProcess, Func<bool> valid)
    {
        if (!valid()) return null;
        Win32.GetWindowThreadProcessId(foreground, out var pid);
        if (!allowOwnProcess && pid == Environment.ProcessId) return null;
        using var process = Process.GetProcessById((int)pid);
        if (Rules.IsExcluded(process.ProcessName, settings.ExcludedApps)) return null;
        var element = AutomationElement.FocusedElement;
        if (element is null || element.Current.ProcessId != pid || element.Current.IsPassword ||
            !element.Current.IsEnabled || !element.Current.HasKeyboardFocus) return null;
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && ((ValuePattern)value).Current.IsReadOnly) return null;
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var p)) return null;
        var pattern = (TextPattern)p;
        var selection = pattern.GetSelection();
        if (selection.Length != 1 || selection[0].CompareEndpoints(TextPatternRangeEndpoint.Start, selection[0], TextPatternRangeEndpoint.End) != 0) return null;
        var range = selection[0].Clone();
        range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -(Rules.MaxShortcutLength + 3));
        if (range.GetAttributeValue(TextPattern.IsReadOnlyAttribute) is true) return null;
        // Request one extra character and reject truncation. A provider can use larger
        // text units, and a grapheme can contain multiple UTF-16 code units.
        const int readLimit = 512;
        var before = range.GetText(readLimit + 1);
        if (before.Length > readLimit) return null;
        if (trigger == 0x20)
        {
            if (!before.EndsWith(' ')) return null;
            before = before[..^1];
        }
        var match = Rules.Match(before, snippets);
        if (match is null) return null;
        // Enter/Tab do not commit an IME composition. Only use them with a Latin keyboard
        // and ASCII shortcuts; Korean expansion is supported after Space commits the IME.
        if (trigger != 0x20)
        {
            var language = (int)Win32.GetKeyboardLayout(Win32.GetWindowThreadProcessId(foreground, out _)) & 0x3ff;
            if (language is 0x12 or 0x11 or 0x04 || match.Shortcut.Any(c => c > 127)) return null;
        }
        // A second caret/text read prevents stale provider snapshots from being applied.
        var again = pattern.GetSelection();
        if (again.Length != 1 || again[0].CompareEndpoints(TextPatternRangeEndpoint.Start, selection[0], TextPatternRangeEndpoint.Start) != 0 ||
            again[0].CompareEndpoints(TextPatternRangeEndpoint.End, selection[0], TextPatternRangeEndpoint.End) != 0 ||
            range.GetText(readLimit + 1) != before + (trigger == 0x20 ? " " : "") ||
            !element.Current.HasKeyboardFocus || !valid()) return null;
        return new(match, before, match.Shortcut.Length + (trigger == 0x20 ? 1 : 0),
            Rules.Expand(match, DateTime.Now) + (trigger == 0x20 ? " " : ""));
    }
}
