using System.Text;

namespace AikaPanel;

public sealed class PanelConfig
{
    public string DataDir { get; set; } = "";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8099;
    public string Password { get; set; } = "";
    // Pasta UI do client (vitrine cifrada da Loja de Cash). Vazio = nao sincroniza.
    public string ClientUIDir { get; set; } = "";
}

/// <summary>Resultado de um save da Loja de Cash (server cru + sync client cifrado).</summary>
public sealed class CashSaveResult
{
    public string ServerBackup { get; set; } = "";
    public bool ClientConfigured { get; set; }
    public bool ClientSynced { get; set; }
    public string? ClientBackup { get; set; }
    public string ClientMessage { get; set; } = "";
}

/// <summary>Estado de sincronizacao entre Data\PI.bin (cru) e UI\PI.bin (cifrado).</summary>
public sealed class CashSyncStatus
{
    public bool ClientConfigured { get; set; }
    public string ClientPath { get; set; } = "";
    public bool ClientExists { get; set; }
    public bool InSync { get; set; }
    public string Mensagem { get; set; } = "";
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
    private readonly string _clientPath; // UI\PI.bin (cifrado) ou "" se nao configurado
    private readonly object _lock = new();
    public CashRepository(PanelConfig cfg)
    {
        _path = Path.Combine(cfg.DataDir, "PI.bin");
        _clientPath = string.IsNullOrWhiteSpace(cfg.ClientUIDir) ? "" : Path.Combine(cfg.ClientUIDir, "PI.bin");
    }

    public string FilePath => _path;
    public string ClientPath => _clientPath;
    public bool ClientConfigured => _clientPath.Length > 0;
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

    /// <summary>
    /// Backs up + applies edit + writes Data\PI.bin (cru). Em seguida sincroniza a vitrine do
    /// client gravando UI\PI.bin CIFRADO (Key1) a partir dos mesmos bytes. Retorna o resultado.
    /// </summary>
    public CashSaveResult Update(int slot, CashEdit edit)
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
            var res = new CashSaveResult { ServerBackup = Path.GetFileName(bak), ClientConfigured = ClientConfigured };
            SyncClientLocked(bytes, res);
            return res;
        }
    }

    /// <summary>Grava a vitrine do client (UI\PI.bin cifrado) a partir dos bytes crus dados.</summary>
    private void SyncClientLocked(byte[] rawBytes, CashSaveResult res)
    {
        if (!ClientConfigured) { res.ClientMessage = "Sync do client desativado (ClientUIDir vazio)."; return; }
        try
        {
            var enc = CashCrypto.Encrypt(rawBytes);
            var dir = Path.GetDirectoryName(_clientPath)!;
            if (!Directory.Exists(dir)) { res.ClientMessage = $"Pasta do client nao existe: {dir}"; return; }
            if (File.Exists(_clientPath)) res.ClientBackup = Path.GetFileName(Backup.Make(_clientPath));
            File.WriteAllBytes(_clientPath, enc);
            res.ClientSynced = true;
            res.ClientMessage = "Vitrine do client (UI\\PI.bin) sincronizada.";
        }
        catch (Exception ex) { res.ClientMessage = "Falha ao sincronizar client: " + ex.Message; }
    }

    /// <summary>Forca a sincronizacao do client a partir do Data\PI.bin atual.</summary>
    public CashSaveResult SyncClient()
    {
        lock (_lock)
        {
            var bytes = ReadAll();
            var res = new CashSaveResult { ServerBackup = "", ClientConfigured = ClientConfigured };
            SyncClientLocked(bytes, res);
            return res;
        }
    }

    /// <summary>Em sync quando encrypt(Data\PI.bin) == UI\PI.bin byte-a-byte.</summary>
    public CashSyncStatus SyncStatus()
    {
        var st = new CashSyncStatus { ClientConfigured = ClientConfigured, ClientPath = _clientPath };
        if (!ClientConfigured) { st.Mensagem = "Sync do client desativado (ClientUIDir vazio)."; return st; }
        st.ClientExists = File.Exists(_clientPath);
        if (!st.ClientExists) { st.Mensagem = "UI\\PI.bin do client nao encontrado."; return st; }
        try
        {
            var enc = CashCrypto.Encrypt(ReadAll());
            var cur = File.ReadAllBytes(_clientPath);
            st.InSync = enc.Length == cur.Length && enc.AsSpan().SequenceEqual(cur);
            st.Mensagem = st.InSync ? "Server PI ↔ Client PI sincronizados."
                                    : "Dessincronizados — salve ou clique em Sincronizar.";
        }
        catch (Exception ex) { st.Mensagem = "Erro ao verificar sync: " + ex.Message; }
        return st;
    }
}

public sealed class ItemRepository
{
    private readonly string _path;
    private readonly object _lock = new();
    public ItemRepository(PanelConfig cfg) => _path = Path.Combine(cfg.DataDir, "ItemList.bin");

    public string FilePath => _path;
    public bool Exists => File.Exists(_path);

    public byte[] ReadAll()
    {
        var bytes = File.ReadAllBytes(_path);
        int rem = bytes.Length % ItemList.RecordSize;
        if (rem != 0 && rem != ItemList.TrailerSize)
            throw new InvalidOperationException(
                $"ItemList.bin tem {bytes.Length} bytes (resto {rem}). Esperado multiplo de {ItemList.RecordSize} (+{ItemList.TrailerSize} de trailer). Arquivo invalido.");
        return bytes;
    }

    public List<ItemEntry> DecodeAll()
    {
        var bytes = ReadAll();
        int count = bytes.Length / ItemList.RecordSize;
        var list = new List<ItemEntry>(count);
        for (int i = 0; i < count; i++)
            list.Add(ItemList.Decode(bytes.AsSpan(i * ItemList.RecordSize, ItemList.RecordSize), i));
        return list;
    }

    public (bool ok, int total, int firstMismatch) SelfTest() => ItemList.SelfTest(ReadAll());

    public string Update(int id, ItemEdit edit)
    {
        lock (_lock)
        {
            var bytes = ReadAll();
            int count = bytes.Length / ItemList.RecordSize;
            if (id < 0 || id >= count)
                throw new ArgumentOutOfRangeException(nameof(id), $"ID {id} fora do intervalo (0..{count - 1}).");
            var bak = Backup.Make(_path);
            ItemList.ApplyEdit(bytes.AsSpan(id * ItemList.RecordSize, ItemList.RecordSize), edit);
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
