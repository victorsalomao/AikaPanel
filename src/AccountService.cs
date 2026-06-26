using MySqlConnector;

namespace AikaPanel;

// ---------------------------------------------------------------------------
//  Account + Character editor over the caelite MySQL DB (normalized schema).
//  Tables: accounts, characters, items (slot_type 0=equip, 1=inventory, 2=storage).
//  Password = MD5(password) lowercase hex (matches Src/Forms/AccCreateForm.pas).
//  Every UPDATE snapshots the prior row(s) to panel_backups\*.json first.
//  ⚠ Edit characters while OFFLINE: the server caches logged-in chars and
//  overwrites the DB on save/logout.
// ---------------------------------------------------------------------------

// ----- DTOs -----------------------------------------------------------------
public class AccountSummary
{
    public uint Id { get; set; }
    public string Username { get; set; } = "";
    public string Mail { get; set; } = "";
    public uint AccountType { get; set; }
    public uint AccountStatus { get; set; }
    public uint Nation { get; set; }
    public long Cash { get; set; }
    public long StorageGold { get; set; }
    public string IsActive { get; set; } = "";
    public long BanDays { get; set; }
    public string PremiumTime { get; set; } = "";
    public string Discord { get; set; } = "";
    public int CharCount { get; set; }
}

public sealed class CharSummary
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public uint Slot { get; set; }
    public uint Level { get; set; }
    public uint ClassInfo { get; set; }
    public long Gold { get; set; }
    public bool Deleted { get; set; }
}

public sealed class AccountDetail : AccountSummary
{
    public List<CharSummary> Characters { get; set; } = new();
}

public sealed class AccountEdit
{
    public string? Username { get; set; }
    public string? Mail { get; set; }
    public uint? Nation { get; set; }
    public uint? AccountType { get; set; }
    public uint? AccountStatus { get; set; }
    public long? Cash { get; set; }
    public long? StorageGold { get; set; }
    public long? BanDays { get; set; }
    public string? PremiumTime { get; set; }
    public string? IsActive { get; set; }
    public string? Discord { get; set; }
    public string? NewPassword { get; set; } // if set, resets password_hash = md5(NewPassword)
}

public sealed class AccountCreateDto
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Mail { get; set; } = "";
    public uint Nation { get; set; } = 1;
    public uint AccountType { get; set; }
    public long Cash { get; set; }
}

public sealed class CharacterDetail
{
    public uint Id { get; set; }
    public uint OwnerAccId { get; set; }
    public string Name { get; set; } = "";
    public uint Slot { get; set; }
    public uint Level { get; set; }
    public ulong Experience { get; set; }
    public uint ClassInfo { get; set; }
    public uint Strength { get; set; }
    public uint Agility { get; set; }
    public uint Intelligence { get; set; }
    public uint Constitution { get; set; }
    public uint Luck { get; set; }
    public uint Status { get; set; }
    public uint Altura { get; set; }
    public uint Tronco { get; set; }
    public uint Perna { get; set; }
    public uint Corpo { get; set; }
    public long CurHp { get; set; }
    public long CurMp { get; set; }
    public long Honor { get; set; }
    public long Killpoint { get; set; }
    public long Infamia { get; set; }
    public long Skillpoint { get; set; }
    public long Gold { get; set; }
    public uint GuildIndex { get; set; }
    public uint Posx { get; set; }
    public uint Posy { get; set; }
    public uint ActiveTitle { get; set; }
    public bool PlayerKill { get; set; }
    public bool Deleted { get; set; }
}

public sealed class ItemRow
{
    public int SlotType { get; set; }   // 0 equip, 1 inventory, 2 storage
    public int Slot { get; set; }
    public long ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public long App { get; set; }
    public long Identific { get; set; }
    public int Effect1Index { get; set; }
    public int Effect1Value { get; set; }
    public int Effect2Index { get; set; }
    public int Effect2Value { get; set; }
    public int Effect3Index { get; set; }
    public int Effect3Value { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }
    public int Refine { get; set; }
    public long Time { get; set; }
}

