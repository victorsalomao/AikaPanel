namespace AikaPanel;

/// <summary>
/// Cifra posicional do MasterEditor (SaveEncriptedFileKey1/Key2), com suporte aos arquivos v4
/// do AIKAClient: um HEADER de 12 bytes em texto (ex. "BR00022I"/"BR00010S") + corpo cifrado.
/// O `j` da cifra REINICIA em 0 no inicio do corpo (apos o header) — foi isso que diferiu do PI.bin.
///
/// Provado byte-a-byte:
///   ItemList4.bin = header("BR00022I") + Encrypt(Data\ItemList.bin, Key1)   [round-trip identico]
///   SkillData4.bin = header("BR00010S") + Encrypt(Data\SkillData.bin, Key2)  [identico ao server]
/// </summary>
public static class V4Cipher
{
    public const int HeaderLen = 12;

    public static readonly byte[] Key1 = Hex(
        "BEC6C0CCC5DB20B8AEBDBAC6AE20C0CEC4DAB5F920B7E7C6BEC0D4B4CFB4D92E20B7EAB7E720B6F6B6F3" +
        "2E2E2E20C0B82E2E2E20C1A4B8BB20C1A4B8BB20B1CDC2FAB4D92E20B1D7B7A1B5B520C7D8BEDFC7CFB4CF" +
        "20BEEEC2BF20BCF620BEF8C1D22E2E2E20");

    public static readonly byte[] Key2 = Hex(
        "C0CCB0C520C0D0C1F620B8B6BCBCBFE42E20C0D0C0B8B8E920B3AABBDBBBE7B6F7B5CBB4CFB4D92E20C1A6" +
        "B9DF20C0FDB4EB20C0D0C1F6B8BBB0ED20C2F8C7D120BBE7B6F7B5EEB7CE20BBE7BCBCBFE42E20BEC6BCCC" +
        "C1D23F20C1C1C0BABCBCBBF3B8B8B5ECBDC3B4D92E");

    private static byte[] Hex(string h)
    {
        var a = new byte[h.Length / 2];
        for (int i = 0; i < a.Length; i++) a[i] = Convert.ToByte(h.Substring(i * 2, 2), 16);
        return a;
    }

    // Loop identico ao Pascal (sign=-1 cifra, +1 decifra), operando numa COPIA do buffer.
    private static byte[] Transform(byte[] input, int sign, byte[] key)
    {
        var buf = (byte[])input.Clone();
        int size = buf.Length;
        bool par = (size % 2) == 0;
        int len = key.Length;
        int j = 0;
        while (j < size - 1)
        {
            buf[j] = (byte)(buf[j] - sign * (key[j % len] + j)); j++;
            buf[j] = (byte)(buf[j] - sign * (key[j % len] + j)); j++;
            if (!par) { buf[j] = (byte)(buf[j] - sign * (key[j % len] + j)); j++; }
        }
        return buf;
    }

    /// <summary>Corpo cru -> corpo cifrado (sem header).</summary>
    public static byte[] Encrypt(byte[] raw, byte[] key) => Transform(raw, -1, key);

    /// <summary>Corpo cifrado -> corpo cru (sem header).</summary>
    public static byte[] Decrypt(byte[] enc, byte[] key) => Transform(enc, +1, key);
}
