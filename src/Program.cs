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
    }
}

var cash = new CashRepository(cfg);
var quests = new QuestRepository(cfg);
var items = new ItemRepository(cfg);
var skills = new SkillRepository(cfg);

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
app.UseStaticFiles(new StaticFileOptions { FileProvider = webFiles });

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
    if (path.StartsWith("/api/") && path != "/api/health" && path != "/api/login")
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
app.MapGet("/api/itens", (string? q, int page = 1, int limit = 50) =>
{
    if (!items.Exists) return Results.Json(Err("ARQUIVO_NAO_ENCONTRADO", $"ItemList.bin nao encontrado em {items.FilePath}"), statusCode: 404);
    try
    {
        var all = items.DecodeAll();
        int total = all.Count;
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
            // Sem busca: esconder slots totalmente vazios (sem nenhum nome) para a lista ficar util
            filtered = filtered.Where(x => x.Name.Length > 0 || x.NameEnglish.Length > 0);
        }
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

Console.WriteLine($"AikaPanel em http://{cfg.Host}:{cfg.Port}  (DataDir: {cfg.DataDir})");
app.Run();
return 0;

record LoginDto(string password);
record QuestsSaveDto(List<List<string>> linhas);