public sealed class ItemUpsert
{
    public int SlotType { get; set; }
    public int Slot { get; set; }
    public long ItemId { get; set; }   // 0 = clear the slot
    public long App { get; set; }
    public int Refine { get; set; } = 1;
    public long Identific { get; set; }
    public int Effect1Index { get; set; }
    public int Effect1Value { get; set; }
    public int Effect2Index { get; set; }
    public int Effect2Value { get; set; }
    public int Effect3Index { get; set; }
    public int Effect3Value { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }
    public long Time { get; set; }
}

public sealed class TitleRow
{
    public uint Index { get; set; }
    public uint Level { get; set; }
    public uint Progress { get; set; }
    public string Name { get; set; } = "";
}

public sealed class TitleAssign
{
    public uint Index { get; set; }
    public uint Level { get; set; }
    public uint Progress { get; set; }
}

// ----- Repository -----------------------------------------------------------
public sealed class AccountRepository
{
    private readonly Db _db;
    private readonly ItemNames _names;
    private readonly PanelConfig _cfg;
    public AccountRepository(Db db, ItemNames names, PanelConfig cfg) { _db = db; _names = names; _cfg = cfg; }

    private static long L(object o) => o == DBNull.Value ? 0 : Convert.ToInt64(o);
    private static uint U(object o) => o == DBNull.Value ? 0u : Convert.ToUInt32(o);
    private static ulong UL(object o) => o == DBNull.Value ? 0ul : Convert.ToUInt64(o);
    private static string S(object o) => o == DBNull.Value ? "" : Convert.ToString(o) ?? "";

