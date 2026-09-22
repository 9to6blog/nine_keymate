using KeyMate.Core;
using System.Text.Json;
using Microsoft.Data.Sqlite;

var passed = 0;
void Check(string name, Action action) { action(); Console.WriteLine("PASS " + name); passed++; }
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
void Throws(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected invalid data"); }
var greeting = new Snippet { Shortcut = "ㅈㅅ", Expansion = "안녕하세요." };
Check("Korean suffix and boundary", () => { Equal(greeting, Rules.Match("오늘 ㅈㅅ", [greeting])); Equal<Snippet?>(null, Rules.Match("안ㅈㅅ", [greeting])); });
Check("ASCII punctuation boundary", () => Equal(greeting, Rules.Match("(ㅈㅅ", [greeting])));
Check("Unicode combining mark boundary", () => Equal<Snippet?>(null, Rules.Match("a\u0301ㅈㅅ", [greeting])));
Check("Boundary can be disabled", () => { var x = greeting with { WordBoundary = false }; Equal(x, Rules.Match("안ㅈㅅ", [x])); });
Check("Disabled entries are skipped", () => Equal<Snippet?>(null, Rules.Match("ㅈㅅ", [greeting with { Enabled = false }])));
Check("Longest matching shortcut wins", () => { var x = greeting with { Shortcut = "/hello" }; Equal(x, Rules.Match("/hello", [x with { Shortcut = "hello", WordBoundary = false }, x])); });
Check("Case rules", () => { var x = greeting with { Shortcut = "/SIG" }; Equal(x, Rules.Match("/sig", [x])); Equal<Snippet?>(null, Rules.Match("/sig", [x with { CaseSensitive = true }])); });
Check("Date and time placeholders", () => Equal("2026-09-23 15:04", Rules.Expand(greeting with { Expansion = "{date} {time}" }, new(2026, 9, 23, 15, 4, 0))));
Check("Process exclusions exact normalized", () => { Equal(true, Rules.IsExcluded("CMD", "game.exe; cmd.exe")); Equal(false, Rules.IsExcluded("mycmd", "cmd.exe")); });
Check("Reject conflicting duplicate", () => Equal("이미 사용 중인 단축어입니다.", Rules.Validate(greeting with { Id = "another" }, [greeting])));
Check("Validate controls and empty values", () => { Equal(false, Rules.Validate(greeting with { Shortcut = "a b" }, []) is null); Equal(false, Rules.Validate(greeting with { Expansion = "\0" }, []) is null); Equal(false, Rules.Validate(greeting with { Shortcut = "😀" }, []) is null); });

var root = Path.Combine(Path.GetTempPath(), "KeyMate-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var repo = new Repository(Path.Combine(root, "test.db"));
    Check("Initialize examples once", () => { Equal(6, repo.List().Count); Equal(6, new Repository(repo.DatabasePath).List().Count); });
    var sample = new Snippet { Shortcut = "sql'--", Expansion = "첫째 줄\n둘째 줄 😀", Description = "Round trip" };
    Check("Persist Unicode multiline and SQL characters", () => { repo.Save(sample); Equal(sample.Expansion, repo.List().Single(s => s.Id == sample.Id).Expansion); });
    Check("Update preserves identity", () => { repo.Save(sample with { Enabled = false }); Equal(false, repo.List().Single(s => s.Id == sample.Id).Enabled); });
    Check("Preferences survive reopening", () => { var s = new Settings { Theme = "Dark", Tab = true, ExcludedApps = "foo.exe" }; repo.SaveSettings(s); Equal(s, new Repository(repo.DatabasePath).LoadSettings()); });
    var json = Path.Combine(root, "backup.json");
    Check("Backup round trip preserves entries", () => { repo.Export(json); Equal(7, repo.Import(json)); Equal(7, repo.List().Count); });
    Check("Import rejects malformed batch without partial writes", () =>
    {
        var count = repo.List().Count;
        File.WriteAllText(json, JsonSerializer.Serialize(new Backup(1, DateTimeOffset.Now, [new() { Shortcut = "newone", Expansion = "good" }, new() { Shortcut = "bad shortcut", Expansion = "bad" }])));
        Throws(() => repo.Import(json)); Equal(count, repo.List().Count); Equal(false, repo.List().Any(s => s.Shortcut == "newone"));
    });
    Check("Import cannot hijack unrelated IDs", () =>
    {
        File.WriteAllText(json, JsonSerializer.Serialize(new Backup(1, DateTimeOffset.Now, [sample with { Shortcut = "different" }])));
        repo.Import(json); Equal(true, repo.List().Any(s => s.Shortcut == sample.Shortcut)); Equal(true, repo.List().Any(s => s.Shortcut == "different"));
    });
    Check("Delete last entry does not recreate samples", () => { foreach (var s in repo.List()) repo.Delete(s.Id); Equal(0, new Repository(repo.DatabasePath).List().Count); });
}
finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
Console.WriteLine($"{passed} checks passed.");
