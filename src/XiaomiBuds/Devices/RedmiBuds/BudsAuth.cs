using System.Security.Cryptography;

namespace XiaomiBuds.Devices.RedmiBuds;

/// <summary>
/// Redmi Buds challenge/response authentication: a Bluetooth-flavoured SAFER+ variant
/// (128-bit key, 8 rounds). The peer's 16-byte challenge is the key, a fixed constant the plaintext.
/// Ported from Gadgetbridge (AGPL-3.0-or-later, (c) 2024 Jonathan Gobbo).
/// </summary>
public static class BudsAuth
{
    const int Pattern = 0x9999;

    static readonly byte[] Seq =
    [
        0x11, 0x22, 0x33, 0x33, 0x22, 0x11, 0x11, 0x22,
        0x33, 0x33, 0x22, 0x11, 0x11, 0x22, 0x33, 0x33,
    ];

    static readonly int[,] Coefficients =
    {
        { 2, 1, 1, 1, 4, 2, 1, 1, 2, 2, 4, 2, 4, 4, 16, 8 },
        { 2, 1, 1, 1, 4, 2, 1, 1, 1, 1, 2, 1, 2, 2, 8, 4 },
        { 1, 1, 4, 2, 2, 2, 4, 2, 16, 8, 4, 4, 2, 1, 1, 1 },
        { 1, 1, 4, 2, 1, 1, 2, 1, 8, 4, 2, 2, 2, 1, 1, 1 },
        { 16, 8, 2, 2, 4, 2, 4, 4, 1, 1, 4, 2, 1, 1, 2, 1 },
        { 8, 4, 1, 1, 2, 1, 2, 2, 1, 1, 4, 2, 1, 1, 2, 1 },
        { 2, 2, 4, 2, 4, 4, 16, 8, 2, 1, 1, 1, 4, 2, 1, 1 },
        { 1, 1, 2, 1, 2, 2, 8, 4, 2, 1, 1, 1, 4, 2, 1, 1 },
        { 4, 2, 4, 4, 16, 8, 2, 2, 1, 1, 2, 1, 1, 1, 4, 2 },
        { 2, 1, 2, 2, 8, 4, 1, 1, 1, 1, 2, 1, 1, 1, 4, 2 },
        { 4, 4, 16, 8, 1, 1, 2, 1, 4, 2, 1, 1, 4, 2, 2, 2 },
        { 2, 2, 8, 4, 1, 1, 2, 1, 4, 2, 1, 1, 2, 1, 1, 1 },
        { 1, 1, 2, 1, 1, 1, 4, 2, 4, 4, 16, 8, 2, 2, 4, 2 },
        { 1, 1, 2, 1, 1, 1, 4, 2, 2, 2, 8, 4, 1, 1, 2, 1 },
        { 4, 2, 1, 1, 2, 1, 1, 1, 4, 2, 2, 2, 16, 8, 4, 4 },
        { 4, 2, 1, 1, 2, 1, 1, 1, 2, 1, 1, 1, 8, 4, 2, 2 },
    };

    static readonly int[] Exp = new int[256];
    static readonly int[] Log = new int[256];
    static readonly int[,] Bias = new int[16, 16];

    static BudsAuth()
    {
        for (int i = 0; i < 256; i++)
            Exp[i] = i == 128 ? 0 : ModPow(45, i, 257);
        Log[0] = 128;
        for (int i = 1; i < 256; i++)
        {
            int v = ModPow(45, i, 257);
            if (v != 256) Log[v] = i;
        }
        for (int i = 0; i < 16; i++)
            for (int j = 0; j < 16; j++)
            {
                int e = 17 * (i + 2) + (j + 1);
                int v = ModPow(45, ModPow(45, e, 257), 257);
                Bias[i, j] = v == 256 ? 0 : v;
            }
    }

    static int ModPow(long b, long e, long m)
    {
        long r = 1;
        b %= m;
        while (e > 0)
        {
            if ((e & 1) == 1) r = r * b % m;
            b = b * b % m;
            e >>= 1;
        }
        return (int)r;
    }

    static bool IsXor(int i) => ((1 << i) & Pattern) != 0;

    static int[][] KeySchedule(byte[] key)
    {
        var k0 = key.Select(b => (int)b).ToArray();
        k0[15] ^= 6;
        var keys = new List<int[]> { k0 };
        var reg = new int[17];
        Array.Copy(k0, reg, 16);
        reg[16] = k0.Aggregate(0, (a, b) => a ^ b);
        for (int idx = 1; idx <= 16; idx++)
        {
            for (int i = 0; i < 17; i++) reg[i] = ((reg[i] >> 5) | (reg[i] << 3)) & 0xFF;
            var k = new int[16];
            for (int i = 0; i < 16; i++) k[i] = (reg[(idx + i) % 17] + Bias[idx - 1, i]) & 0xFF;
            keys.Add(k);
        }
        return keys.ToArray();
    }

    static byte[] Encrypt(byte[] plain, int[][] keys)
    {
        var c = plain.Select(b => (int)b).ToArray();
        var t = new int[16];
        for (int rnd = 0; rnd < 8; rnd++)
        {
            if (rnd == 2)
                for (int i = 0; i < 16; i++) c[i] = IsXor(i) ? c[i] ^ plain[i] : (c[i] + plain[i]) & 0xFF;
            var k = keys[rnd * 2];
            for (int i = 0; i < 16; i++) c[i] = IsXor(i) ? c[i] ^ k[i] : (c[i] + k[i]) & 0xFF;
            for (int i = 0; i < 16; i++) c[i] = IsXor(i) ? Exp[c[i]] & 0xFF : Log[c[i]];
            k = keys[rnd * 2 + 1];
            for (int i = 0; i < 16; i++) c[i] = IsXor(i) ? (k[i] + c[i]) & 0xFF : k[i] ^ c[i];
            for (int i = 0; i < 16; i++)
            {
                int s = 0;
                for (int j = 0; j < 16; j++) s += Coefficients[i, j] * c[j];
                t[i] = s & 0xFF;
            }
            Array.Copy(t, c, 16);
        }
        var last = keys[16];
        var o = new byte[16];
        for (int i = 0; i < 16; i++) o[i] = (byte)(IsXor(i) ? last[i] ^ c[i] : (last[i] + c[i]) & 0xFF);
        return o;
    }

    /// <summary>Answer a 16-byte challenge sent by the earbuds.</summary>
    public static byte[] ComputeResponse(byte[] challenge)
    {
        if (challenge.Length != 16) throw new ArgumentException("challenge must be 16 bytes");
        return Encrypt(Seq, KeySchedule(challenge));
    }

    public static byte[] RandomChallenge() => RandomNumberGenerator.GetBytes(16);
}
