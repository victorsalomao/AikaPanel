using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace AikaPanel;

/// <summary>MySQL connection settings for the caelite account/character DB.</summary>
public sealed class DbConfig
{
    public string Server { get; set; } = "localhost";
    public uint Port { get; set; } = 3306;
    public string Database { get; set; } = "caelite";
    public string User { get; set; } = "root";
    public string Password { get; set; } = "";
    public bool Configured => Server.Length > 0 && Database.Length > 0 && User.Length > 0;

    public string ConnString =>
        new MySqlConnectionStringBuilder
        {
            Server = Server, Port = Port, Database = Database, UserID = User, Password = Password,
            ConnectionTimeout = 8, DefaultCommandTimeout = 30, AllowUserVariables = true,
        }.ConnectionString;

    /// <summary>Resolves DB settings: appsettings "Mysql" wins, else parse Bin\AikaServer.ini [MySQL].</summary>
    public static DbConfig Resolve(PanelConfig cfg)
    {
        var c = new DbConfig();
        // 1) AikaServer.ini next to Bin (DataDir is ...\Bin\Data)
        try
        {
            var ini = Path.GetFullPath(Path.Combine(cfg.DataDir, "..", "AikaServer.ini"));
            if (File.Exists(ini)) ParseIni(ini, c);
        }
        catch { /* ignore, fall back to defaults/appsettings */ }
        // 2) appsettings overrides
        if (cfg.MysqlServer.Length > 0) c.Server = cfg.MysqlServer;
        if (cfg.MysqlPort > 0) c.Port = cfg.MysqlPort;
        if (cfg.MysqlDatabase.Length > 0) c.Database = cfg.MysqlDatabase;
        if (cfg.MysqlUser.Length > 0) c.User = cfg.MysqlUser;
        if (cfg.MysqlPassword.Length > 0) c.Password = cfg.MysqlPassword;
        return c;
    }

    private static void ParseIni(string path, DbConfig c)
    {
        string section = "";
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
            if (line.StartsWith("[") && line.EndsWith("]")) { section = line[1..^1].Trim().ToUpperInvariant(); continue; }
            if (section != "MYSQL") continue;
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            var k = line[..eq].Trim().ToLowerInvariant();
            var v = line[(eq + 1)..].Trim();
            switch (k)
            {
                case "server": c.Server = v; break;
                case "port": if (uint.TryParse(v, out var p)) c.Port = p; break;
                case "database": c.Database = v; break;
                case "username": c.User = v; break;
                case "password": c.Password = v; break;
            }
        }
    }
}

public sealed class Db
{
    private readonly DbConfig _cfg;
    public Db(DbConfig cfg) { _cfg = cfg; }
    public DbConfig Config => _cfg;

    public MySqlConnection Open()
    {
        var con = new MySqlConnection(_cfg.ConnString);
        con.Open();
        return con;
    }

    /// <summary>Quick connectivity probe for the UI badge.</summary>
    public (bool ok, string msg) Ping()
    {
        try
        {
            using var con = Open();
            using var cmd = new MySqlCommand("SELECT 1", con);
            cmd.ExecuteScalar();
            return (true, $"{_cfg.User}@{_cfg.Server}:{_cfg.Port}/{_cfg.Database}");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public static string Md5Hex(string text)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(text ?? ""));
        var sb = new StringBuilder(32);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}

/// <summary>Resolves item names from Data\ItemList.bin (read-only, cached). Shared by editors.</summary>
public sealed class ItemNames
{
    private readonly string _path;
    private byte[]? _bytes;
    private DateTime _stamp;
    private static readonly Encoding Cp = PiBin.Win1252;

    public ItemNames(PanelConfig cfg) => _path = Path.Combine(cfg.DataDir, "ItemList.bin");

    private void Ensure()
    {
        if (!File.Exists(_path)) { _bytes = null; return; }
        var s = File.GetLastWriteTimeUtc(_path);
        if (_bytes != null && s == _stamp) return;
        _bytes = File.ReadAllBytes(_path);
        _stamp = s;
    }

    public string Name(int id)
    {
        Ensure();
        if (_bytes == null) return "";
        int o = id * ItemList.RecordSize;
        if (id <= 0 || o + 64 > _bytes.Length) return "";
        var raw = _bytes.AsSpan(o, 64);
        int z = raw.IndexOf((byte)0); if (z < 0) z = 64;
        return Cp.GetString(raw.Slice(0, z));
    }
}

/// <summary>Writes a JSON snapshot of DB rows before an edit, so changes are reversible.</summary>
public static class DbBackup
{
    public static string Save(PanelConfig cfg, string label, object data)
    {
        var dir = Path.GetFullPath(Path.Combine(cfg.DataDir, "..", "panel_backups"));
        Directory.CreateDirectory(dir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var file = Path.Combine(dir, $"{label}.{stamp}.json");
        for (int i = 1; File.Exists(file); i++) file = Path.Combine(dir, $"{label}.{stamp}-{i}.json");
        File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(data,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return Path.GetFileName(file);
    }
}
