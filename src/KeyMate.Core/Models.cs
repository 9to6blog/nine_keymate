using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace KeyMate.Core;

public sealed record Snippet
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Shortcut { get; init; } = "";
    public string Expansion { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool CaseSensitive { get; init; }
    public bool WordBoundary { get; init; } = true;
    [JsonIgnore] public string Preview => Expansion.Replace("\r", "").Replace("\n", "  ↵  ");
    [JsonIgnore] public string Category => string.IsNullOrWhiteSpace(Description) ? "일반" : Description;
}

public sealed record Settings
{
    public bool Enabled { get; init; } = true;
    public bool Space { get; init; } = true;
    public bool Enter { get; init; }
    public bool Tab { get; init; }
    public bool StartWithWindows { get; init; }
    public string ExcludedApps { get; init; } = "WindowsTerminal.exe; cmd.exe; powershell.exe; pwsh.exe; mstsc.exe";
    public string Theme { get; init; } = "System";
}

public sealed record Backup(int Version, DateTimeOffset ExportedAt, List<Snippet> Snippets);

public static class Rules
{
    public const int MaxShortcutLength = 64;
    public const int MaxExpansionLength = 4000;
    public static string? Validate(Snippet s, IEnumerable<Snippet> existing)
    {
        if (string.IsNullOrWhiteSpace(s.Shortcut) || s.Shortcut.Length > MaxShortcutLength || s.Shortcut.Any(char.IsWhiteSpace))
            return "단축어는 공백 없이 1~64자로 입력해 주세요.";
        if (!s.Shortcut.IsNormalized(NormalizationForm.FormC) || s.Shortcut.Any(c => char.IsControl(c) || char.IsSurrogate(c) ||
                CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
            return "단축어에는 조합이 완료된 한글·영문 등을 사용해 주세요. 이모지·결합 문자는 지원하지 않습니다.";
        if (string.IsNullOrWhiteSpace(s.Expansion) || s.Expansion.Length > MaxExpansionLength)
            return "대치 문구는 1~4,000자로 입력해 주세요.";
        if (s.Expansion.Any(c => char.IsControl(c) && c is not ('\r' or '\n')))
            return "대치 문구에 지원하지 않는 제어 문자가 있습니다.";
        if (s.Description.Length > 80) return "설명은 80자 이내로 입력해 주세요.";
        if (s.Description.Any(char.IsControl)) return "설명은 줄바꿈 없이 입력해 주세요.";
        if (existing.Any(e => e.Id != s.Id && string.Equals(e.Shortcut, s.Shortcut,
                e.CaseSensitive && s.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)))
            return "이미 사용 중인 단축어입니다.";
        return null;
    }

    public static Snippet? Match(string beforeCaret, IEnumerable<Snippet> snippets)
    {
        foreach (var s in snippets.Where(s => s.Enabled).OrderByDescending(s => s.Shortcut.Length))
        {
            if (s.Shortcut.Length == 0 || !beforeCaret.EndsWith(s.Shortcut,
                    s.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)) continue;
            var start = beforeCaret.Length - s.Shortcut.Length;
            if (s.WordBoundary && start > 0 && IsWord(beforeCaret[start - 1])) continue;
            return s;
        }
        return null;
    }
    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_' ||
        CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;

    public static string Expand(Snippet snippet, DateTime now) => snippet.Expansion
        .Replace("{date}", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        .Replace("{time}", now.ToString("HH:mm", CultureInfo.InvariantCulture))
        .Replace("{현재 날짜}", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        .Replace("{현재 시간}", now.ToString("HH:mm", CultureInfo.InvariantCulture));

    public static bool IsExcluded(string processName, string exclusions) => exclusions
        .Split([';', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(x => string.Equals(Path.GetFileNameWithoutExtension(x), Path.GetFileNameWithoutExtension(processName), StringComparison.OrdinalIgnoreCase));

    public static List<Snippet> Examples() =>
    [
        new() { Shortcut = "ㅈㅅ", Expansion = "안녕하세요. 좋은 하루 보내세요.", Description = "인사말" },
        new() { Shortcut = "@@", Expansion = "my@email.com", Description = "이메일", Enabled = false },
        new() { Shortcut = "주소1", Expansion = "여기에 주소를 입력해 주세요.", Description = "주소", Enabled = false },
        new() { Shortcut = "/sig", Expansion = "감사합니다.\n홍길동 드림", Description = "메일 서명" },
        new() { Shortcut = "!date", Expansion = "{date}", Description = "오늘 날짜" },
        new() { Shortcut = "!time", Expansion = "{time}", Description = "현재 시간" }
    ];
}
