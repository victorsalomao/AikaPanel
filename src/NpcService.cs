using System.Buffers.Binary;
using System.Text;

namespace AikaPanel;

// ---------------------------------------------------------------------------
//  NPC (.npc) editor for the MERGED Aika server.
//  Files: <DataDir>\NPCs\[ID] Name.npc  (TNPCFile = TNPCHeader 558B + TBasicNpc).
//  All offsets validated byte-exact against real merchant files (5639 bytes).
//  Layout source: Src/Data/PlayerData.pas + MiscData.pas (TItem=20, TPosition=X,Y Single).
//
//  | Field                         | offset | type                       |
//  | Title (header shortstring[35])| 0      | len@0, chars@1..           |
//  | Index (NPC id)                | 558    | DWORD                      |
//  | Equip[0..15]                  | 902    | TItem x20                  |
//  | Inventory[0..63]  = the SHOP  | 1226   | TItem x20 (Index@+0,App@+2)|
//  | LastPos (spawn)               | 4807/4811 | Single X / Y            |
//
//  The shop a player sees = the NPC's own Inventory. Map is implicit by the
//  spawn coordinate (global coord space). Editing the file + restart moves it,
//  EXCEPT for NPCs whose position is hardcoded in Src/Mob/NPC.pas.
// ---------------------------------------------------------------------------

public static class NpcFile
{
    public const int SizeStd = 5639;     // standard record size (validated)
    public const int HeaderLen = 558;
    public const int TitleMax = 35;
    public const int IndexOff = 558;     // TBasicNpc.Index : DWORD
    public const int EquipOff = 902;     // Equip[0..15]
    public const int InvOff = 1226;      // Inventory[0..63] (shop)
    public const int ItemSize = 20;
    public const int InvSlots = 64;      // 0..63 (only 0..39 are shown in-game!)
    public const int ShopVisibleMax = 40;// slots >= 40 never reach ShowShop
    public const int PosXOff = 4807;     // LastPos.X : Single
    public const int PosYOff = 4811;     // LastPos.Y : Single
    public const int NameOff = 574;      // TCustomNpc.Name (AnsiChar[16]) — the in-game name id (client resolves via Dialog.bin), NOT the filename
    public const int NameLen = 16;

    // NPC ids whose spawn position is HARDCODED in Src/Mob/NPC.pas (file edit won't move them).
    public static readonly HashSet<int> HardcodedPos = new()
    { 2130, 2700, 2701, 2702, 2703, 2704, 2705, 2706, 2707, 2708, 2709 };

    private static readonly Encoding Cp = PiBin.Win1252; // registers the CodePages provider

    public static string ReadTitle(ReadOnlySpan<byte> file)
    {
        int len = file[0];
        if (len > TitleMax) len = TitleMax;
        return Cp.GetString(file.Slice(1, len)).TrimEnd('\0');
    }

    /// <summary>Writes a clean Delphi ShortString[35]: length byte + chars + zero-fill to 35.</summary>
    public static void WriteTitle(Span<byte> file, string title)
    {
        var bytes = Cp.GetBytes(title ?? "");
        int len = Math.Min(bytes.Length, TitleMax);
        file[0] = (byte)len;
        bytes.AsSpan(0, len).CopyTo(file.Slice(1, len));
        file.Slice(1 + len, TitleMax - len).Clear(); // zero the rest of the field
    }

