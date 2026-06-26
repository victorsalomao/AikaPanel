using System.Buffers.Binary;

namespace AikaPanel;

/// <summary>One ItemList entry (TItemFromList). Editable + display fields only.</summary>
public sealed class ItemEntry
{
    public int Id { get; set; }            // array index 0..30999 (write key)
    public string Name { get; set; } = "";        // 0   64 PT
    public string NameEnglish { get; set; } = ""; // 64  64 EN
    public string Descricao { get; set; } = "";   // 128 128
    public ushort ItemType { get; set; }   // 258
    public ushort UseEffect { get; set; }  // 268
    public ushort Classe { get; set; }     // 300
    public ushort IconID { get; set; }     // 320
    public ushort Level { get; set; }      // 330
    public uint PriceHonor { get; set; }   // 280
    public uint PriceMedal { get; set; }   // 284
    public uint PriceGold { get; set; }    // 288
    public uint SellPrince { get; set; }   // 292
    public ushort TypePriceItem { get; set; }      // 440
    public ushort TypePriceItemValue { get; set; } // 442
    public ushort ATKFis { get; set; }     // 358
    public ushort DefFis { get; set; }     // 360
    public ushort MagATK { get; set; }     // 362
    public ushort DefMag { get; set; }     // 364
    public ushort HP { get; set; }         // 372
    public ushort MP { get; set; }         // 374
    public byte TypeItem { get; set; }     // 390 raridade
    public byte TypeTrade { get; set; }    // 393
}

/// <summary>Fields the client may change (same shape as ItemEntry minus Id).</summary>
public sealed class ItemEdit
{
    public string Name { get; set; } = "";
    public string NameEnglish { get; set; } = "";
    public string Descricao { get; set; } = "";
    public ushort ItemType { get; set; }
    public ushort UseEffect { get; set; }
    public ushort Classe { get; set; }
    public ushort IconID { get; set; }
    public ushort Level { get; set; }
    public uint PriceHonor { get; set; }
    public uint PriceMedal { get; set; }
    public uint PriceGold { get; set; }
    public uint SellPrince { get; set; }
    public ushort TypePriceItem { get; set; }
    public ushort TypePriceItemValue { get; set; }
    public ushort ATKFis { get; set; }
    public ushort DefFis { get; set; }
    public ushort MagATK { get; set; }
    public ushort DefMag { get; set; }
    public ushort HP { get; set; }
    public ushort MP { get; set; }
    public byte TypeItem { get; set; }
    public byte TypeTrade { get; set; }
}

public static class ItemList
{
    public const int RecordSize = 464;
    public const int ExpectedRecords = 31000;
    public const int TrailerSize = 4; // 31000*464 + 4 = 14,384,004 (checksum/magic, preserved)

    // Offsets computed from packed TItemFromList in Src/Data/FilesData.pas (~L13-75); sum == 464.
    private const int OffName = 0; private const int LenName = 64;
    private const int OffNameEn = 64; private const int LenNameEn = 64;
    private const int OffDesc = 128; private const int LenDesc = 128;
    private const int OffItemType = 258;
    private const int OffUseEffect = 268;
    private const int OffPriceHonor = 280;
    private const int OffPriceMedal = 284;
    private const int OffPriceGold = 288;
    private const int OffSellPrince = 292;
    private const int OffClasse = 300;
    private const int OffIconID = 320;
    private const int OffLevel = 330;
    private const int OffATKFis = 358;
    private const int OffDefFis = 360;
    private const int OffMagATK = 362;
    private const int OffDefMag = 364;
    private const int OffHP = 372;
    private const int OffMP = 374;
    private const int OffTypeItem = 390;
    private const int OffTypeTrade = 393;
    private const int OffTypePriceItem = 440;
    private const int OffTypePriceItemValue = 442;

