using System.Buffers.Binary;
using System.Text;

namespace AikaPanel;

/// <summary>One Cash Shop entry (TPremiumItem). Only editable fields + read-only display fields.</summary>
public sealed class CashItem
{
    public int Slot { get; set; }          // file position 0..5999 (write key, NOT stored in file)
    public uint Indice { get; set; }       // offset 0  u32  (read-only display)
    public byte Show { get; set; }         // offset 6  byte (editable: 1=visivel,0=oculto)
    public string Name { get; set; } = ""; // offset 8  64 bytes (editable)
    public string Descricao { get; set; } = ""; // offset 72 256 bytes (editable)
    public uint Price { get; set; }        // offset 328 u32 (editable)
    public ushort Amount { get; set; }     // offset 334 u16 (editable)
    public ushort ItemIndex { get; set; }  // offset 336 u16 (editable)
}

/// <summary>Fields the client is allowed to change.</summary>
public sealed class CashEdit
{
    public byte Show { get; set; }
    public string Name { get; set; } = "";
    public string Descricao { get; set; } = "";
    public uint Price { get; set; }
    public ushort Amount { get; set; }
    public ushort ItemIndex { get; set; }
}

public static class PiBin
{
    public const int RecordSize = 376;
    public const int ExpectedRecords = 6000;
    // The live PI.bin is 6000*376 + 4 trailing bytes (a checksum/magic). Records are 0-aligned;
    // the 4-byte trailer is preserved byte-for-byte because we always read/write the whole file.
    public const int TrailerSize = 4;

    // Field offsets inside a record
    private const int OffIndex = 0;     // u32
    private const int OffShow = 6;      // byte
    private const int OffName = 8;      // 64 bytes
    private const int LenName = 64;
    private const int OffDesc = 72;     // 256 bytes
    private const int LenDesc = 256;
    private const int OffPrice = 328;   // u32
    private const int OffAmount = 334;  // u16
    private const int OffItemIndex = 336; // u16

    private static Encoding? _win1252;
    public static Encoding Win1252
    {
        get
        {
            if (_win1252 == null)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _win1252 = Encoding.GetEncoding(1252);
            }
            return _win1252;
        }
    }

    public static string ReadString(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        if (end < 0) end = field.Length;
        return Win1252.GetString(field.Slice(0, end));
    }

    /// <summary>
    /// Writes a string into a fixed Delphi-style field: copy the bytes, then a single null
    /// terminator (only if it fits) — and DO NOT clear the rest of the buffer. The existing
    /// editor / Delphi leave old trailing bytes after the terminator untouched, so preserving
    /// them is what makes round-trip byte-exact and matches the real write behavior.
    /// </summary>
    public static void WriteString(Span<byte> field, string value)
    {
        var bytes = Win1252.GetBytes(value ?? "");
        int n = Math.Min(bytes.Length, field.Length);
        bytes.AsSpan(0, n).CopyTo(field);
        if (n < field.Length) field[n] = 0; // terminator; trailing bytes left as-is
    }

    public static CashItem Decode(ReadOnlySpan<byte> rec, int slot)
    {
        return new CashItem
        {
            Slot = slot,
            Indice = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(OffIndex, 4)),
            Show = rec[OffShow],
            Name = ReadString(rec.Slice(OffName, LenName)),
            Descricao = ReadString(rec.Slice(OffDesc, LenDesc)),
            Price = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(OffPrice, 4)),
            Amount = BinaryPrimitives.ReadUInt16LittleEndian(rec.Slice(OffAmount, 2)),
            ItemIndex = BinaryPrimitives.ReadUInt16LittleEndian(rec.Slice(OffItemIndex, 2)),
        };
    }

    /// <summary>Modifies ONLY the editable fields in-place. Every other byte is preserved.</summary>
    public static void ApplyEdit(Span<byte> rec, CashEdit e)
    {
        rec[OffShow] = e.Show;
        WriteString(rec.Slice(OffName, LenName), e.Name);
        WriteString(rec.Slice(OffDesc, LenDesc), e.Descricao);
        BinaryPrimitives.WriteUInt32LittleEndian(rec.Slice(OffPrice, 4), e.Price);
        BinaryPrimitives.WriteUInt16LittleEndian(rec.Slice(OffAmount, 2), e.Amount);
        BinaryPrimitives.WriteUInt16LittleEndian(rec.Slice(OffItemIndex, 2), e.ItemIndex);
    }

    /// <summary>
    /// Proof of byte-exactness: for every record, encode(decode(record)) must equal the original
    /// bytes. Returns (ok, totalRecords, firstMismatchSlot).
    /// </summary>
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
            var item = Decode(orig, i);
            ApplyEdit(scratch, new CashEdit
            {
                Show = item.Show,
                Name = item.Name,
                Descricao = item.Descricao,
                Price = item.Price,
                Amount = item.Amount,
                ItemIndex = item.ItemIndex,
            });
            if (!orig.SequenceEqual(scratch))
                return (false, count, i);
        }
        return (true, count, -1);
    }
}
