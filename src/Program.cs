using System.Collections.Concurrent;
using AikaPanel;

// Load config (works both from bin dir and when published as single file)
var cfg = new PanelConfig();
var cfgPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
if (File.Exists(cfgPath))
{
    var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cfgPath));
    if (json.RootElement.TryGetProperty("AikaPanel", out var ap))
    {
        if (ap.TryGetProperty("DataDir", out var d)) cfg.DataDir = d.GetString() ?? cfg.DataDir;
        if (ap.TryGetProperty("Host", out var h)) cfg.Host = h.GetString() ?? cfg.Host;
        if (ap.TryGetProperty("Port", out var p)) cfg.Port = p.GetInt32();
        if (ap.TryGetProperty("Password", out var pw)) cfg.Password = pw.GetString() ?? cfg.Password;
        if (ap.TryGetProperty("ClientUIDir", out var cu)) cfg.ClientUIDir = cu.GetString() ?? cfg.ClientUIDir;
        if (ap.TryGetProperty("ClientGameDir", out var cg)) cfg.ClientGameDir = cg.GetString() ?? cfg.ClientGameDir;
        if (ap.TryGetProperty("Mysql", out var my))
        {
            if (my.TryGetProperty("Server", out var ms)) cfg.MysqlServer = ms.GetString() ?? "";
            if (my.TryGetProperty("Port", out var mp) && mp.TryGetUInt32(out var mpv)) cfg.MysqlPort = mpv;
            if (my.TryGetProperty("Database", out var md)) cfg.MysqlDatabase = md.GetString() ?? "";
            if (my.TryGetProperty("User", out var mu)) cfg.MysqlUser = mu.GetString() ?? "";
            if (my.TryGetProperty("Password", out var mpw)) cfg.MysqlPassword = mpw.GetString() ?? "";
        }
    }
}

var cash = new CashRepository(cfg);
var quests = new QuestRepository(cfg);
var items = new ItemRepository(cfg);
var skills = new SkillRepository(cfg);
var titlesRepo = new TitleRepository(cfg);
var npcs = new NpcRepository(cfg);
var db = new Db(DbConfig.Resolve(cfg));
var itemNames = new ItemNames(cfg);
var accounts = new AccountRepository(db, itemNames, cfg);
var icons = new IconService(cfg);

// --- Headless self-test mode (run before trusting the UI) ---
if (args.Contains("--selftest"))
{
    try
    {
        int rc = 0;
        var c = cash.SelfTest();
        Console.WriteLine(c.ok ? $"PI.bin       ROUND-TRIP OK {c.total}/{c.total}"
                               : $"PI.bin       ROUND-TRIP FAIL at slot {c.firstMismatch} (of {c.total})");
        if (!c.ok) rc = 1;
        var it = items.SelfTest();
        Console.WriteLine(it.ok ? $"ItemList.bin ROUND-TRIP OK {it.total}/{it.total}"
                                : $"ItemList.bin ROUND-TRIP FAIL at id {it.firstMismatch} (of {it.total})");
        if (!it.ok) rc = 1;
        var sk = skills.SelfTest();
        Console.WriteLine(sk.ok ? $"SkillData.bin ROUND-TRIP OK {sk.total}/{sk.total}"
                                : $"SkillData.bin ROUND-TRIP FAIL at id {sk.firstMismatch} (of {sk.total})");
        if (!sk.ok) rc = 1;
        var np = new NpcRepository(cfg).SelfTest();
        Console.WriteLine(np.ok ? $"NPCs (*.npc)  ROUND-TRIP OK {np.total}/{np.total}"
                                : $"NPCs (*.npc)  ROUND-TRIP FAIL at file #{np.firstMismatch} (of {np.total})");
        if (!np.ok) rc = 1;
        var ti = titlesRepo.SelfTest();
        Console.WriteLine(ti.ok ? $"Title.bin     ROUND-TRIP OK {ti.total}/{ti.total}"
                                : $"Title.bin     ROUND-TRIP FAIL at level rec {ti.firstMismatch} (of {ti.total})");
        if (!ti.ok) rc = 1;
        return rc;
    }
    catch (Exception ex) { Console.WriteLine("SELFTEST ERROR: " + ex.Message); return 2; }
}

