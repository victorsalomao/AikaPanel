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
    // Raiz do client (onde ficam ItemList4.bin e SkillData4.bin cifrados v4). Vazio = nao sincroniza.
    public string ClientGameDir { get; set; } = "";
    // MySQL (caelite) — vazio = lido do Bin\AikaServer.ini [MySQL]. Override opcional via appsettings "Mysql".
    public string MysqlServer { get; set; } = "";
    public uint MysqlPort { get; set; }
    public string MysqlDatabase { get; set; } = "";
    public string MysqlUser { get; set; } = "";
    public string MysqlPassword { get; set; } = "";
}

/// <summary>Resultado generico de um sync de arquivo cifrado do client.</summary>
public sealed class ClientSyncResult
{
    public bool Configured { get; set; }
    public bool Synced { get; set; }
    public string? Backup { get; set; }
    public string Mensagem { get; set; } = "";
}

/// <summary>Estado de sync de um arquivo cifrado do client v4 (header + corpo).</summary>
public sealed class ClientSyncStatus
{
    public bool Configured { get; set; }
    public string ClientPath { get; set; } = "";
    public bool Exists { get; set; }
    public string Header { get; set; } = "";
    public bool InSync { get; set; }
    public string Mensagem { get; set; } = "";
}

/// <summary>
/// Sincroniza um arquivo cifrado v4 do client (header de 12 bytes + corpo cifrado) a partir do
/// arquivo CRU do server. Push completo: client = header_existente + Encrypt(serverRaw, key).
/// Preserva o header do client byte-a-byte; so os corpos dos registros mudam.
/// </summary>
public sealed class V4ClientSync
{
    private readonly string _path;
    private readonly byte[] _key;
    public V4ClientSync(string clientPath, byte[] key) { _path = clientPath; _key = key; }

    public bool Configured => _path.Length > 0;
    public string Path => _path;
    public bool Exists => Configured && File.Exists(_path);

    private byte[]? ReadHeader()
    {
        if (!Exists) return null;
        var hdr = new byte[V4Cipher.HeaderLen];
        using var fs = File.OpenRead(_path);
        if (fs.Read(hdr, 0, hdr.Length) != hdr.Length) return null;
        return hdr;
    }

    private static string HeaderTag(byte[] hdr)
    {
        int n = Array.IndexOf(hdr, (byte)0); if (n < 0) n = hdr.Length;
        return System.Text.Encoding.ASCII.GetString(hdr, 0, n);
    }

    /// <summary>Reescreve o client = header existente + Encrypt(serverRaw). Backup antes.</summary>
    public ClientSyncResult Push(byte[] serverRaw)
    {
        var res = new ClientSyncResult { Configured = Configured };
        if (!Configured) { res.Mensagem = "Sync do client desativado."; return res; }
        var hdr = ReadHeader();
        if (hdr == null) { res.Mensagem = $"Arquivo v4 do client nao encontrado: {_path}"; return res; }
        try
        {
            var body = V4Cipher.Encrypt(serverRaw, _key);
            res.Backup = System.IO.Path.GetFileName(Backup.Make(_path));
            using (var fs = File.Create(_path)) { fs.Write(hdr, 0, hdr.Length); fs.Write(body, 0, body.Length); }
            res.Synced = true;
            res.Mensagem = $"Client v4 ({HeaderTag(hdr)}) sincronizado.";
        }
        catch (Exception ex) { res.Mensagem = "Falha ao sincronizar client: " + ex.Message; }
        return res;
    }

