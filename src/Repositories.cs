using System.Text;

namespace AikaPanel;

public sealed class PanelConfig
{
    public string DataDir { get; set; } = "";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8099;
    public string Password { get; set; } = "";
}

public static class Backup
{
    /// <summary>Copies file to a timestamped .bak next to it. Returns backup path.</summary>
    public static string Make(string path)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var bak = $"{path}.{stamp}.bak";
        File.Copy(path, bak, overwrite: false);
        return bak;
    }
}

public sealed class CashRepository
{
    private readonly string _path;
    private readonly object _lock = new();
    public CashRepository(PanelConfig cfg) => _path = Path.Combine(cfg.DataDir, "PI.bin");

    public string FilePath => _path;
    public bool Exists => File.Exists(_path);

    public byte[] ReadAll()
    {
        var bytes = File.ReadAllBytes(_path);
        int rem = bytes.Length % PiBin.RecordSize;
        if (rem != 0 && rem != PiBin.TrailerSize)
            throw new InvalidOperationException(
                $"PI.bin tem {bytes.Length} bytes (resto {rem}). Esperado multiplo de {PiBin.RecordSize} (+{PiBin.TrailerSize} de trailer). Arquivo invalido.");
        return bytes;
    }

    public List<CashItem> DecodeAll()
    {
        var bytes = ReadAll();
        int count = bytes.Length / PiBin.RecordSize;
        var list = new List<CashItem>(count);
        for (int i = 0; i < count; i++)
            list.Add(PiBin.Decode(bytes.AsSpan(i * PiBin.RecordSize, PiBin.RecordSize), i));
        return list;
    }

    public (bool ok, int total, int firstMismatch) SelfTest() => PiBin.SelfTest(ReadAll());

    /// <summary>Backs up, applies edit to a single slot, writes whole file back. Returns backup path.</summary>
    public string Update(int slot, CashEdit edit)
    {
        lock (_lock)
        {
            var bytes = ReadAll();
            int count = bytes.Length / PiBin.RecordSize;
            if (slot < 0 || slot >= count)
                throw new ArgumentOutOfRangeException(nameof(slot), $"Slot {slot} fora do intervalo (0..{count - 1}).");
            var bak = Backup.Make(_path);
            PiBin.ApplyEdit(bytes.AsSpan(slot * PiBin.RecordSize, PiBin.RecordSize), edit);
            File.WriteAllBytes(_path, bytes);
            return bak;
        }
    }
}

public sealed class QuestRepository
{
    // Column names derived from Load.pas InitQuests (authoritative — file is headerless).
    public static readonly string[] Columns =
    {
        "npcid","questid","questtype","questmark",
        "reward1","reward2","reward3","reward4","reward5","reward6",
        "req1","req2","req3","req4","req5",
        "rtype1","rtype2","rtype3","rtype4","rtype5",
        "reqamount1","reqamount2","reqamount3","reqamount4","reqamount5",
        "deleteitem1","deleteitem2","deleteitem3",
        "deleteamount1","deleteamount2","deleteamount3",
        "gold","exp","levelmin"
    };
    public const int ColCount = 34;

    private readonly string _path;
    private readonly object _lock = new();
    public QuestRepository(PanelConfig cfg) => _path = Path.Combine(cfg.DataDir, "Quest", "Quests.csv");

    public string FilePath => _path;
    public bool Exists => File.Exists(_path);

    public List<string[]> Read()
    {
        var rows = new List<string[]>();
        foreach (var raw in File.ReadAllLines(_path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;
            var parts = line.Split(',');
            var row = new string[ColCount];
            for (int i = 0; i < ColCount; i++)
                row[i] = i < parts.Length ? parts[i].Trim() : "0";
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Validates all cells are integers, backs up, writes CSV (CRLF). Returns backup path.</summary>
    public string Write(List<string[]> rows)
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            foreach (var row in rows)
            {
                if (row.Length != ColCount)
                    throw new InvalidOperationException($"Linha com {row.Length} colunas (esperado {ColCount}).");
                var cells = new string[ColCount];
                for (int i = 0; i < ColCount; i++)
                {
                    var v = (row[i] ?? "").Trim();
                    if (v.Length == 0) v = "0";
                    if (!long.TryParse(v, out _))
                        throw new InvalidOperationException($"Valor invalido (nao numerico): '{row[i]}' na coluna {Columns[i]}.");
                    cells[i] = v;
                }
                sb.Append(string.Join(",", cells)).Append("\r\n");
            }
            var bak = Backup.Make(_path);
            File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(false));
            return bak;
        }
    }
}
