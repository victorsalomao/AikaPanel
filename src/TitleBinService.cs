using System.Buffers.Binary;

namespace AikaPanel;

/// <summary>Um nível de título (TitleLevel, 56 bytes) — representação editável.
/// Layout (offsets internos) confirmado por probe contra Src/Data/FilesData.pas:498.</summary>
public sealed class TitleLevelData
{
    public int Level { get; set; }                      // 1..4 (slot+1) — chave de escrita dentro do título
    public byte Hide { get; set; }                      // off 0  HideTitle (0/1)
    public byte Type { get; set; }                      // off 1  TitleType
    public ushort Goal { get; set; }                    // off 2  TitleGoal
    public ushort[] Ef { get; set; } = new ushort[3];   // off 4  3× código de atributo (EF)
    public ushort[] Efv { get; set; } = new ushort[3];  // off 10 3× valor do atributo
    public string Name { get; set; } = "";              // off 16 32 bytes (Win-1252)
    public byte Category { get; set; }                  // off 48 TitleCategory
    public byte Idx { get; set; }                       // off 49 TitleIndex gravado no registro
    public uint Color { get; set; }                     // off 52 TitleCollor
    // off 50..51 (Null_1) NÃO exposto — preservado byte-a-byte
}

/// <summary>Um título = 4 níveis. Name = nome do nível 1 (para pickers).</summary>
public sealed class TitleData
{
    public int Index { get; set; }                      // 0..254
    public string Name { get; set; } = "";
    public List<TitleLevelData> Levels { get; set; } = new();
}

public static class TitleBin
{
    public const int LevelSize = 56;
    public const int LevelsPerTitle = 4;
    public const int TitleCount = 255;
    public const int TitlesRegion = TitleCount * LevelsPerTitle * LevelSize; // 57120
    public const int TrailerSize = 98528;                                    // cauda não usada pelo server, preservada
    public const int ExpectedFileSize = TitlesRegion + TrailerSize;          // 155648

    private const int OffHide = 0, OffType = 1, OffGoal = 2, OffEf = 4, OffEfv = 10,
                      OffName = 16, LenName = 32, OffCategory = 48, OffIdx = 49, OffColor = 52;

    private static ushort U16(ReadOnlySpan<byte> r, int o) => BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(o, 2));
    private static void W16(Span<byte> r, int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(r.Slice(o, 2), v);
    private static uint U32(ReadOnlySpan<byte> r, int o) => BinaryPrimitives.ReadUInt32LittleEndian(r.Slice(o, 4));
    private static void W32(Span<byte> r, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(r.Slice(o, 4), v);

    public static TitleLevelData DecodeLevel(ReadOnlySpan<byte> r, int level) => new()
    {
        Level = level,
        Hide = r[OffHide], Type = r[OffType], Goal = U16(r, OffGoal),
        Ef  = new[] { U16(r, OffEf),  U16(r, OffEf + 2),  U16(r, OffEf + 4) },
        Efv = new[] { U16(r, OffEfv), U16(r, OffEfv + 2), U16(r, OffEfv + 4) },
        Name = PiBin.ReadString(r.Slice(OffName, LenName)),
        Category = r[OffCategory], Idx = r[OffIdx], Color = U32(r, OffColor),
    };

    /// <summary>Modifica só os campos editáveis. Null_1 (off 50..51) e quaisquer bytes após o
    /// terminador do nome são preservados (PiBin.WriteString não limpa o resto).</summary>
    public static void ApplyLevel(Span<byte> r, TitleLevelData e)
    {
        r[OffHide] = e.Hide; r[OffType] = e.Type; W16(r, OffGoal, e.Goal);
        W16(r, OffEf, e.Ef[0]); W16(r, OffEf + 2, e.Ef[1]); W16(r, OffEf + 4, e.Ef[2]);
        W16(r, OffEfv, e.Efv[0]); W16(r, OffEfv + 2, e.Efv[1]); W16(r, OffEfv + 4, e.Efv[2]);
        PiBin.WriteString(r.Slice(OffName, LenName), e.Name);
        r[OffCategory] = e.Category; r[OffIdx] = e.Idx; W32(r, OffColor, e.Color);
    }

    public static TitleData DecodeTitle(byte[] file, int titleIndex)
    {
        var t = new TitleData { Index = titleIndex };
        for (int l = 0; l < LevelsPerTitle; l++)
        {
            int off = (titleIndex * LevelsPerTitle + l) * LevelSize;
            t.Levels.Add(DecodeLevel(file.AsSpan(off, LevelSize), l + 1));
        }
        t.Name = t.Levels[0].Name;
        return t;
    }

    public static List<TitleData> DecodeAll(byte[] file)
    {
        var list = new List<TitleData>(TitleCount);
        for (int i = 0; i < TitleCount; i++) list.Add(DecodeTitle(file, i));
        return list;
    }

    /// <summary>Round-trip nos 1020 registros de nível da região de títulos. O trailing nunca é
    /// tocado por ApplyLevel, então fica intacto.</summary>
    public static (bool ok, int total, int firstMismatch) SelfTest(byte[] file)
    {
        if (file.Length != ExpectedFileSize)
            throw new InvalidOperationException($"Title.bin tem {file.Length} bytes, esperado {ExpectedFileSize}.");
        int count = TitleCount * LevelsPerTitle; // 1020
        var scratch = new byte[LevelSize];
        for (int i = 0; i < count; i++)
        {
            var orig = file.AsSpan(i * LevelSize, LevelSize);
            orig.CopyTo(scratch);
            ApplyLevel(scratch, DecodeLevel(orig, (i % LevelsPerTitle) + 1));
            if (!orig.SequenceEqual(scratch)) return (false, count, i);
        }
        return (true, count, -1);
    }
}