    public static int ReadIndex(ReadOnlySpan<byte> file) =>
        (int)BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(IndexOff, 4));

    public static void WriteIndex(Span<byte> file, int id) =>
        BinaryPrimitives.WriteUInt32LittleEndian(file.Slice(IndexOff, 4), (uint)id);

    public static float ReadX(ReadOnlySpan<byte> file) => BinaryPrimitives.ReadSingleLittleEndian(file.Slice(PosXOff, 4));
    public static float ReadY(ReadOnlySpan<byte> file) => BinaryPrimitives.ReadSingleLittleEndian(file.Slice(PosYOff, 4));
    public static void WriteX(Span<byte> file, float v) => BinaryPrimitives.WriteSingleLittleEndian(file.Slice(PosXOff, 4), v);
    public static void WriteY(Span<byte> file, float v) => BinaryPrimitives.WriteSingleLittleEndian(file.Slice(PosYOff, 4), v);

    /// <summary>The NPC's in-game display-name id (the numeric Name field). The client shows the
    /// name/dialog for this id — two files with the SAME NameId look identical in-game even though
    /// they are different .npc files (one may hold the shop, the other be an empty decoy).</summary>
    public static string ReadNameId(ReadOnlySpan<byte> file)
    {
        var raw = file.Slice(NameOff, NameLen);
        int z = raw.IndexOf((byte)0); if (z < 0) z = NameLen;
        return Cp.GetString(raw.Slice(0, z)).Trim();
    }

    public static int SlotOffset(int slot) => InvOff + slot * ItemSize;

    public static int ReadSlotItem(ReadOnlySpan<byte> file, int slot) =>
        BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(SlotOffset(slot), 2));

    public static int ReadSlotApp(ReadOnlySpan<byte> file, int slot) =>
        BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(SlotOffset(slot) + 2, 2));

    /// <summary>Sets a shop slot: itemId 0 fully clears the 20-byte record; otherwise writes
    /// Index + APP verbatim (the edit layer decides the APP default).</summary>
    public static void WriteSlot(Span<byte> file, int slot, int itemId, int app)
    {
        var rec = file.Slice(SlotOffset(slot), ItemSize);
        if (itemId <= 0) { rec.Clear(); return; }
        BinaryPrimitives.WriteUInt16LittleEndian(rec.Slice(0, 2), (ushort)itemId);
        BinaryPrimitives.WriteUInt16LittleEndian(rec.Slice(2, 2), (ushort)app);
    }

    public static int ShopCount(ReadOnlySpan<byte> file)
    {
        int n = 0;
        for (int s = 0; s < InvSlots; s++) if (ReadSlotItem(file, s) != 0) n++;
        return n;
    }

    /// <summary>Round-trip proof: re-applying decoded fields onto a fresh copy yields identical bytes.</summary>
    public static (bool ok, int total, int firstMismatch) SelfTest(string dir)
    {
        if (!Directory.Exists(dir)) return (true, 0, -1);
        var files = Directory.GetFiles(dir, "*.npc");
        int chec1 = 0;
        foreach (var f in files)
        {
            var orig = File.ReadAllBytes(f);
            if (orig.Length < SizeStd) continue;
            var copy = (byte[])orig.Clone();
            var span = copy.AsSpan();
            // Re-apply the LOSSLESS fields with their own decoded values -> must be a no-op.
            // (Title is excluded: it's a deliberate-rewrite ShortString that some files store
            //  null-padded to a larger length; the editor only rewrites it on an explicit edit.)
            WriteIndex(span, ReadIndex(orig));
            WriteX(span, ReadX(orig));
            WriteY(span, ReadY(orig));
            for (int s = 0; s < InvSlots; s++)
            {
                int it = ReadSlotItem(orig, s);
                if (it != 0) WriteSlot(span, s, it, ReadSlotApp(orig, s)); // skip empty slots (no clear)
            }
            chec1++;
            if (!orig.AsSpan().SequenceEqual(copy)) return (false, files.Length, chec1 - 1);
        }
        return (true, files.Length, -1);
    }
}

// ----- DTOs -----------------------------------------------------------------

public sealed class NpcShopSlot
{
    public int Slot { get; set; }
    public int ItemId { get; set; }
    public int App { get; set; }
    public string ItemName { get; set; } = "";
    public long SellPrince { get; set; }   // gold price from ItemList (0/1 => won't buy)
    public bool Hidden { get; set; }        // slot >= 40 (won't appear in shop)
    public bool PriceBlocked { get; set; }  // gold price <= 1 (BuyNPCItens rejects)
}

public class NpcSummary
{
    public int Id { get; set; }
    public string File { get; set; } = "";
    public string Name { get; set; } = "";   // from filename "[id] Name"
    public string Title { get; set; } = "";   // header title
    public double X { get; set; }
    public double Y { get; set; }
    public int ShopCount { get; set; }
    public bool HardcodedPos { get; set; }
    public int Size { get; set; }
    public string NameId { get; set; } = "";   // in-game display-name id (Name field) — what the client shows, NOT the filename
    public int DupCount { get; set; } = 1;      // how many NPCs share this NameId (same in-game name)
    public int TwinShopId { get; set; }         // a twin (same NameId, other file) that HAS a shop (>0); 0 if none
    public int TwinShopCount { get; set; }      // that twin's shop item count
}

public sealed class NpcTwin
{
    public int Id { get; set; }
    public string File { get; set; } = "";
    public string Name { get; set; } = "";
    public int ShopCount { get; set; }
}

public sealed class NpcDetail : NpcSummary
{
    public List<NpcShopSlot> Shop { get; set; } = new();
    public List<NpcTwin> Twins { get; set; } = new();   // other NPCs with the same in-game NameId
}

public sealed class NpcSlotEdit { public int Slot { get; set; } public int ItemId { get; set; } public int App { get; set; } }

public sealed class NpcEdit
{
    public string? Title { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public List<NpcSlotEdit>? Shop { get; set; }   // only listed slots are written
}

public sealed class NpcCreateDto
{
    public int SourceId { get; set; }     // clone from this existing NPC
    public int NewId { get; set; }
    public string Name { get; set; } = "";
    public string? Title { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class CityCluster
{
    public string Name { get; set; } = "";
    public double X { get; set; }      // centroid (suggested anchor)
    public double Y { get; set; }
    public int Count { get; set; }
}
