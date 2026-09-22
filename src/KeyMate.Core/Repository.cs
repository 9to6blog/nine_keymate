using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace KeyMate.Core;

public sealed class Repository
{
    private readonly string connectionString;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string DatabasePath { get; }
    public Repository(string path)
    {
        DatabasePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS snippets (id TEXT PRIMARY KEY, shortcut TEXT NOT NULL, expansion TEXT NOT NULL,
              description TEXT NOT NULL, enabled INTEGER NOT NULL, case_sensitive INTEGER NOT NULL, word_boundary INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS preferences (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            """;
        cmd.ExecuteNonQuery();
        using var check = c.CreateCommand();
        check.CommandText = "SELECT value FROM preferences WHERE key='initialized'";
        if (check.ExecuteScalar() is null)
        {
            using var tx = c.BeginTransaction();
            foreach (var s in Rules.Examples()) Write(c, tx, s);
            using var mark = c.CreateCommand();
            mark.Transaction = tx;
            mark.CommandText = "INSERT INTO preferences VALUES('initialized','1')";
            mark.ExecuteNonQuery();
            tx.Commit();
        }
    }
    private SqliteConnection Open() { var c = new SqliteConnection(connectionString); c.Open(); return c; }
    public List<Snippet> List()
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM snippets ORDER BY rowid";
        using var r = cmd.ExecuteReader();
        var result = new List<Snippet>();
        while (r.Read()) result.Add(new Snippet { Id = r.GetString(0), Shortcut = r.GetString(1), Expansion = r.GetString(2),
            Description = r.GetString(3), Enabled = r.GetBoolean(4), CaseSensitive = r.GetBoolean(5), WordBoundary = r.GetBoolean(6) });
        return result;
    }
    public void Save(Snippet s)
    {
        var error = Rules.Validate(s, List());
        if (error is not null) throw new InvalidDataException(error);
        using var c = Open(); Write(c, null, s);
    }
    private static void Write(SqliteConnection c, SqliteTransaction? tx, Snippet s)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO snippets VALUES($id,$shortcut,$expansion,$description,$enabled,$case,$boundary)
            ON CONFLICT(id) DO UPDATE SET shortcut=$shortcut,expansion=$expansion,description=$description,
            enabled=$enabled,case_sensitive=$case,word_boundary=$boundary
            """;
        cmd.Parameters.AddWithValue("$id", s.Id); cmd.Parameters.AddWithValue("$shortcut", s.Shortcut);
        cmd.Parameters.AddWithValue("$expansion", s.Expansion); cmd.Parameters.AddWithValue("$description", s.Description);
        cmd.Parameters.AddWithValue("$enabled", s.Enabled); cmd.Parameters.AddWithValue("$case", s.CaseSensitive);
        cmd.Parameters.AddWithValue("$boundary", s.WordBoundary); cmd.ExecuteNonQuery();
    }
    public void Delete(string id)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM snippets WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery();
    }
    public Settings LoadSettings()
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT value FROM preferences WHERE key='settings'";
        return cmd.ExecuteScalar() is string value ? JsonSerializer.Deserialize<Settings>(value, Json) ?? new() : new();
    }
    public void SaveSettings(Settings settings)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO preferences VALUES('settings',$value) ON CONFLICT(key) DO UPDATE SET value=$value";
        cmd.Parameters.AddWithValue("$value", JsonSerializer.Serialize(settings, Json)); cmd.ExecuteNonQuery();
    }
    public void Export(string path)
    {
        var content = JsonSerializer.Serialize(new Backup(1, DateTimeOffset.Now, List()), Json);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, true);
    }
    // Validate the whole file before starting a single all-or-nothing transaction.
    // Merge by shortcut; never delete existing entries on import.
    public int Import(string path)
    {
        if (new FileInfo(path).Length > 10 * 1024 * 1024) throw new InvalidDataException("백업 파일은 10MB 이하여야 합니다.");
        var backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(path), Json);
        if (backup is null || backup.Version != 1 || backup.Snippets is null || backup.Snippets.Count > 5000)
            throw new InvalidDataException("지원하지 않는 KeyMate 백업 형식입니다.");
        var current = List(); var incoming = new List<Snippet>();
        foreach (var item in backup.Snippets)
        {
            if (item is null || item.Shortcut is null || item.Expansion is null || item.Description is null)
                throw new InvalidDataException("백업 항목의 필수 값이 비어 있습니다.");
            var s = item with { Id = Guid.NewGuid().ToString("N") };
            var error = Rules.Validate(s, incoming);
            if (error is not null) throw new InvalidDataException(error);
            incoming.Add(s);
        }
        var merged = new List<Snippet>(current);
        var writes = new List<Snippet>();
        foreach (var item in incoming)
        {
            var old = merged.FirstOrDefault(s => string.Equals(s.Shortcut, item.Shortcut,
                s.CaseSensitive && item.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
            var s = item with { Id = old?.Id ?? item.Id };
            var error = Rules.Validate(s, merged);
            if (error is not null) throw new InvalidDataException(error);
            merged.RemoveAll(x => x.Id == s.Id); merged.Add(s); writes.Add(s);
        }
        using var c = Open(); using var tx = c.BeginTransaction();
        foreach (var s in writes) Write(c, tx, s);
        tx.Commit(); return writes.Count;
    }
}