    private static ushort U16(ReadOnlySpan<byte> r, int o) => BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(o, 2));
    private static uint U32(ReadOnlySpan<byte> r, int o) => BinaryPrimitives.ReadUInt32LittleEndian(r.Slice(o, 4));
    private static void W16(Span<byte> r, int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(r.Slice(o, 2), v);
    private static void W32(Span<byte> r, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(r.Slice(o, 4), v);

    public static ItemEntry Decode(ReadOnlySpan<byte> r, int id) => new()
    {
        Id = id,
        Name = PiBin.ReadString(r.Slice(OffName, LenName)),
        NameEnglish = PiBin.ReadString(r.Slice(OffNameEn, LenNameEn)),
        Descricao = PiBin.ReadString(r.Slice(OffDesc, LenDesc)),
        ItemType = U16(r, OffItemType),
        UseEffect = U16(r, OffUseEffect),
        Classe = U16(r, OffClasse),
        IconID = U16(r, OffIconID),
        Level = U16(r, OffLevel),
        PriceHonor = U32(r, OffPriceHonor),
        PriceMedal = U32(r, OffPriceMedal),
        PriceGold = U32(r, OffPriceGold),
        SellPrince = U32(r, OffSellPrince),
        TypePriceItem = U16(r, OffTypePriceItem),
        TypePriceItemValue = U16(r, OffTypePriceItemValue),
        ATKFis = U16(r, OffATKFis),
        DefFis = U16(r, OffDefFis),
        MagATK = U16(r, OffMagATK),
        DefMag = U16(r, OffDefMag),
        HP = U16(r, OffHP),
        MP = U16(r, OffMP),
        TypeItem = r[OffTypeItem],
        TypeTrade = r[OffTypeTrade],
    };

    /// <summary>Modifies ONLY editable fields in-place; every other byte (and trailer) is preserved.</summary>
    public static void ApplyEdit(Span<byte> r, ItemEdit e)
    {
        PiBin.WriteString(r.Slice(OffName, LenName), e.Name);
        PiBin.WriteString(r.Slice(OffNameEn, LenNameEn), e.NameEnglish);
        PiBin.WriteString(r.Slice(OffDesc, LenDesc), e.Descricao);
        W16(r, OffItemType, e.ItemType);
        W16(r, OffUseEffect, e.UseEffect);
        W16(r, OffClasse, e.Classe);
        W16(r, OffIconID, e.IconID);
        W16(r, OffLevel, e.Level);
        W32(r, OffPriceHonor, e.PriceHonor);
        W32(r, OffPriceMedal, e.PriceMedal);
        W32(r, OffPriceGold, e.PriceGold);
        W32(r, OffSellPrince, e.SellPrince);
        W16(r, OffTypePriceItem, e.TypePriceItem);
        W16(r, OffTypePriceItemValue, e.TypePriceItemValue);
        W16(r, OffATKFis, e.ATKFis);
        W16(r, OffDefFis, e.DefFis);
        W16(r, OffMagATK, e.MagATK);
        W16(r, OffDefMag, e.DefMag);
        W16(r, OffHP, e.HP);
        W16(r, OffMP, e.MP);
        r[OffTypeItem] = e.TypeItem;
        r[OffTypeTrade] = e.TypeTrade;
    }

    public static ItemEdit ToEdit(ItemEntry i) => new()
    {
        Name = i.Name, NameEnglish = i.NameEnglish, Descricao = i.Descricao,
        ItemType = i.ItemType, UseEffect = i.UseEffect, Classe = i.Classe, IconID = i.IconID, Level = i.Level,
        PriceHonor = i.PriceHonor, PriceMedal = i.PriceMedal, PriceGold = i.PriceGold, SellPrince = i.SellPrince,
        TypePriceItem = i.TypePriceItem, TypePriceItemValue = i.TypePriceItemValue,
        ATKFis = i.ATKFis, DefFis = i.DefFis, MagATK = i.MagATK, DefMag = i.DefMag, HP = i.HP, MP = i.MP,
        TypeItem = i.TypeItem, TypeTrade = i.TypeTrade,
    };

    /// <summary>Byte-exact proof: encode(decode(record)) == original for all records + trailer.</summary>
    public static (bool ok, int total, int firstMismatch) SelfTest(byte[] file)
    {
        int rem = file.Length % RecordSize;
        if (rem != 0 && rem != TrailerSize)
            throw new InvalidOperationException($"Tamanho {file.Length} invalido (resto {rem}, esperado 0 ou {TrailerSize}).");
        int count = file.Length / RecordSize;
        var scratch = new byte[RecordSize];
        for (int i = 0; i < count; i++)
        {
            var orig = file.AsSpan(i * RecordSize, RecordSize);
            orig.CopyTo(scratch);
            ApplyEdit(scratch, ToEdit(Decode(orig, i)));
            if (!orig.SequenceEqual(scratch)) return (false, count, i);
        }
        return (true, count, -1);
    }
}
