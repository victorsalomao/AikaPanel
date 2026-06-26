using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AikaPanel;

/// <summary>Reads/writes the server's <c>Data\NPCs\*.npc</c> files (shop + position).</summary>
public sealed class NpcRepository
{
    private readonly string _dir;            // <DataDir>\NPCs
    private readonly string _itemListPath;   // <DataDir>\ItemList.bin (for name/price lookup)
    private readonly object _lock = new();
    private static readonly Encoding Cp = PiBin.Win1252; // registers the CodePages provider
    private static readonly Regex FileRe = new(@"^\[(\d+)\]\s*(.*)$", RegexOptions.Compiled);

    // ItemList cache (name + gold sell price), invalidated by file write time.
    private byte[]? _itemBytes;
    private DateTime _itemStamp;

    public NpcRepository(PanelConfig cfg)
    {
        _dir = Path.Combine(cfg.DataDir, "NPCs");
        _itemListPath = Path.Combine(cfg.DataDir, "ItemList.bin");
    }

    public string Dir => _dir;
    public bool Exists => Directory.Exists(_dir);

    // ----- item lookups -----------------------------------------------------
    private void EnsureItems()
    {
        if (!File.Exists(_itemListPath)) { _itemBytes = null; return; }
        var stamp = File.GetLastWriteTimeUtc(_itemListPath);
        if (_itemBytes != null && stamp == _itemStamp) return;
        _itemBytes = File.ReadAllBytes(_itemListPath);
        _itemStamp = stamp;
    }

    private string ItemName(int id)
    {
        EnsureItems();
        if (_itemBytes == null) return "";
        int o = id * ItemList.RecordSize;
        if (id < 0 || o + 64 > _itemBytes.Length) return "";
        var raw = _itemBytes.AsSpan(o, 64);
        int z = raw.IndexOf((byte)0); if (z < 0) z = 64;
        return Cp.GetString(raw.Slice(0, z));
    }

    private long ItemSell(int id)
    {
        EnsureItems();
        if (_itemBytes == null) return 0;
        int o = id * ItemList.RecordSize;
        if (id < 0 || o + 296 > _itemBytes.Length) return 0;
        return BinaryPrimitives.ReadUInt32LittleEndian(_itemBytes.AsSpan(o + 292, 4)); // SellPrince
    }

