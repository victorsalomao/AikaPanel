namespace AikaPanel;

/// <summary>
/// Cifra/decifra o catalogo da Loja de Cash (PI.bin) com a Key1 do MasterEditor.
/// Replica EXATAMENTE TFunctions.SaveEncriptedFileKey1 (Functions.pas:84-149):
///   sign = -1; par = (size mod 2 == 0)
///   while j &lt; size-1: processa 2 bytes (e um 3o quando 'par' e falso), avancando j.
///   encrypt: buffer[j] := buffer[j] - sign*(cipher[j mod len] + j)  => + (cipher + j)
/// O server le Data\PI.bin (cru); o client le UI\PI.bin (cifrado) — mesma estrutura.
/// </summary>
public static class CashCrypto
{
    // Key1 (hex) do MasterEditor — 102 bytes.
    private const string Key1Hex =
        "BEC6C0CCC5DB20B8AEBDBAC6AE20C0CEC4DAB5F920B7E7C6BEC0D4B4CFB4D92E20B7EAB7E720B6F6B6F3" +
        "2E2E2E20C0B82E2E2E20C1A4B8BB20C1A4B8BB20B1CDC2FAB4D92E20B1D7B7A1B5B520C7D8BEDFC7CFB4CF" +
        "20BEEEC2BF20BCF620BEF8C1D22E2E2E20";

    private static readonly byte[] Cipher = StrToArray(Key1Hex);
    public static int CipherLength => Cipher.Length; // CountStr(Key1) = 102

    // StrToArray: hex string -> bytes (cada par de hex vira 1 byte).
    private static byte[] StrToArray(string hex)
    {
        var arr = new byte[hex.Length / 2];
        for (int i = 0; i < arr.Length; i++)
            arr[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return arr;
    }

    // sign = -1 (encrypt). Loop identico ao Pascal, operando in-place numa COPIA.
    private static byte[] Transform(byte[] input, int sign)
    {
        var buffer = (byte[])input.Clone();
        int size = buffer.Length;
        bool par = (size % 2) == 0;
        int len = Cipher.Length;
        int j = 0;
        while (j < size - 1)
        {
            buffer[j] = (byte)(buffer[j] - sign * (Cipher[j % len] + j)); j++;
            buffer[j] = (byte)(buffer[j] - sign * (Cipher[j % len] + j)); j++;
            if (!par)
            {
                buffer[j] = (byte)(buffer[j] - sign * (Cipher[j % len] + j)); j++;
            }
        }
        return buffer;
    }

    /// <summary>Data\PI.bin (cru) -> UI\PI.bin (cifrado). sign=-1 como no MasterEditor.</summary>
    public static byte[] Encrypt(byte[] raw) => Transform(raw, -1);

    /// <summary>UI\PI.bin (cifrado) -> Data\PI.bin (cru). Inverso (sign=+1).</summary>
    public static byte[] Decrypt(byte[] enc) => Transform(enc, +1);
}
