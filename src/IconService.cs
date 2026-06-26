using System.IO.Compression;
using System.Text.Json;

namespace AikaPanel;

/// <summary>
/// Serves the client's ItemIcons atlases as transparent PNG sprite-sheets and keeps a
/// persistent IconID -> (atlas, cell) map. Atlases are 1024x1024 = 32x32 grid of 32px cells,
/// addressed by cell = row*32 + col (row-major, top-left = 0).
///
/// The map is NOT a closed-form formula (proven: the client places item icons in arbitrary
/// per-batch offsets across 11 atlases). It is built empirically and stored in iconmap.json so
/// it survives restarts and grows as items are linked from the UI. See ICONS_RESEARCH.md.
/// </summary>
public sealed class IconService
{
    public const int Cell = 32;
    public const int Cols = 32;          // for the standard 1024x1024 atlases
    public const int AtlasCount = 11;

    private readonly PanelConfig _cfg;
    private readonly string _mapPath;
    private readonly object _lock = new();

    // atlas (1..11) -> cached transparent PNG bytes (null = not yet decoded / failed)
    private readonly byte[]?[] _sheetCache = new byte[AtlasCount + 1][];
    private readonly (int w, int h)[] _dims = new (int, int)[AtlasCount + 1];

    // forward map: IconID -> (atlas 1..11, cell)
    private Dictionary<int, int[]> _map = new();

    public IconService(PanelConfig cfg)
    {
        _cfg = cfg;
        _mapPath = Path.Combine(cfg.ClientUIDir ?? ".", "iconmap.json");
        LoadMap();
    }

    private string AtlasPath(int atlas) =>
        Path.Combine(_cfg.ClientUIDir ?? ".", $"ItemIcons{atlas:00}.jit");

    public bool AtlasExists(int atlas) => atlas >= 1 && atlas <= AtlasCount && File.Exists(AtlasPath(atlas));

    // ---- map persistence ----
    private void LoadMap()
    {
        try
        {
            if (File.Exists(_mapPath))
            {
                var doc = JsonSerializer.Deserialize<Dictionary<string, int[]>>(File.ReadAllText(_mapPath));
                if (doc != null)
                {
                    _map = new();
                    foreach (var kv in doc)
                        if (int.TryParse(kv.Key, out var id) && kv.Value is { Length: 2 })
                            _map[id] = kv.Value;
                }
            }
        }
        catch { _map = new(); }
        SeedAnchors();
    }

    /// <summary>Confirmed anchors (verified visually). Only fills entries not already present.</summary>
    private void SeedAnchors()
    {
        // "Letra do Enigma A..Z" IconIDs 4113..4138 -> atlas 8, cells 145..170 (proven).
        for (int i = 0; i <= 25; i++)
            _map.TryAdd(4113 + i, new[] { 8, 145 + i });
    }