// --- Crypto proof: encrypt(raw)==enc?  decrypt(enc)==raw?  ---
if (args.Length >= 3 && args[0] == "--cryptotest")
{
    var raw = File.ReadAllBytes(args[1]);
    var enc = File.ReadAllBytes(args[2]);
    int Compare(byte[] a, byte[] b, out int firstDiff)
    {
        firstDiff = -1; int match = 0; int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) { if (a[i] == b[i]) match++; else if (firstDiff < 0) firstDiff = i; }
        return match;
    }
    var encOut = CashCrypto.Encrypt(raw);
    int m1 = Compare(encOut, enc, out int d1);
    Console.WriteLine($"encrypt(raw) vs enc : {m1}/{enc.Length} iguais" + (m1 == enc.Length && raw.Length == enc.Length ? "  -> BYTE-IDENTICO" : $"  (1a diff @ {d1})"));
    var decOut = CashCrypto.Decrypt(enc);
    int m2 = Compare(decOut, raw, out int d2);
    Console.WriteLine($"decrypt(enc) vs raw : {m2}/{raw.Length} iguais" + (m2 == raw.Length && raw.Length == enc.Length ? "  -> BYTE-IDENTICO" : $"  (1a diff @ {d2})"));
    return (m1 == enc.Length && m2 == raw.Length && raw.Length == enc.Length) ? 0 : 1;
}

if (args.Length >= 3 && (args[0] == "--encrypt" || args[0] == "--decrypt"))
{
    var inp = File.ReadAllBytes(args[1]);
    var outb = args[0] == "--encrypt" ? CashCrypto.Encrypt(inp) : CashCrypto.Decrypt(inp);
    File.WriteAllBytes(args[2], outb);
    Console.WriteLine($"{args[0]} {inp.Length} bytes -> {args[2]}");
    return 0;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://{cfg.Host}:{cfg.Port}");
var app = builder.Build();

// --- Simple token auth (password gate) ---
var tokens = new ConcurrentDictionary<string, byte>();

var webFiles = new Microsoft.Extensions.FileProviders.ManifestEmbeddedFileProvider(
    System.Reflection.Assembly.GetExecutingAssembly(), "wwwroot");
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = webFiles });
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = webFiles,
    // The HTML/JS/CSS shell is embedded in the exe and changes on every republish.
    // Without this, a browser can keep running a stale cached app.js after an update
    // (no cache-busting on the <script>/<link> tags) -> features look "broken" even
    // though the server is correct. no-cache = always revalidate against the ETag,
    // so the user always gets the current bundle (cheap: 304 when unchanged).
    OnPrepareResponse = ctx =>
    {
        var path = ctx.File.Name;
        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache, must-revalidate";
        }
    },
});

bool Authed(HttpRequest r) =>
    r.Headers.TryGetValue("X-Auth-Token", out var t) && tokens.ContainsKey(t.ToString());

object Ok(object data, string msg = "ok") => new { sucesso = true, dados = data, mensagem = msg };
object Err(string code, string msg) => new { sucesso = false, erro = code, mensagem = msg };

app.MapGet("/api/health", () => Results.Json(new { status = "ok", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }));

app.MapPost("/api/login", (LoginDto dto) =>
{
    if (string.IsNullOrEmpty(cfg.Password) || dto.password != cfg.Password)
        return Results.Json(Err("SENHA_INVALIDA", "Senha incorreta."), statusCode: 401);
    var token = Guid.NewGuid().ToString("N");
    tokens[token] = 1;
    return Results.Json(Ok(new { token }, "Autenticado."));
});

// Auth gate for everything under /api except health + login
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
    // Icon art reads are public (localhost-only, just game sprites; <img> tags can't send the token header).
    bool publicIconRead = ctx.Request.Method == "GET" &&
        (path.StartsWith("/api/iconsheet") || path.StartsWith("/api/iconmeta")
         || path.StartsWith("/api/iconmap") || path.StartsWith("/api/iconcell"));
    if (path.StartsWith("/api/") && path != "/api/health" && path != "/api/login" && !publicIconRead)
    {
        if (!Authed(ctx.Request))
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(Err("NAO_AUTENTICADO", "Faca login primeiro."));
            return;
        }
    }
    await next();
});