    /// <summary>InSync quando client[12:] == Encrypt(serverRaw, key) e o header foi preservado.</summary>
    public ClientSyncStatus Status(byte[] serverRaw)
    {
        var st = new ClientSyncStatus { Configured = Configured, ClientPath = _path };
        if (!Configured) { st.Mensagem = "Sync do client desativado."; return st; }
        st.Exists = File.Exists(_path);
        if (!st.Exists) { st.Mensagem = "Arquivo v4 do client nao encontrado."; return st; }
        try
        {
            var cur = File.ReadAllBytes(_path);
            var hdr = new byte[V4Cipher.HeaderLen];
            Array.Copy(cur, hdr, V4Cipher.HeaderLen);
            st.Header = HeaderTag(hdr);
            var expected = V4Cipher.Encrypt(serverRaw, _key);
            st.InSync = cur.Length == V4Cipher.HeaderLen + expected.Length
                        && cur.AsSpan(V4Cipher.HeaderLen).SequenceEqual(expected);
            st.Mensagem = st.InSync ? $"Server ↔ client ({st.Header}) sincronizados."
                                    : "Dessincronizados — salve ou clique em Sincronizar.";
        }
        catch (Exception ex) { st.Mensagem = "Erro ao verificar sync: " + ex.Message; }
        return st;
    }
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
    /// <summary>Copies file to a timestamped .bak next to it. Returns backup path.
    /// Appends a counter if a backup with the same second already exists (avoids collisions
    /// when two saves/a save+delete happen within the same second).</summary>
    public static string Make(string path)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var bak = $"{path}.{stamp}.bak";
        for (int i = 1; File.Exists(bak); i++) bak = $"{path}.{stamp}-{i}.bak";
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
    private readonly V4ClientSync _client;
    private readonly object _lock = new();
    public ItemRepository(PanelConfig cfg)
    {
        _path = Path.Combine(cfg.DataDir, "ItemList.bin");
        var cp = string.IsNullOrWhiteSpace(cfg.ClientGameDir) ? "" : Path.Combine(cfg.ClientGameDir, "ItemList4.bin");
        _client = new V4ClientSync(cp, V4Cipher.Key1);
    }

    public string FilePath => _path;
    public bool Exists => File.Exists(_path);
    public ClientSyncStatus SyncStatus() => _client.Status(ReadAll());
    public ClientSyncResult SyncClient() { lock (_lock) { return _client.Push(ReadAll()); } }

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

    /// <summary>Backup + edit + grava Data\ItemList.bin (cru), depois sincroniza o ItemList4.bin (cifrado Key1).</summary>
    public (string serverBackup, ClientSyncResult client) Update(int id, ItemEdit edit)
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
            var client = _client.Push(bytes);
            return (Path.GetFileName(bak), client);
        }
    }

    /// <summary>Decodes a single item by id (cheap: reads file once, slices one record).</summary>
    public ItemEntry? GetOne(int id)
    {
        var bytes = ReadAll();
        int count = bytes.Length / ItemList.RecordSize;
        if (id < 0 || id >= count) return null;
        return ItemList.Decode(bytes.AsSpan(id * ItemList.RecordSize, ItemList.RecordSize), id);
    }

    /// <summary>Sets only the gold sell price (SellPrince) of an item, preserving every other field.
    /// Forces TypePriceItem=0 (gold) so the NPC shop charges gold, then re-syncs the client.</summary>
    public (string serverBackup, ClientSyncResult client) UpdateSellPrice(int id, uint sellPrince)
    {
        lock (_lock)
        {
            var cur = GetOne(id) ?? throw new ArgumentOutOfRangeException(nameof(id), $"Item {id} fora do intervalo.");
            var edit = ItemList.ToEdit(cur);
            edit.SellPrince = sellPrince;
            edit.TypePriceItem = 0; // escambo off -> cobra ouro
            return Update(id, edit);
        }
    }
}

public sealed class SkillRepository
{
    private readonly string _path;
    private readonly V4ClientSync _client;
    private readonly object _lock = new();
    public SkillRepository(PanelConfig cfg)
    {
        _path = Path.Combine(cfg.DataDir, "SkillData.bin");
        var cp = string.IsNullOrWhiteSpace(cfg.ClientGameDir) ? "" : Path.Combine(cfg.ClientGameDir, "SkillData4.bin");
        _client = new V4ClientSync(cp, V4Cipher.Key2);
    }