    private void SaveMap()
    {
        var ordered = _map.OrderBy(k => k.Key).ToDictionary(k => k.Key.ToString(), k => k.Value);
        var tmp = _mapPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(ordered, new JsonSerializerOptions { WriteIndented = false }));
        File.Copy(tmp, _mapPath, true);
        File.Delete(tmp);
    }

    public int[]? Lookup(int iconId) => _map.TryGetValue(iconId, out var v) ? v : null;

    public int? ReverseLookup(int atlas, int cell)
    {
        foreach (var kv in _map)
            if (kv.Value[0] == atlas && kv.Value[1] == cell) return kv.Key;
        return null;
    }

    public IReadOnlyDictionary<int, int[]> Map => _map;

    public void SetLink(int iconId, int atlas, int cell)
    {
        lock (_lock) { _map[iconId] = new[] { atlas, cell }; SaveMap(); }
    }

    public bool RemoveLink(int iconId)
    {
        lock (_lock) { var r = _map.Remove(iconId); if (r) SaveMap(); return r; }
    }

    // ---- sheet decoding ----
    public (int w, int h)? Dims(int atlas)
    {
        if (GetSheetPng(atlas) == null) return null;
        return _dims[atlas];
    }

    public byte[]? GetSheetPng(int atlas)
    {
        if (atlas < 1 || atlas > AtlasCount) return null;
        if (_sheetCache[atlas] != null) return _sheetCache[atlas];
        lock (_lock)
        {
            if (_sheetCache[atlas] != null) return _sheetCache[atlas];
            try
            {
                var path = AtlasPath(atlas);
                if (!File.Exists(path)) return null;
                var (rgba, w, h) = DecodeJit(File.ReadAllBytes(path));
                if (rgba == null) return null;
                _dims[atlas] = (w, h);
                _sheetCache[atlas] = EncodePngRGBA(w, h, rgba);
                return _sheetCache[atlas];
            }
            catch { return null; }
        }
    }

    /// <summary>Decode a .jit atlas (JT31=DXT1, JT33=DXT3, JT35=DXT5) to RGBA. Returns (null,0,0) on unsupported (e.g. JT20).</summary>
    private static (byte[]? rgba, int w, int h) DecodeJit(byte[] d)
    {
        string magic = System.Text.Encoding.ASCII.GetString(d, 0, 4);
        int w = BitConverter.ToInt32(d, 4), h = BitConverter.ToInt32(d, 8);
        bool dxt5 = magic == "JT35", dxt1 = magic == "JT31", dxt3 = magic == "JT33";
        if (!(dxt5 || dxt1 || dxt3)) return (null, 0, 0);     // JT20 / unknown -> caller handles
        if (w <= 0 || h <= 0 || w > 8192 || h > 8192) return (null, 0, 0);
        var rgba = new byte[w * h * 4];
        int p = 12;
        for (int by = 0; by < h; by += 4)
        for (int bx = 0; bx < w; bx += 4)
        {
            var a = new int[16];
            if (dxt1) { for (int i = 0; i < 16; i++) a[i] = 255; }
            else if (dxt5)
            {
                int a0 = d[p], a1 = d[p + 1];
                long bits = 0; for (int i = 0; i < 6; i++) bits |= (long)d[p + 2 + i] << (8 * i);
                for (int i = 0; i < 16; i++)
                {
                    int idx = (int)((bits >> (3 * i)) & 7), av;
                    if (idx == 0) av = a0; else if (idx == 1) av = a1;
                    else if (a0 > a1) av = ((8 - idx) * a0 + (idx - 1) * a1) / 7;
                    else if (idx < 6) av = ((6 - idx) * a0 + (idx - 1) * a1) / 5;
                    else av = (idx == 6) ? 0 : 255;
                    a[i] = av;
                }
                p += 8;
            }
            else // DXT3: 4-bit alpha per pixel
            {
                for (int i = 0; i < 8; i++) { a[i * 2] = (d[p + i] & 0x0F) * 17; a[i * 2 + 1] = (d[p + i] >> 4) * 17; }
                p += 8;
            }
            ushort c0 = BitConverter.ToUInt16(d, p), c1 = BitConverter.ToUInt16(d, p + 2);
            uint cidx = BitConverter.ToUInt32(d, p + 4); p += 8;
            var col = new (int r, int g, int b)[4];
            col[0] = R5G6B5(c0); col[1] = R5G6B5(c1);
            col[2] = ((2 * col[0].r + col[1].r) / 3, (2 * col[0].g + col[1].g) / 3, (2 * col[0].b + col[1].b) / 3);
            col[3] = ((col[0].r + 2 * col[1].r) / 3, (col[0].g + 2 * col[1].g) / 3, (col[0].b + 2 * col[1].b) / 3);
            for (int i = 0; i < 16; i++)
            {
                int px = bx + (i % 4), py = by + (i / 4);
                if (px >= w || py >= h) continue;
                var c = col[(int)((cidx >> (2 * i)) & 3)];
                int o = (py * w + px) * 4;
                rgba[o] = (byte)c.r; rgba[o + 1] = (byte)c.g; rgba[o + 2] = (byte)c.b; rgba[o + 3] = (byte)a[i];
            }
        }
        return (rgba, w, h);
    }

    private static (int, int, int) R5G6B5(ushort c) => (((c >> 11) & 31) * 255 / 31, ((c >> 5) & 63) * 255 / 63, (c & 31) * 255 / 31);

    // ---- PNG encode (RGBA) ----
    private static byte[] EncodePngRGBA(int w, int h, byte[] rgba)
    {
        using var ms = new MemoryStream();
        Span<byte> sig = stackalloc byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }; ms.Write(sig);
        var ihdr = new byte[13];
        WBE(ihdr, 0, w); WBE(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
        Chunk(ms, "IHDR", ihdr);
        var raw = new byte[h * (w * 4 + 1)];
        for (int y = 0; y < h; y++) { int ro = y * (w * 4 + 1); raw[ro] = 0; Array.Copy(rgba, y * w * 4, raw, ro + 1, w * 4); }
        Chunk(ms, "IDAT", ZlibCompress(raw));
        Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WBE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WBE(len, 0, data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type); s.Write(t); s.Write(data);
        uint c = Crc(t, data); var cb = new byte[4]; WBE(cb, 0, (int)c); s.Write(cb);
    }
    private static byte[] ZlibCompress(byte[] data)
    {
        using var ms = new MemoryStream(); ms.WriteByte(0x78); ms.WriteByte(0x9C);
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true)) ds.Write(data, 0, data.Length);
        uint adl = Adler32(data); ms.WriteByte((byte)(adl >> 24)); ms.WriteByte((byte)(adl >> 16)); ms.WriteByte((byte)(adl >> 8)); ms.WriteByte((byte)adl);
        return ms.ToArray();
    }
    private static uint Adler32(byte[] d) { uint a = 1, b = 0; foreach (var x in d) { a = (a + x) % 65521; b = (b + a) % 65521; } return (b << 16) | a; }
    private static uint Crc(byte[] t, byte[] d) { uint c = 0xffffffff; foreach (var x in t) c = Cr(c, x); foreach (var x in d) c = Cr(c, x); return c ^ 0xffffffff; }
    private static uint Cr(uint c, byte x) { c ^= x; for (int i = 0; i < 8; i++) c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320 : c >> 1; return c; }
}