// --- Cash Shop ---
app.MapGet("/api/cash", (string? q, int page = 1, int limit = 50, bool onlyVisible = false) =>
{
    if (!cash.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", $"PI.bin nao encontrado em {cash.FilePath}"), statusCode: 404);
    try
    {
        var all = cash.DecodeAll();
        int total = all.Count;
        int visiveis = all.Count(x => x.Show == 1);
        IEnumerable<CashItem> filtered = all;
        if (onlyVisible) filtered = filtered.Where(x => x.Show == 1);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            filtered = filtered.Where(x =>
                x.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                x.Slot.ToString() == s ||
                x.Indice.ToString() == s ||
                x.ItemIndex.ToString() == s);
        }
        var list = filtered.ToList();
        int matched = list.Count;
        if (limit < 1) limit = 50;
        var pageItems = list.Skip((Math.Max(1, page) - 1) * limit).Take(limit).ToList();
        return Results.Json(Ok(new { itens = pageItems, total, visiveis, matched, page, limit, tem_mais = (Math.Max(1, page) - 1) * limit + pageItems.Count < matched }));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_LEITURA", ex.Message), statusCode: 400); }
});

app.MapPost("/api/cash/{slot:int}", (int slot, CashEdit edit) =>
{
    if (!cash.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", "PI.bin nao encontrado."), statusCode: 404);
    if (edit.Name.Length > 64) return Results.Json(Err("NOME_LONGO", "Nome excede 64 caracteres."), statusCode: 400);
    if (edit.Descricao.Length > 256) return Results.Json(Err("DESC_LONGA", "Descricao excede 256 caracteres."), statusCode: 400);
    if (edit.Show > 1) edit.Show = 1;
    try
    {
        var r = cash.Update(slot, edit);
        var msg = r.ClientSynced
            ? "Salvo. Server (Data\\PI.bin) e vitrine do client (UI\\PI.bin) sincronizados. Reinicie o servidor para aplicar."
            : $"Salvo no server. {r.ClientMessage} Reinicie o servidor para aplicar.";
        return Results.Json(Ok(new { backup = r.ServerBackup, clientSynced = r.ClientSynced, clientBackup = r.ClientBackup, clientMsg = r.ClientMessage }, msg));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_GRAVACAO", ex.Message), statusCode: 400); }
});

app.MapGet("/api/cash/selftest", () =>
{
    try { var (ok, total, mm) = cash.SelfTest(); return Results.Json(Ok(new { ok, total, firstMismatch = mm })); }
    catch (Exception ex) { return Results.Json(Err("ERRO_SELFTEST", ex.Message), statusCode: 400); }
});

app.MapGet("/api/cash/syncstatus", () =>
{
    try { return Results.Json(Ok(cash.SyncStatus())); }
    catch (Exception ex) { return Results.Json(Err("ERRO_SYNC", ex.Message), statusCode: 400); }
});

app.MapPost("/api/cash/sync", () =>
{
    if (!cash.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", "PI.bin nao encontrado."), statusCode: 404);
    try
    {
        var r = cash.SyncClient();
        if (r.ClientSynced) return Results.Json(Ok(new { clientBackup = r.ClientBackup }, "Vitrine do client sincronizada com o Data\\PI.bin atual."));
        return Results.Json(Err("SYNC_FALHOU", r.ClientMessage), statusCode: 400);
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_SYNC", ex.Message), statusCode: 400); }
});

// --- Quests ---
app.MapGet("/api/quests", () =>
{
    if (!quests.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", $"Quests.csv nao encontrado em {quests.FilePath}"), statusCode: 404);
    try { return Results.Json(Ok(new { colunas = QuestRepository.Columns, linhas = quests.Read() })); }
    catch (Exception ex) { return Results.Json(Err("ERRO_LEITURA", ex.Message), statusCode: 400); }
});

app.MapPost("/api/quests", (QuestsSaveDto dto) =>
{
    if (!quests.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", "Quests.csv nao encontrado."), statusCode: 404);
    try
    {
        var rows = dto.linhas.Select(r => r.ToArray()).ToList();
        var bak = quests.Write(rows);
        return Results.Json(Ok(new { backup = Path.GetFileName(bak), linhas = rows.Count }, "Quests salvas. Reinicie o servidor para aplicar."));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_GRAVACAO", ex.Message), statusCode: 400); }
});

// --- Itens (ItemList.bin) ---
app.MapGet("/api/itens", (string? q, int? minLevel, int? maxLevel, int page = 1, int limit = 50) =>
{
    if (!items.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", $"ItemList.bin nao encontrado em {items.FilePath}"), statusCode: 404);
    try
    {
        var all = items.DecodeAll();
        int total = all.Count;
        bool hasLevel = minLevel.HasValue || maxLevel.HasValue;
        IEnumerable<ItemEntry> filtered = all;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            filtered = filtered.Where(x =>
                x.Id.ToString() == s ||
                x.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                x.NameEnglish.Contains(s, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            // Sem busca textual: esconder slots totalmente vazios (sem nenhum nome) para a lista ficar util
            filtered = filtered.Where(x => x.Name.Length > 0 || x.NameEnglish.Length > 0);
        }
        // Filtro por nivel (independente da busca textual): util para achar itens acima de um level
        if (minLevel.HasValue) filtered = filtered.Where(x => x.Level >= minLevel.Value);
        if (maxLevel.HasValue) filtered = filtered.Where(x => x.Level <= maxLevel.Value);
        // Com filtro de nivel, ordenar por level (depois id) facilita a verificacao
        if (hasLevel) filtered = filtered.OrderBy(x => x.Level).ThenBy(x => x.Id);
        var list = filtered.ToList();
        int matched = list.Count;
        if (limit < 1) limit = 50;
        var pageItems = list.Skip((Math.Max(1, page) - 1) * limit).Take(limit).ToList();
        return Results.Json(Ok(new { itens = pageItems, total, matched, page, limit, tem_mais = (Math.Max(1, page) - 1) * limit + pageItems.Count < matched }));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_LEITURA", ex.Message), statusCode: 400); }
});

app.MapPost("/api/itens/{id:int}", (int id, ItemEdit edit) =>
{
    if (!items.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", "ItemList.bin nao encontrado."), statusCode: 404);
    if (edit.Name.Length > 64) return Results.Json(Err("NOME_LONGO", "Nome excede 64 caracteres."), statusCode: 400);
    if (edit.NameEnglish.Length > 64) return Results.Json(Err("NOME_EN_LONGO", "Nome EN excede 64 caracteres."), statusCode: 400);
    if (edit.Descricao.Length > 128) return Results.Json(Err("DESC_LONGA", "Descricao excede 128 caracteres."), statusCode: 400);
    try
    {
        var (bak, client) = items.Update(id, edit);
        var msg = client.Synced ? "Item salvo. Server (Data\\ItemList.bin) e client (ItemList4.bin) sincronizados. Reinicie o servidor para aplicar."
                                : $"Item salvo no server. {client.Mensagem} Reinicie o servidor para aplicar.";
        return Results.Json(Ok(new { backup = bak, clientSynced = client.Synced, clientBackup = client.Backup, clientMsg = client.Mensagem }, msg));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_GRAVACAO", ex.Message), statusCode: 400); }
});

app.MapGet("/api/itens/selftest", () =>
{
    try { var (ok, total, mm) = items.SelfTest(); return Results.Json(Ok(new { ok, total, firstMismatch = mm })); }
    catch (Exception ex) { return Results.Json(Err("ERRO_SELFTEST", ex.Message), statusCode: 400); }
});

app.MapGet("/api/itens/syncstatus", () =>
{
    try { return Results.Json(Ok(items.SyncStatus())); }
    catch (Exception ex) { return Results.Json(Err("ERRO_SYNC", ex.Message), statusCode: 400); }
});

app.MapPost("/api/itens/sync", () =>
{
    if (!items.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", "ItemList.bin nao encontrado."), statusCode: 404);
    try
    {
        var r = items.SyncClient();
        if (r.Synced) return Results.Json(Ok(new { clientBackup = r.Backup }, r.Mensagem));
        return Results.Json(Err("SYNC_FALHOU", r.Mensagem), statusCode: 400);
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_SYNC", ex.Message), statusCode: 400); }
});

// --- Icons (ItemIcons atlases + IconID->cell map) ---
app.MapGet("/api/iconmeta", () =>
{
    var list = new List<object>();
    for (int a = 1; a <= IconService.AtlasCount; a++)
        list.Add(new { atlas = a, exists = icons.AtlasExists(a) });
    return Results.Json(Ok(new { cell = IconService.Cell, cols = IconService.Cols, atlasCount = IconService.AtlasCount, atlases = list, mapped = icons.Map.Count }));
});

// Full forward map: { "<iconId>": [atlas, cell] } — used by the UI to render per-item sprites.
app.MapGet("/api/iconmap", () => Results.Json(Ok(icons.Map)));

// Atlas sprite-sheet as transparent PNG (cached).
app.MapGet("/api/iconsheet/{atlas:int}", (int atlas) =>
{
    var png = icons.GetSheetPng(atlas);
    if (png == null) return Results.NotFound();
    return Results.Bytes(png, "image/png");
});

// Reverse lookup: which IconID (if any) is linked to this atlas/cell.
app.MapGet("/api/iconcell/{atlas:int}/{cell:int}", (int atlas, int cell) =>
    Results.Json(Ok(new { atlas, cell, iconId = icons.ReverseLookup(atlas, cell) })));

// Link / unlink an IconID to a cell (manual map building). Auth-gated.
app.MapPost("/api/iconmap/{iconId:int}", (int iconId, IconLinkDto dto) =>
{
    if (dto.Atlas < 1 || dto.Atlas > IconService.AtlasCount) return Results.Json(Err("ATLAS_INVALIDO", "atlas 1..11"), statusCode: 400);
    if (dto.Cell < 0 || dto.Cell > 4095) return Results.Json(Err("CELL_INVALIDO", "cell fora do intervalo"), statusCode: 400);
    icons.SetLink(iconId, dto.Atlas, dto.Cell);
    return Results.Json(Ok(new { iconId, atlas = dto.Atlas, cell = dto.Cell }, "Icone vinculado."));
});
app.MapDelete("/api/iconmap/{iconId:int}", (int iconId) =>
    Results.Json(Ok(new { removed = icons.RemoveLink(iconId) })));

// --- Skills (SkillData.bin) ---
app.MapGet("/api/skills", (string? q, int page = 1, int limit = 50) =>
{
    if (!skills.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", $"SkillData.bin nao encontrado em {skills.FilePath}"), statusCode: 404);
    try
    {
        var all = skills.DecodeAll();
        int total = all.Count;
        IEnumerable<SkillEntry> filtered = all;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            filtered = filtered.Where(x =>
                x.Id.ToString() == s ||
                x.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                x.NameEnglish.Contains(s, StringComparison.OrdinalIgnoreCase));
        }
        else filtered = filtered.Where(x => x.Name.Length > 0 || x.NameEnglish.Length > 0);
        var list = filtered.ToList();
        int matched = list.Count;
        if (limit < 1) limit = 50;
        var pageItems = list.Skip((Math.Max(1, page) - 1) * limit).Take(limit).ToList();
        return Results.Json(Ok(new { itens = pageItems, total, matched, page, limit, tem_mais = (Math.Max(1, page) - 1) * limit + pageItems.Count < matched }));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_LEITURA", ex.Message), statusCode: 400); }
});

app.MapPost("/api/skills/{id:int}", (int id, SkillEdit edit) =>
{
    if (!skills.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", "SkillData.bin nao encontrado."), statusCode: 404);
    if (edit.Name.Length > 64) return Results.Json(Err("NOME_LONGO", "Nome excede 64 caracteres."), statusCode: 400);
    if (edit.NameEnglish.Length > 64) return Results.Json(Err("NOME_EN_LONGO", "Nome EN excede 64 caracteres."), statusCode: 400);
    if (edit.Descricao.Length > 288) return Results.Json(Err("DESC_LONGA", "Descricao excede 288 caracteres."), statusCode: 400);
    try
    {
        var (bak, client) = skills.Update(id, edit);
        var msg = client.Synced ? "Skill salva. Server (Data\\SkillData.bin) e client (SkillData4.bin) sincronizados. Reinicie o servidor para aplicar."
                                : $"Skill salva no server. {client.Mensagem} Reinicie o servidor para aplicar.";
        return Results.Json(Ok(new { backup = bak, clientSynced = client.Synced, clientBackup = client.Backup, clientMsg = client.Mensagem }, msg));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_GRAVACAO", ex.Message), statusCode: 400); }
});

app.MapGet("/api/skills/selftest", () =>
{
    try { var (ok, total, mm) = skills.SelfTest(); return Results.Json(Ok(new { ok, total, firstMismatch = mm })); }
    catch (Exception ex) { return Results.Json(Err("ERRO_SELFTEST", ex.Message), statusCode: 400); }
});

app.MapGet("/api/skills/syncstatus", () =>
{
    try { return Results.Json(Ok(skills.SyncStatus())); }
    catch (Exception ex) { return Results.Json(Err("ERRO_SYNC", ex.Message), statusCode: 400); }
});

app.MapPost("/api/skills/sync", () =>
{
    if (!skills.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", "SkillData.bin nao encontrado."), statusCode: 404);
    try
    {
        var r = skills.SyncClient();
        if (r.Synced) return Results.Json(Ok(new { clientBackup = r.Backup }, r.Mensagem));
        return Results.Json(Err("SYNC_FALHOU", r.Mensagem), statusCode: 400);
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_SYNC", ex.Message), statusCode: 400); }
});

// --- Títulos (Title.bin) ---
app.MapGet("/api/titles", () =>
{
    if (!titlesRepo.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", $"Title.bin nao encontrado em {titlesRepo.FilePath}"), statusCode: 404);
    try { return Results.Json(Ok(new { itens = titlesRepo.DecodeAll() })); }
    catch (Exception ex) { return Results.Json(Err("ERRO_LEITURA", ex.Message), statusCode: 400); }
});

app.MapGet("/api/titles/selftest", () =>
{
    try { var (ok, total, mm) = titlesRepo.SelfTest(); return Results.Json(Ok(new { ok, total, firstMismatch = mm })); }
    catch (Exception ex) { return Results.Json(Err("ERRO", ex.Message), statusCode: 400); }
});

app.MapGet("/api/titles/ef-names", () => Results.Json(Ok(EfNames.Map)));

// --- NPCs (Data\NPCs\*.npc) -------------------------------------------------
app.MapGet("/api/npcs", (string? q) =>
{
    if (!npcs.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", $"Pasta de NPCs nao encontrada em {npcs.Dir}"), statusCode: 404);
    try
    {
        var all = npcs.List();
        int total = all.Count;
        IEnumerable<NpcSummary> f = all;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            f = f.Where(x => x.Id.ToString() == s
                || x.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
                || x.Title.Contains(s, StringComparison.OrdinalIgnoreCase)
                || x.NameId.Contains(s, StringComparison.OrdinalIgnoreCase));
        }
        var list = f.ToList();
        return Results.Json(Ok(new { itens = list, total, matched = list.Count }));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_LEITURA", ex.Message), statusCode: 400); }
});

app.MapGet("/api/npcs/cities", () =>
{
    try { return Results.Json(Ok(new { cidades = npcs.Cities() })); }
    catch (Exception ex) { return Results.Json(Err("ERRO_LEITURA", ex.Message), statusCode: 400); }
});

app.MapGet("/api/npcs/freespot", (double x, double y) =>
{
    try { var (fx, fy) = npcs.FreeSpot(x, y); return Results.Json(Ok(new { x = fx, y = fy })); }
    catch (Exception ex) { return Results.Json(Err("ERRO", ex.Message), statusCode: 400); }
});

app.MapGet("/api/npcs/nextid", () =>
{
    try { return Results.Json(Ok(new { id = npcs.NextFreeId() })); }
    catch (Exception ex) { return Results.Json(Err("ERRO", ex.Message), statusCode: 400); }
});

app.MapGet("/api/npcs/selftest", () =>
{
    try { var (ok, total, mm) = npcs.SelfTest(); return Results.Json(Ok(new { ok, total, firstMismatch = mm })); }
    catch (Exception ex) { return Results.Json(Err("ERRO_SELFTEST", ex.Message), statusCode: 400); }
});

app.MapGet("/api/npcs/{id:int}", (int id) =>
{
    try
    {
        var d = npcs.Get(id);
        if (d == null) return Results.Json(Err("NAO_ENCONTRADO", $"NPC {id} nao encontrado."), statusCode: 404);
        return Results.Json(Ok(d));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_LEITURA", ex.Message), statusCode: 400); }
});

app.MapPost("/api/npcs/{id:int}", (int id, NpcEdit edit) =>
{
    if (!npcs.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", "Pasta de NPCs nao encontrada."), statusCode: 404);
    try
    {
        var (bak, warnings) = npcs.Update(id, edit);
        return Results.Json(Ok(new { backup = bak, avisos = warnings }, "NPC salvo. Reinicie o servidor para aplicar."));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_GRAVACAO", ex.Message), statusCode: 400); }
});

app.MapPost("/api/npcs", (NpcCreateDto dto) =>
{
    if (!npcs.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", "Pasta de NPCs nao encontrada."), statusCode: 404);
    try
    {
        var (file, warnings) = npcs.Clone(dto);
        return Results.Json(Ok(new { file, avisos = warnings }, $"NPC criado: {file}"));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_CRIACAO", ex.Message), statusCode: 400); }
});

app.MapDelete("/api/npcs/{id:int}", (int id) =>
{
    try
    {
        var (bak, file) = npcs.Delete(id);
        return Results.Json(Ok(new { backup = bak, file }, $"NPC {id} removido (backup {bak}). Reinicie o servidor."));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_REMOCAO", ex.Message), statusCode: 400); }
});

// Inline price edit from the NPC shop view (sets ItemList SellPrince, re-syncs client).
app.MapPost("/api/itens/{id:int}/preco", (int id, PrecoDto dto) =>
{
    if (!items.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", "ItemList.bin nao encontrado."), statusCode: 404);
    try
    {
        var (bak, client) = items.UpdateSellPrice(id, dto.sellPrince);
        var msg = client.Synced ? "Preco salvo (ItemList) e client sincronizado. Reinicie o servidor."
                                 : $"Preco salvo no server. {client.Mensagem} Reinicie o servidor.";
        return Results.Json(Ok(new { backup = bak, clientSynced = client.Synced }, msg));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_GRAVACAO", ex.Message), statusCode: 400); }
});

// --- Contas / Personagens (MySQL caelite) -----------------------------------
app.MapGet("/api/db/ping", () =>
{
    var (ok, msg) = db.Ping();
    return Results.Json(ok ? Ok(new { ok, msg }) : Err("DB_OFFLINE", msg));
});

app.MapGet("/api/accounts", (string? q, int page = 1, int limit = 50) =>
{
    try
    {
        var (itens, total, matched) = accounts.ListAccounts(q, page, limit);
        return Results.Json(Ok(new { itens, total, matched, page, limit, tem_mais = (Math.Max(1, page) - 1) * limit + itens.Count < matched }));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_DB", ex.Message), statusCode: 400); }
});

app.MapGet("/api/accounts/{id:int}", (uint id) =>
{
    try
    {
        var a = accounts.GetAccount(id);
        return a == null ? Results.Json(Err("NAO_ENCONTRADO", $"Conta {id} nao encontrada."), statusCode: 404) : Results.Json(Ok(a));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_DB", ex.Message), statusCode: 400); }
});

app.MapPost("/api/accounts/{id:int}", (uint id, AccountEdit edit) =>
{
    try
    {
        var (bak, rows) = accounts.UpdateAccount(id, edit);
        return Results.Json(Ok(new { backup = bak, rows }, rows > 0 ? "Conta salva." : "Nada para alterar."));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_GRAVACAO", ex.Message), statusCode: 400); }
});

app.MapPost("/api/accounts", (AccountCreateDto dto) =>
{
    try
    {
        var id = accounts.CreateAccount(dto);
        return Results.Json(Ok(new { id }, $"Conta criada (id {id}). Senha gravada como MD5."));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_CRIACAO", ex.Message), statusCode: 400); }
});

app.MapGet("/api/characters/{id:int}", (uint id) =>
{
    try
    {
        var c = accounts.GetCharacter(id);
        return c == null ? Results.Json(Err("NAO_ENCONTRADO", $"Personagem {id} nao encontrado."), statusCode: 404) : Results.Json(Ok(c));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_DB", ex.Message), statusCode: 400); }
});

app.MapPost("/api/characters/{id:int}", (uint id, Dictionary<string, System.Text.Json.JsonElement> body) =>
{
    try
    {
        var fields = new Dictionary<string, object?>();
        foreach (var kv in body) fields[kv.Key] = JsonToObj(kv.Value);
        var (bak, rows) = accounts.UpdateCharacter(id, fields);
        return Results.Json(Ok(new { backup = bak, rows }, rows > 0 ? "Personagem salvo. (edite com o char OFFLINE)" : "Nada para alterar."));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_GRAVACAO", ex.Message), statusCode: 400); }
});

app.MapGet("/api/characters/{id:int}/items", (uint id) =>
{
    try { return Results.Json(Ok(new { itens = accounts.GetItems(id) })); }
    catch (Exception ex) { return Results.Json(Err("ERRO_DB", ex.Message), statusCode: 400); }
});

app.MapPost("/api/characters/{id:int}/items", (uint id, ItemUpsert u) =>
{
    try
    {
        var bak = accounts.UpsertItem(id, u);
        return Results.Json(Ok(new { backup = bak }, "Item salvo. (edite com o char OFFLINE; reinicie/relogue para ver)"));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_GRAVACAO", ex.Message), statusCode: 400); }
});

app.MapGet("/api/characters/{id:int}/titles", (uint id) =>
{
    try
    {
        var rows = accounts.GetTitles(id);
        var names = titlesRepo.Exists
            ? titlesRepo.DecodeAll().ToDictionary(t => t.Index, t => t.Name)
            : new Dictionary<int, string>();
        foreach (var r in rows) r.Name = names.TryGetValue((int)r.Index, out var n) ? n : "";
        return Results.Json(Ok(new { itens = rows }));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_DB", ex.Message), statusCode: 400); }
});

app.MapPost("/api/characters/{id:int}/titles", (uint id, List<TitleAssign> body) =>
{
    try
    {
        var bak = accounts.SaveTitles(id, body);
        return Results.Json(Ok(new { backup = bak }, "Titulos do personagem salvos. (edite com o char OFFLINE)"));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_GRAVACAO", ex.Message), statusCode: 400); }
});

// --- Itens de Evento (tecla T) ----------------------------------------------
// Entrega item via slot_type=17 (EVENT_ITEM): fica guardado ate o player apertar T.
app.MapPost("/api/eventitem", (EventItemDto dto) =>
{
    if (dto.ItemId <= 0) return Results.Json(Err("ITEM_INVALIDO", "Informe um item valido."), statusCode: 400);
    int qty = dto.Qty <= 0 ? 1 : dto.Qty;
    try
    {
        if (dto.ToAll)
        {
            int n = accounts.GiveEventItemAll(dto.ItemId, qty);
            return Results.Json(Ok(new { entregue = n, todos = true },
                $"Item de evento enviado para {n} personagem(ns). Cada um aperta T no jogo para receber."));
        }
        if (string.IsNullOrWhiteSpace(dto.Target))
            return Results.Json(Err("ALVO_VAZIO", "Informe o nome do personagem ou marque 'Todos'."), statusCode: 400);
        var cid = accounts.FindCharIdByName(dto.Target.Trim());
        if (cid == null) return Results.Json(Err("CHAR_NAO_ENCONTRADO", $"Personagem '{dto.Target}' nao encontrado."), statusCode: 404);
        int rows = accounts.GiveEventItem(cid.Value, dto.ItemId, qty);
        return Results.Json(Ok(new { entregue = rows, charId = cid.Value },
            $"Item de evento enviado para {dto.Target.Trim()}. Ele aperta T no jogo para receber."));
    }
    catch (Exception ex) { return Results.Json(Err("ERRO_DB", ex.Message), statusCode: 400); }
});

Console.WriteLine($"AikaPanel em http://{cfg.Host}:{cfg.Port}  (DataDir: {cfg.DataDir})");
app.Run();
return 0;

static object? JsonToObj(System.Text.Json.JsonElement e) => e.ValueKind switch
{
    System.Text.Json.JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
    System.Text.Json.JsonValueKind.String => e.GetString(),
    System.Text.Json.JsonValueKind.True => 1,
    System.Text.Json.JsonValueKind.False => 0,
    System.Text.Json.JsonValueKind.Null => null,
    _ => e.ToString(),
};

record LoginDto(string password);
record QuestsSaveDto(List<List<string>> linhas);
record PrecoDto(uint sellPrince);
record IconLinkDto(int Atlas, int Cell);
record EventItemDto(string? Target, long ItemId, int Qty, bool ToAll);