    public string FilePath => _path;
    public bool Exists => File.Exists(_path);
    public ClientSyncStatus SyncStatus() => _client.Status(ReadAll());
    public ClientSyncResult SyncClient() { lock (_lock) { return _client.Push(ReadAll()); } }

    public byte[] ReadAll()
    {
        var bytes = File.ReadAllBytes(_path);
        int rem = bytes.Length % SkillData.RecordSize;
        if (rem != 0 && rem != SkillData.TrailerSize)
            throw new InvalidOperationException(
                $"SkillData.bin tem {bytes.Length} bytes (resto {rem}). Esperado multiplo de {SkillData.RecordSize} (+{SkillData.TrailerSize} de trailer). Arquivo invalido.");
        return bytes;
    }

    public List<SkillEntry> DecodeAll()
    {
        var bytes = ReadAll();
        int count = bytes.Length / SkillData.RecordSize;
        var list = new List<SkillEntry>(count);
        for (int i = 0; i < count; i++)
            list.Add(SkillData.Decode(bytes.AsSpan(i * SkillData.RecordSize, SkillData.RecordSize), i));
        return list;
    }

    public (bool ok, int total, int firstMismatch) SelfTest() => SkillData.SelfTest(ReadAll());

    /// <summary>Backup + edit + grava Data\SkillData.bin (cru), depois sincroniza o SkillData4.bin (cifrado Key2).</summary>
    public (string serverBackup, ClientSyncResult client) Update(int id, SkillEdit edit)
    {
        lock (_lock)
        {
            var bytes = ReadAll();
            int count = bytes.Length / SkillData.RecordSize;
            if (id < 0 || id >= count)
                throw new ArgumentOutOfRangeException(nameof(id), $"ID {id} fora do intervalo (0..{count - 1}).");
            var bak = Backup.Make(_path);
            SkillData.ApplyEdit(bytes.AsSpan(id * SkillData.RecordSize, SkillData.RecordSize), edit);
            File.WriteAllBytes(_path, bytes);
            var client = _client.Push(bytes);
            return (Path.GetFileName(bak), client);
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

public sealed class TitleRepository
{
    private readonly string _path;
    private readonly object _lock = new();
    public TitleRepository(PanelConfig cfg) { _path = Path.Combine(cfg.DataDir, "Title.bin"); }

    public string FilePath => _path;
    public bool Exists => File.Exists(_path);

    public byte[] ReadAll()
    {
        var b = File.ReadAllBytes(_path);
        if (b.Length != TitleBin.ExpectedFileSize)
            throw new InvalidOperationException(
                $"Title.bin tem {b.Length} bytes (esperado {TitleBin.ExpectedFileSize}). Arquivo invalido.");
        return b;
    }

    public List<TitleData> DecodeAll() => TitleBin.DecodeAll(ReadAll());

    public (bool ok, int total, int firstMismatch) SelfTest() => TitleBin.SelfTest(ReadAll());

    /// <summary>Backup + aplica os 4 níveis do título `index` + grava Data\Title.bin. Trailing preservado.</summary>
    public string Update(int index, List<TitleLevelData> levels)
    {
        lock (_lock)
        {
            if (index < 0 || index >= TitleBin.TitleCount)
                throw new ArgumentOutOfRangeException(nameof(index), $"Indice {index} fora de 0..{TitleBin.TitleCount - 1}.");
            if (levels == null || levels.Count != TitleBin.LevelsPerTitle)
                throw new ArgumentException($"Esperado {TitleBin.LevelsPerTitle} niveis, veio {levels?.Count ?? 0}.");
            var bytes = ReadAll();
            var bak = Backup.Make(_path);
            for (int l = 0; l < TitleBin.LevelsPerTitle; l++)
            {
                int off = (index * TitleBin.LevelsPerTitle + l) * TitleBin.LevelSize;
                TitleBin.ApplyLevel(bytes.AsSpan(off, TitleBin.LevelSize), levels[l]);
            }
            File.WriteAllBytes(_path, bytes);
            return Path.GetFileName(bak);
        }
    }
}