    public (List<AccountSummary> itens, int total, int matched) ListAccounts(string? q, int page, int limit)
    {
        using var con = _db.Open();
        string where = "";
        if (!string.IsNullOrWhiteSpace(q))
            where = "WHERE a.id = @qi OR a.username LIKE @qs OR a.mail LIKE @qs ";
        int total;
        using (var ct = new MySqlCommand("SELECT COUNT(*) FROM accounts", con)) total = Convert.ToInt32(ct.ExecuteScalar());
        int matched = total;
        var sql = $@"SELECT a.id,a.username,a.mail,a.account_type,a.account_status,a.nation,a.cash,a.storage_gold,
                     a.isactive,a.ban_days,a.premium_time,a.discord,
                     (SELECT COUNT(*) FROM characters c WHERE c.owner_accid=a.id AND c.deleted=0) cc
                     FROM accounts a {where} ORDER BY a.id LIMIT @lim OFFSET @off";
        var list = new List<AccountSummary>();
        if (limit < 1) limit = 50;
        using (var cmd = new MySqlCommand(sql, con))
        {
            if (where.Length > 0)
            {
                cmd.Parameters.AddWithValue("@qi", int.TryParse(q, out var qi) ? qi : -1);
                cmd.Parameters.AddWithValue("@qs", "%" + q!.Trim() + "%");
                using (var mc = new MySqlCommand($"SELECT COUNT(*) FROM accounts a {where}", con))
                {
                    mc.Parameters.AddWithValue("@qi", int.TryParse(q, out var qi2) ? qi2 : -1);
                    mc.Parameters.AddWithValue("@qs", "%" + q.Trim() + "%");
                    matched = Convert.ToInt32(mc.ExecuteScalar());
                }
            }
            cmd.Parameters.AddWithValue("@lim", limit);
            cmd.Parameters.AddWithValue("@off", (Math.Max(1, page) - 1) * limit);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) list.Add(ReadSummary(rd));
        }
        return (list, total, matched);
    }

    private static AccountSummary ReadSummary(MySqlDataReader rd) => new()
    {
        Id = U(rd["id"]), Username = S(rd["username"]), Mail = S(rd["mail"]),
        AccountType = U(rd["account_type"]), AccountStatus = U(rd["account_status"]), Nation = U(rd["nation"]),
        Cash = L(rd["cash"]), StorageGold = L(rd["storage_gold"]), IsActive = S(rd["isactive"]),
        BanDays = L(rd["ban_days"]), PremiumTime = S(rd["premium_time"]), Discord = S(rd["discord"]),
        CharCount = Convert.ToInt32(rd["cc"]),
    };

    public AccountDetail? GetAccount(uint id)
    {
        using var con = _db.Open();
        AccountDetail? acc = null;
        using (var cmd = new MySqlCommand(
            @"SELECT a.id,a.username,a.mail,a.account_type,a.account_status,a.nation,a.cash,a.storage_gold,
              a.isactive,a.ban_days,a.premium_time,a.discord,0 cc FROM accounts a WHERE a.id=@id", con))
        {
            cmd.Parameters.AddWithValue("@id", id);
            using var rd = cmd.ExecuteReader();
            if (rd.Read())
            {
                var s = ReadSummary(rd);
                acc = new AccountDetail
                {
                    Id = s.Id, Username = s.Username, Mail = s.Mail, AccountType = s.AccountType,
                    AccountStatus = s.AccountStatus, Nation = s.Nation, Cash = s.Cash, StorageGold = s.StorageGold,
                    IsActive = s.IsActive, BanDays = s.BanDays, PremiumTime = s.PremiumTime, Discord = s.Discord,
                };
            }
        }
        if (acc == null) return null;
        using (var cmd = new MySqlCommand(
            "SELECT id,name,slot,level,classinfo,gold,deleted FROM characters WHERE owner_accid=@id ORDER BY slot", con))
        {
            cmd.Parameters.AddWithValue("@id", id);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                acc.Characters.Add(new CharSummary
                {
                    Id = U(rd["id"]), Name = S(rd["name"]), Slot = U(rd["slot"]), Level = U(rd["level"]),
                    ClassInfo = U(rd["classinfo"]), Gold = L(rd["gold"]), Deleted = U(rd["deleted"]) != 0,
                });
        }
        acc.CharCount = acc.Characters.Count(c => !c.Deleted);
        return acc;
    }

    public (string backup, int rows) UpdateAccount(uint id, AccountEdit e)
    {
        using var con = _db.Open();
        var before = GetAccount(id) ?? throw new InvalidOperationException($"Conta {id} nao encontrada.");
        var bak = DbBackup.Save(_cfg, $"account_{id}", before);

        var sets = new List<string>();
        var cmd = new MySqlCommand { Connection = con };
        void Set(string col, string p, object? val) { sets.Add($"{col}=@{p}"); cmd.Parameters.AddWithValue("@" + p, val ?? DBNull.Value); }
        if (e.Username != null) Set("username", "u", e.Username);
        if (e.Mail != null) Set("mail", "m", e.Mail);
        if (e.Nation.HasValue) Set("nation", "n", e.Nation.Value);
        if (e.AccountType.HasValue) Set("account_type", "at", e.AccountType.Value);
        if (e.AccountStatus.HasValue) Set("account_status", "as", e.AccountStatus.Value);
        if (e.Cash.HasValue) Set("cash", "c", e.Cash.Value);
        if (e.StorageGold.HasValue) Set("storage_gold", "sg", e.StorageGold.Value);
        if (e.BanDays.HasValue) Set("ban_days", "bd", e.BanDays.Value);
        if (e.PremiumTime != null) Set("premium_time", "pt", e.PremiumTime);
        if (e.IsActive != null) Set("isactive", "ia", e.IsActive);
        if (e.Discord != null) Set("discord", "d", e.Discord);
        if (!string.IsNullOrEmpty(e.NewPassword)) Set("password_hash", "pw", Db.Md5Hex(e.NewPassword));
        if (sets.Count == 0) return (bak, 0);

        cmd.CommandText = $"UPDATE accounts SET {string.Join(",", sets)} WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        int rows = cmd.ExecuteNonQuery();
        return (bak, rows);
    }

    public uint CreateAccount(AccountCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username)) throw new ArgumentException("Usuario obrigatorio.");
        if (string.IsNullOrEmpty(dto.Password)) throw new ArgumentException("Senha obrigatoria.");
        using var con = _db.Open();
        using (var c = new MySqlCommand("SELECT id FROM accounts WHERE LOWER(username)=LOWER(@u) LIMIT 1", con))
        { c.Parameters.AddWithValue("@u", dto.Username); if (c.ExecuteScalar() != null) throw new InvalidOperationException("Usuario ja existe."); }
        if (!string.IsNullOrWhiteSpace(dto.Mail))
            using (var c = new MySqlCommand("SELECT id FROM accounts WHERE LOWER(mail)=LOWER(@m) LIMIT 1", con))
            { c.Parameters.AddWithValue("@m", dto.Mail); if (c.ExecuteScalar() != null) throw new InvalidOperationException("E-mail ja cadastrado."); }

        uint newId;
        using (var c = new MySqlCommand("SELECT IFNULL(MAX(id),0)+1 FROM accounts", con)) newId = Convert.ToUInt32(c.ExecuteScalar());
        using (var c = new MySqlCommand(
            @"INSERT INTO accounts (id,forum_id,username,password_hash,mail,nation,account_type,cash)
              VALUES (@id,@id,@u,@pw,@m,@n,@at,@c)", con))
        {
            c.Parameters.AddWithValue("@id", newId);
            c.Parameters.AddWithValue("@u", dto.Username);
            c.Parameters.AddWithValue("@pw", Db.Md5Hex(dto.Password));
            c.Parameters.AddWithValue("@m", dto.Mail ?? "");
            c.Parameters.AddWithValue("@n", dto.Nation);
            c.Parameters.AddWithValue("@at", dto.AccountType);
            c.Parameters.AddWithValue("@c", dto.Cash);
            c.ExecuteNonQuery();
        }
        return newId;
    }

    // ----- characters -----
    public CharacterDetail? GetCharacter(uint id)
    {
        using var con = _db.Open();
        using var cmd = new MySqlCommand("SELECT * FROM characters WHERE id=@id", con);
        cmd.Parameters.AddWithValue("@id", id);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        return new CharacterDetail
        {
            Id = U(rd["id"]), OwnerAccId = U(rd["owner_accid"]), Name = S(rd["name"]), Slot = U(rd["slot"]),
            Level = U(rd["level"]), Experience = UL(rd["experience"]), ClassInfo = U(rd["classinfo"]),
            Strength = U(rd["strength"]), Agility = U(rd["agility"]), Intelligence = U(rd["intelligence"]),
            Constitution = U(rd["constitution"]), Luck = U(rd["luck"]), Status = U(rd["status"]),
            Altura = U(rd["altura"]), Tronco = U(rd["tronco"]), Perna = U(rd["perna"]), Corpo = U(rd["corpo"]),
            CurHp = L(rd["curhp"]), CurMp = L(rd["curmp"]), Honor = L(rd["honor"]), Killpoint = L(rd["killpoint"]),
            Infamia = L(rd["infamia"]), Skillpoint = L(rd["skillpoint"]), Gold = L(rd["gold"]),
            GuildIndex = U(rd["guildindex"]), Posx = U(rd["posx"]), Posy = U(rd["posy"]),
            ActiveTitle = U(rd["active_title"]), PlayerKill = U(rd["playerkill"]) != 0, Deleted = U(rd["deleted"]) != 0,
        };
    }

    public (string backup, int rows) UpdateCharacter(uint id, Dictionary<string, object?> fields)
    {
        if (fields.Count == 0) return ("", 0);
        using var con = _db.Open();
        var before = GetCharacter(id) ?? throw new InvalidOperationException($"Personagem {id} nao encontrado.");
        var bak = DbBackup.Save(_cfg, $"character_{id}", before);
        // whitelist of editable columns -> guards against arbitrary column writes
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "name","slot","level","experience","classinfo","strength","agility","intelligence","constitution",
          "luck","status","altura","tronco","perna","corpo","curhp","curmp","honor","killpoint","infamia",
          "skillpoint","gold","guildindex","posx","posy","active_title","playerkill","deleted" };
        var sets = new List<string>();
        var cmd = new MySqlCommand { Connection = con };
        int i = 0;
        foreach (var kv in fields)
        {
            if (!allowed.Contains(kv.Key)) continue;
            var p = "p" + (i++);
            sets.Add($"`{kv.Key}`=@{p}");
            cmd.Parameters.AddWithValue("@" + p, kv.Value ?? DBNull.Value);
        }
        if (sets.Count == 0) return (bak, 0);
        cmd.CommandText = $"UPDATE characters SET {string.Join(",", sets)} WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        return (bak, cmd.ExecuteNonQuery());
    }

    // ----- titles -----
    public List<TitleRow> GetTitles(uint charId)
    {
        using var con = _db.Open();
        using var cmd = new MySqlCommand(
            "SELECT title_index,title_level,title_progress FROM titles WHERE owner_charid=@c ORDER BY title_index", con);
        cmd.Parameters.AddWithValue("@c", charId);
        var list = new List<TitleRow>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new TitleRow { Index = U(rd["title_index"]), Level = U(rd["title_level"]), Progress = U(rd["title_progress"]) });
        return list;
    }

    /// <summary>Substitui TODOS os títulos do char (DELETE + INSERT em transação). Backup JSON antes.
    /// Espelha a proc save_titles. Index 0 é ignorado (placeholder "nenhum").</summary>
    public string SaveTitles(uint charId, List<TitleAssign> titles)
    {
        using var con = _db.Open();
        // snapshot dos títulos atuais para backup reversível
        var prior = new List<Dictionary<string, object?>>();
        using (var sel = new MySqlCommand("SELECT title_index,title_level,title_progress FROM titles WHERE owner_charid=@c", con))
        {
            sel.Parameters.AddWithValue("@c", charId);
            using var rd = sel.ExecuteReader();
            while (rd.Read())
            {
                var d = new Dictionary<string, object?>();
                for (int i = 0; i < rd.FieldCount; i++) d[rd.GetName(i)] = rd.IsDBNull(i) ? null : rd.GetValue(i);
                prior.Add(d);
            }
        }
        var bak = DbBackup.Save(_cfg, $"titles_{charId}", prior);

        using var tx = con.BeginTransaction();
        try
        {
            using (var del = new MySqlCommand("DELETE FROM titles WHERE owner_charid=@c", con, tx))
            {
                del.Parameters.AddWithValue("@c", charId);
                del.ExecuteNonQuery();
            }
            foreach (var t in titles ?? new List<TitleAssign>())
            {
                if (t.Index == 0) continue; // 0 = "nenhum"
                using var ins = new MySqlCommand(
                    "INSERT INTO titles (owner_charid,title_index,title_level,title_progress) VALUES (@c,@i,@l,@p)", con, tx);
                ins.Parameters.AddWithValue("@c", charId);
                ins.Parameters.AddWithValue("@i", t.Index);
                ins.Parameters.AddWithValue("@l", t.Level);
                ins.Parameters.AddWithValue("@p", t.Progress);
                ins.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
        return bak;
    }

    // ----- items -----
    public List<ItemRow> GetItems(uint ownerId)
    {
        using var con = _db.Open();
        using var cmd = new MySqlCommand(
            @"SELECT slot_type,slot,item_id,app,identific,effect1_index,effect1_value,effect2_index,effect2_value,
              effect3_index,effect3_value,min,max,refine,time FROM items WHERE owner_id=@o ORDER BY slot_type,slot", con);
        cmd.Parameters.AddWithValue("@o", ownerId);
        var list = new List<ItemRow>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            int item = (int)L(rd["item_id"]);
            if (item == 0) continue;
            list.Add(new ItemRow
            {
                SlotType = (int)L(rd["slot_type"]), Slot = (int)L(rd["slot"]), ItemId = item, ItemName = _names.Name(item),
                App = L(rd["app"]), Identific = L(rd["identific"]),
                Effect1Index = (int)L(rd["effect1_index"]), Effect1Value = (int)L(rd["effect1_value"]),
                Effect2Index = (int)L(rd["effect2_index"]), Effect2Value = (int)L(rd["effect2_value"]),
                Effect3Index = (int)L(rd["effect3_index"]), Effect3Value = (int)L(rd["effect3_value"]),
                Min = (int)L(rd["min"]), Max = (int)L(rd["max"]), Refine = (int)L(rd["refine"]), Time = L(rd["time"]),
            });
        }
        return list;
    }

    /// <summary>Sets one slot: clears it, then inserts when ItemId&gt;0. Backs up the prior slot first.</summary>
    public string UpsertItem(uint ownerId, ItemUpsert u)
    {
        using var con = _db.Open();
        // snapshot prior row(s) for this slot
        using (var sel = new MySqlCommand("SELECT * FROM items WHERE owner_id=@o AND slot_type=@st AND slot=@s", con))
        {
            sel.Parameters.AddWithValue("@o", ownerId);
            sel.Parameters.AddWithValue("@st", u.SlotType);
            sel.Parameters.AddWithValue("@s", u.Slot);
            var rows = new List<Dictionary<string, object?>>();
            using (var rd = sel.ExecuteReader())
                while (rd.Read())
                {
                    var d = new Dictionary<string, object?>();
                    for (int i = 0; i < rd.FieldCount; i++) d[rd.GetName(i)] = rd.IsDBNull(i) ? null : rd.GetValue(i);
                    rows.Add(d);
                }
            var bak = DbBackup.Save(_cfg, $"item_{ownerId}_{u.SlotType}_{u.Slot}", rows);

            using (var del = new MySqlCommand("DELETE FROM items WHERE owner_id=@o AND slot_type=@st AND slot=@s", con))
            {
                del.Parameters.AddWithValue("@o", ownerId);
                del.Parameters.AddWithValue("@st", u.SlotType);
                del.Parameters.AddWithValue("@s", u.Slot);
                del.ExecuteNonQuery();
            }
            if (u.ItemId > 0)
            {
                using var ins = new MySqlCommand(
                    @"INSERT INTO items (slot_type,owner_id,slot,item_id,app,identific,
                      effect1_index,effect1_value,effect2_index,effect2_value,effect3_index,effect3_value,
                      min,max,refine,time)
                      VALUES (@st,@o,@s,@id,@app,@idf,@e1i,@e1v,@e2i,@e2v,@e3i,@e3v,@min,@max,@ref,@time)", con);
                ins.Parameters.AddWithValue("@st", u.SlotType);
                ins.Parameters.AddWithValue("@o", ownerId);
                ins.Parameters.AddWithValue("@s", u.Slot);
                ins.Parameters.AddWithValue("@id", u.ItemId);
                ins.Parameters.AddWithValue("@app", u.App == 0 ? u.ItemId : u.App);
                ins.Parameters.AddWithValue("@idf", u.Identific);
                ins.Parameters.AddWithValue("@e1i", u.Effect1Index);
                ins.Parameters.AddWithValue("@e1v", u.Effect1Value);
                ins.Parameters.AddWithValue("@e2i", u.Effect2Index);
                ins.Parameters.AddWithValue("@e2v", u.Effect2Value);
                ins.Parameters.AddWithValue("@e3i", u.Effect3Index);
                ins.Parameters.AddWithValue("@e3v", u.Effect3Value);
                ins.Parameters.AddWithValue("@min", u.Min);
                ins.Parameters.AddWithValue("@max", u.Max);
                ins.Parameters.AddWithValue("@ref", u.Refine <= 0 ? 1 : u.Refine);
                ins.Parameters.AddWithValue("@time", u.Time);
                ins.ExecuteNonQuery();
            }
            return bak;
        }
    }

    // ----- itens de evento (tecla T) -----
    // slot_type 17 = EVENT_ITEM no servidor (Src/Data/GlobalDefs.pas). O item fica
    // "guardado" nessa linha ate o player apertar T no jogo (GetAllEventItems o move
    // para o inventario). A quantidade vai no campo refine, igual o servidor faz.
    public const int EventSlotType = 17;

    /// <summary>Resolve um personagem ativo pelo nome exato. Retorna null se nao achar.</summary>
    public uint? FindCharIdByName(string name)
    {
        using var con = _db.Open();
        using var cmd = new MySqlCommand(
            "SELECT id FROM characters WHERE name=@n AND deleted=0 LIMIT 1", con);
        cmd.Parameters.AddWithValue("@n", name);
        var r = cmd.ExecuteScalar();
        return (r == null || r == DBNull.Value) ? (uint?)null : Convert.ToUInt32(r);
    }

    /// <summary>Insere 1 item de evento para um personagem. Retorna linhas inseridas (1).</summary>
    public int GiveEventItem(uint charId, long itemId, int qty)
    {
        if (itemId <= 0) throw new ArgumentException("Item invalido.");
        using var con = _db.Open();
        using var ins = new MySqlCommand(
            "INSERT INTO items (slot_type,owner_id,slot,item_id,refine) VALUES (@st,@o,0,@id,@ref)", con);
        ins.Parameters.AddWithValue("@st", EventSlotType);
        ins.Parameters.AddWithValue("@o", charId);
        ins.Parameters.AddWithValue("@id", itemId);
        ins.Parameters.AddWithValue("@ref", qty <= 0 ? 1 : qty);
        return ins.ExecuteNonQuery();
    }

    /// <summary>Insere o item de evento para TODOS os personagens ativos numa unica query.
    /// Offline tambem recebe (pega ao logar e apertar T). Retorna quantos foram afetados.</summary>
    public int GiveEventItemAll(long itemId, int qty)
    {
        if (itemId <= 0) throw new ArgumentException("Item invalido.");
        using var con = _db.Open();
        using var ins = new MySqlCommand(
            "INSERT INTO items (slot_type,owner_id,slot,item_id,refine) " +
            "SELECT @st, id, 0, @id, @ref FROM characters WHERE deleted=0", con);
        ins.Parameters.AddWithValue("@st", EventSlotType);
        ins.Parameters.AddWithValue("@id", itemId);
        ins.Parameters.AddWithValue("@ref", qty <= 0 ? 1 : qty);
        return ins.ExecuteNonQuery();
    }

    public void DeleteTestAccount(uint id)
    {
        using var con = _db.Open();
        // chars of this account
        var charIds = new List<uint>();
        using (var c = new MySqlCommand("SELECT id FROM characters WHERE owner_accid=@id", con))
        { c.Parameters.AddWithValue("@id", id); using var rd = c.ExecuteReader(); while (rd.Read()) charIds.Add(U(rd["id"])); }
        foreach (var cid in charIds)
            using (var c = new MySqlCommand("DELETE FROM items WHERE owner_id=@c", con)) { c.Parameters.AddWithValue("@c", cid); c.ExecuteNonQuery(); }
        using (var c = new MySqlCommand("DELETE FROM characters WHERE owner_accid=@id", con)) { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); }
        using (var c = new MySqlCommand("DELETE FROM accounts WHERE id=@id", con)) { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); }
    }
}
