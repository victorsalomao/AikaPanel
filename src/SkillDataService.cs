using System.Buffers.Binary;

namespace AikaPanel;

/// <summary>One SkillData entry (T_SkillData, 720 bytes). Editable + display fields.</summary>
public sealed class SkillEntry
{
    public int Id { get; set; }            // array index (write key)
    public uint Index { get; set; }        // 0   u32 (read-only display)
    public uint MinLevel { get; set; }     // 4
    public uint Level { get; set; }        // 12
    public uint Classification { get; set; } // 16
    public string NameEnglish { get; set; } = ""; // 20  64
    public string Name { get; set; } = "";        // 84  64
    public uint SkillPoints { get; set; }  // 148
    public uint LearnCosts { get; set; }   // 152
    public uint Classe { get; set; }       // 156
    public uint MP { get; set; }           // 172
    public uint Cooldown { get; set; }     // 184
    public uint TargetType { get; set; }   // 192
    public uint MaxTargets { get; set; }   // 200
    public uint Range { get; set; }        // 208
    public uint SuccessRate { get; set; }  // 216
    public uint Damage { get; set; }       // 248
    public uint Duration { get; set; }     // 292
    public uint CastTime { get; set; }     // 320
    public string Descricao { get; set; } = ""; // 428 288
}

public sealed class SkillEdit
{
    public string Name { get; set; } = "";
    public string NameEnglish { get; set; } = "";
    public string Descricao { get; set; } = "";
    public uint MinLevel { get; set; }
    public uint Level { get; set; }
    public uint Classification { get; set; }
    public uint SkillPoints { get; set; }
    public uint LearnCosts { get; set; }
    public uint Classe { get; set; }
    public uint MP { get; set; }
    public uint Cooldown { get; set; }
    public uint TargetType { get; set; }
    public uint MaxTargets { get; set; }
    public uint Range { get; set; }
    public uint SuccessRate { get; set; }
    public uint Damage { get; set; }
    public uint Duration { get; set; }
    public uint CastTime { get; set; }
}

public static class SkillData
{
    public const int RecordSize = 720;
    public const int TrailerSize = 4; // 12000*720 + 4 = 8,640,004 (server cru)

    // Offsets computados da packed T_SkillData em Src/Data/FilesData.pas (sum == 720).
    private const int OffIndex = 0, OffMinLevel = 4, OffLevel = 12, OffClassification = 16;
    private const int OffNameEn = 20, LenNameEn = 64, OffName = 84, LenName = 64;
    private const int OffSkillPoints = 148, OffLearnCosts = 152, OffClasse = 156;
    private const int OffMP = 172, OffCooldown = 184, OffTargetType = 192, OffMaxTargets = 200;
    private const int OffRange = 208, OffSuccessRate = 216, OffDamage = 248, OffDuration = 292, OffCastTime = 320;
    private const int OffDesc = 428, LenDesc = 288;

    private static uint U32(ReadOnlySpan<byte> r, int o) => BinaryPrimitives.ReadUInt32LittleEndian(r.Slice(o, 4));
    private static void W32(Span<byte> r, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(r.Slice(o, 4), v);

    public static SkillEntry Decode(ReadOnlySpan<byte> r, int id) => new()
    {
        Id = id,
        Index = U32(r, OffIndex),
        MinLevel = U32(r, OffMinLevel),
        Level = U32(r, OffLevel),
        Classification = U32(r, OffClassification),
        NameEnglish = PiBin.ReadString(r.Slice(OffNameEn, LenNameEn)),
        Name = PiBin.ReadString(r.Slice(OffName, LenName)),
        SkillPoints = U32(r, OffSkillPoints),
        LearnCosts = U32(r, OffLearnCosts),
        Classe = U32(r, OffClasse),
        MP = U32(r, OffMP),
        Cooldown = U32(r, OffCooldown),
        TargetType = U32(r, OffTargetType),
        MaxTargets = U32(r, OffMaxTargets),
        Range = U32(r, OffRange),
        SuccessRate = U32(r, OffSuccessRate),
        Damage = U32(r, OffDamage),
        Duration = U32(r, OffDuration),
        CastTime = U32(r, OffCastTime),
        Descricao = PiBin.ReadString(r.Slice(OffDesc, LenDesc)),
    };

    public static void ApplyEdit(Span<byte> r, SkillEdit e)
    {
        PiBin.WriteString(r.Slice(OffNameEn, LenNameEn), e.NameEnglish);
        PiBin.WriteString(r.Slice(OffName, LenName), e.Name);
        PiBin.WriteString(r.Slice(OffDesc, LenDesc), e.Descricao);
        W32(r, OffMinLevel, e.MinLevel);
        W32(r, OffLevel, e.Level);
        W32(r, OffClassification, e.Classification);
        W32(r, OffSkillPoints, e.SkillPoints);
        W32(r, OffLearnCosts, e.LearnCosts);
        W32(r, OffClasse, e.Classe);
        W32(r, OffMP, e.MP);
        W32(r, OffCooldown, e.Cooldown);
        W32(r, OffTargetType, e.TargetType);
        W32(r, OffMaxTargets, e.MaxTargets);
        W32(r, OffRange, e.Range);
        W32(r, OffSuccessRate, e.SuccessRate);
        W32(r, OffDamage, e.Damage);
        W32(r, OffDuration, e.Duration);
        W32(r, OffCastTime, e.CastTime);
    }

    public static SkillEdit ToEdit(SkillEntry s) => new()
    {
        Name = s.Name, NameEnglish = s.NameEnglish, Descricao = s.Descricao,
        MinLevel = s.MinLevel, Level = s.Level, Classification = s.Classification,
        SkillPoints = s.SkillPoints, LearnCosts = s.LearnCosts, Classe = s.Classe,
        MP = s.MP, Cooldown = s.Cooldown, TargetType = s.TargetType, MaxTargets = s.MaxTargets,
        Range = s.Range, SuccessRate = s.SuccessRate, Damage = s.Damage, Duration = s.Duration, CastTime = s.CastTime,
    };

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