    // ----- filename helpers -------------------------------------------------
    private static (int id, string name) ParseName(string fileNoExt)
    {
        var m = FileRe.Match(fileNoExt);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var id))
            return (id, m.Groups[2].Value.Trim());
        return (-1, fileNoExt);
    }

    private string? FindFile(int id)
    {
        if (!Exists) return null;
        foreach (var f in Directory.GetFiles(_dir, "*.npc"))
        {
            var (fid, _) = ParseName(Path.GetFileNameWithoutExtension(f));
            if (fid == id) return f;
        }
        return null;
    }

    // ----- list / get -------------------------------------------------------
    public List<NpcSummary> List()
    {
        var list = new List<NpcSummary>();
        if (!Exists) return list;
        foreach (var f in Directory.GetFiles(_dir, "*.npc"))
        {
            byte[] b;
            try { b = File.ReadAllBytes(f); } catch { continue; }
            if (b.Length < NpcFile.SizeStd) continue;
            var (id, name) = ParseName(Path.GetFileNameWithoutExtension(f));
            if (id < 0) id = NpcFile.ReadIndex(b);
            list.Add(new NpcSummary
            {
                Id = id,
                File = Path.GetFileName(f),
                Name = name,
                Title = NpcFile.ReadTitle(b),
                X = Math.Round(NpcFile.ReadX(b), 1),
                Y = Math.Round(NpcFile.ReadY(b), 1),
                ShopCount = NpcFile.ShopCount(b),
                HardcodedPos = NpcFile.HardcodedPos.Contains(id),
                Size = b.Length,
                NameId = NpcFile.ReadNameId(b),
            });
        }
        list.Sort((a, c) => a.Id.CompareTo(c.Id));

        // In-game name comes from NameId (not the filename). Flag NPCs that share a NameId
        // and, for an empty one, point at the twin that actually holds the shop.
        foreach (var grp in list.Where(n => !string.IsNullOrEmpty(n.NameId)).GroupBy(n => n.NameId))
        {
            var members = grp.ToList();
            if (members.Count < 2) continue;
            var stocked = members.Where(m => m.ShopCount > 0).OrderByDescending(m => m.ShopCount).FirstOrDefault();
            foreach (var n in members)
            {
                n.DupCount = members.Count;
                if (stocked != null && stocked.Id != n.Id)
                {
                    n.TwinShopId = stocked.Id;
                    n.TwinShopCount = stocked.ShopCount;
                }
            }
        }
        return list;
    }

    public NpcDetail? Get(int id)
    {
        var f = FindFile(id);
        if (f == null) return null;
        var b = File.ReadAllBytes(f);
        if (b.Length < NpcFile.SizeStd) return null;
        var (fid, name) = ParseName(Path.GetFileNameWithoutExtension(f));
        var d = new NpcDetail
        {
            Id = fid < 0 ? NpcFile.ReadIndex(b) : fid,
            File = Path.GetFileName(f),
            Name = name,
            Title = NpcFile.ReadTitle(b),
            X = Math.Round(NpcFile.ReadX(b), 1),
            Y = Math.Round(NpcFile.ReadY(b), 1),
            ShopCount = NpcFile.ShopCount(b),
            HardcodedPos = NpcFile.HardcodedPos.Contains(id),
            Size = b.Length,
            NameId = NpcFile.ReadNameId(b),
        };
        for (int s = 0; s < NpcFile.InvSlots; s++)
        {
            int item = NpcFile.ReadSlotItem(b, s);
            if (item == 0) continue;
            long sell = ItemSell(item);
            d.Shop.Add(new NpcShopSlot
            {
                Slot = s,
                ItemId = item,
                App = NpcFile.ReadSlotApp(b, s),
                ItemName = ItemName(item),
                SellPrince = sell,
                Hidden = s >= NpcFile.ShopVisibleMax,
                PriceBlocked = sell <= 1,
            });
        }

        // Twins = other NPCs sharing this NPC's in-game NameId. Lets the panel explain
        // "this one looks empty but [other id] holds the shop you see in-game".
        if (!string.IsNullOrEmpty(d.NameId))
        {
            var all = List();
            var me = all.FirstOrDefault(n => n.Id == d.Id);
            if (me != null) { d.DupCount = me.DupCount; d.TwinShopId = me.TwinShopId; d.TwinShopCount = me.TwinShopCount; }
            d.Twins = all.Where(n => n.Id != d.Id && n.NameId == d.NameId)
                         .Select(n => new NpcTwin { Id = n.Id, File = n.File, Name = n.Name, ShopCount = n.ShopCount })
                         .ToList();
        }
        return d;
    }

    // ----- update -----------------------------------------------------------
    public (string backup, List<string> warnings) Update(int id, NpcEdit edit)
    {
        lock (_lock)
        {
            var f = FindFile(id) ?? throw new FileNotFoundException($"NPC {id} nao encontrado.");
            var b = File.ReadAllBytes(f);
            if (b.Length < NpcFile.SizeStd) throw new InvalidOperationException($"{Path.GetFileName(f)} tem tamanho inesperado ({b.Length}).");
            var span = b.AsSpan();

            if (edit.Title != null) NpcFile.WriteTitle(span, edit.Title);
            if (edit.X.HasValue) NpcFile.WriteX(span, (float)edit.X.Value);
            if (edit.Y.HasValue) NpcFile.WriteY(span, (float)edit.Y.Value);

            var warnings = new List<string>();
            if (edit.Shop != null)
            {
                foreach (var s in edit.Shop)
                {
                    if (s.Slot < 0 || s.Slot >= NpcFile.InvSlots)
                        throw new ArgumentOutOfRangeException(nameof(s.Slot), $"Slot {s.Slot} fora de 0..{NpcFile.InvSlots - 1}.");
                    // New item without an explicit APP -> show the item's own appearance (app = id).
                    int app = (s.ItemId > 0 && s.App <= 0) ? s.ItemId : s.App;
                    NpcFile.WriteSlot(span, s.Slot, s.ItemId, app);
                    if (s.ItemId > 0 && s.Slot >= NpcFile.ShopVisibleMax)
                        warnings.Add($"Slot {s.Slot}: itens em slot >= 40 NAO aparecem na loja (limite do ShowShop).");
                    if (s.ItemId > 0 && ItemSell(s.ItemId) <= 1)
                        warnings.Add($"Item {s.ItemId} ({ItemName(s.ItemId)}) tem SellPrince <= 1 -> nao da pra comprar. Ajuste o preco na aba Itens.");
                }
            }

            if (edit.HardcodedFlag() && NpcFile.HardcodedPos.Contains(id))
                warnings.Add($"NPC {id} tem posicao HARDCODED em NPC.pas - mudar X/Y no arquivo nao move ele (precisa recompilar o server).");

            var bak = Backup.Make(f);
            File.WriteAllBytes(f, b);
            return (Path.GetFileName(bak), warnings);
        }
    }

    // ----- clone / create ---------------------------------------------------
    public (string file, List<string> warnings) Clone(NpcCreateDto dto)
    {
        lock (_lock)
        {
            if (dto.NewId <= 0) throw new ArgumentException("NewId invalido.");
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Nome obrigatorio.");
            if (FindFile(dto.NewId) != null) throw new InvalidOperationException($"Ja existe um NPC com id {dto.NewId}.");
            var src = FindFile(dto.SourceId) ?? throw new FileNotFoundException($"NPC fonte {dto.SourceId} nao encontrado.");
            var b = File.ReadAllBytes(src);
            if (b.Length < NpcFile.SizeStd) throw new InvalidOperationException("Arquivo fonte com tamanho inesperado.");
            var span = b.AsSpan();
            NpcFile.WriteIndex(span, dto.NewId);
            if (dto.Title != null) NpcFile.WriteTitle(span, dto.Title);
            NpcFile.WriteX(span, (float)dto.X);
            NpcFile.WriteY(span, (float)dto.Y);

            var safe = SanitizeName(dto.Name);
            var newPath = Path.Combine(_dir, $"[{dto.NewId}] {safe}.npc");
            File.WriteAllBytes(newPath, b);

            var warnings = new List<string>();
            if (dto.NewId < 2048 || dto.NewId > 3047)
                warnings.Add($"Id {dto.NewId} fora de 2048..3047: a loja de NPC so roda nessa faixa (BuyNPCItens). Fora dela o NPC aparece mas nao vende.");
            warnings.Add("Reinicie o servidor para o novo NPC ser carregado.");
            return (Path.GetFileName(newPath), warnings);
        }
    }

    public (string backup, string file) Delete(int id)
    {
        lock (_lock)
        {
            var f = FindFile(id) ?? throw new FileNotFoundException($"NPC {id} nao encontrado.");
            var bak = Backup.Make(f);
            File.Delete(f);
            return (Path.GetFileName(bak), Path.GetFileName(f));
        }
    }

    private static string SanitizeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, ' ');
        return name.Trim();
    }

    // ----- city clustering + placement -------------------------------------
    private List<(int id, string name, float x, float y)> AllPositions()
    {
        var res = new List<(int, string, float, float)>();
        if (!Exists) return res;
        foreach (var f in Directory.GetFiles(_dir, "*.npc"))
        {
            byte[] b; try { b = File.ReadAllBytes(f); } catch { continue; }
            if (b.Length < NpcFile.SizeStd) continue;
            var (id, name) = ParseName(Path.GetFileNameWithoutExtension(f));
            float x = NpcFile.ReadX(b), y = NpcFile.ReadY(b);
            if (x < 1 || y < 1) continue; // unplaced
            res.Add((id, name, x, y));
        }
        return res;
    }

    // Approx named anchors (from Src/Functions/Load.pas guard coords + NPC clusters).
    private static readonly (string name, float x, float y)[] Anchors =
    {
        ("Regenshein", 3500, 850),
        ("Altar (Regenshein)", 3499, 935),
        ("Amarkand/Verband", 3440, 1550),
        ("Basilan", 3440, 2213),
    };

    public List<CityCluster> Cities()
    {
        var pos = AllPositions();
        // grid clustering at 200-unit cells
        const int cell = 200;
        var buckets = new Dictionary<(int, int), List<(int id, string name, float x, float y)>>();
        foreach (var p in pos)
        {
            var key = ((int)(p.x / cell), (int)(p.y / cell));
            if (!buckets.TryGetValue(key, out var l)) { l = new(); buckets[key] = l; }
            l.Add(p);
        }
        var clusters = new List<CityCluster>();
        foreach (var b in buckets.Values)
        {
            if (b.Count < 3) continue; // ignore tiny scatter
            double cx = b.Average(p => p.x), cy = b.Average(p => p.y);
            clusters.Add(new CityCluster { Name = LabelFor(cx, cy, b), X = Math.Round(cx, 0), Y = Math.Round(cy, 0), Count = b.Count });
        }
        clusters.Sort((a, c) => c.Count.CompareTo(a.Count));
        return clusters;
    }

    private static string LabelFor(double cx, double cy, List<(int id, string name, float x, float y)> b)
    {
        foreach (var a in Anchors)
            if (Math.Abs(a.x - cx) < 180 && Math.Abs(a.y - cy) < 180) return a.name;
        // fall back to the NPC nearest the centroid
        var near = b.OrderBy(p => (p.x - cx) * (p.x - cx) + (p.y - cy) * (p.y - cy)).First();
        return $"Regiao perto de {near.name}";
    }

    /// <summary>Returns a coord near (x,y) at least minDist away from every existing NPC (anti-overlap).</summary>
    public (double x, double y) FreeSpot(double x, double y, double minDist = 3.0)
    {
        var pos = AllPositions();
        bool Free(double px, double py)
        {
            foreach (var p in pos)
                if (Math.Abs(p.x - px) < minDist && Math.Abs(p.y - py) < minDist) return false;
            return true;
        }
        if (Free(x, y)) return (Math.Round(x, 1), Math.Round(y, 1));
        // spiral outward in steps of minDist
        for (int r = 1; r <= 30; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue; // ring only
                    double nx = x + dx * minDist, ny = y + dy * minDist;
                    if (Free(nx, ny)) return (Math.Round(nx, 1), Math.Round(ny, 1));
                }
        return (Math.Round(x, 1), Math.Round(y, 1));
    }

    public int NextFreeId(int start = 2048, int end = 3047)
    {
        var used = new HashSet<int>(List().Select(n => n.Id));
        for (int i = start; i <= end; i++) if (!used.Contains(i)) return i;
        return -1;
    }

    public (bool ok, int total, int firstMismatch) SelfTest() => NpcFile.SelfTest(_dir);
}

internal static class NpcEditExt
{
    // Title/X/Y edits to a hardcoded-pos NPC deserve a warning.
    public static bool HardcodedFlag(this NpcEdit e) => e.X.HasValue || e.Y.HasValue;
}
