// SRP6a для WoW 3.3.5a — обе стороны (клиент и сервер).
//
// Active MITM требует чтобы мы:
//   1) выступили клиентом к реальному серверу (используем настоящий пароль) → получаем K_server
//   2) выступили сервером к реальному клиенту (генерим свой challenge) → получаем K_client
// Из обеих сессий K разные, потому что у каждой свои случайные приватные экспоненты a/b.

using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace WowBot.MitmProxy;

/// <summary>Стандартные параметры SRP6 для WoW 3.3.5a.</summary>
internal static class SrpConstants
{
    // N (LE) — стандартный модуль 256 бит из 3.3.5
    public static readonly byte[] N_le =
    {
        0xB7, 0x9B, 0x3E, 0x2A, 0x87, 0x82, 0x3C, 0xAB,
        0x8F, 0x5E, 0xBF, 0xBF, 0x8E, 0xB1, 0x01, 0x08,
        0x53, 0x50, 0x06, 0x29, 0x8B, 0x5B, 0xAD, 0xBD,
        0x5B, 0x53, 0xE1, 0x89, 0x5E, 0x64, 0x4B, 0x89
    };
    public const byte g = 7;
    public static readonly BigInteger k = new BigInteger(3);

    public static BigInteger N => Util.LeToBig(N_le);
    public static BigInteger gBig => new BigInteger(g);
}

internal sealed record LogonChallenge(byte[] B_le, byte g, byte[] N_le, byte[] Salt);
internal sealed record SrpResult(byte[] A_le, byte[] M1, byte[] K);

/// <summary>Клиентская SRP6: получили challenge от сервера, считаем A, M1, K.</summary>
internal static class Srp6Client
{
    public static SrpResult ComputeProof(string account, string password, LogonChallenge ch)
    {
        var N = Util.LeToBig(ch.N_le);
        var g = new BigInteger(ch.g);
        var B = Util.LeToBig(ch.B_le);
        var k = new BigInteger(3);

        var idHash = Util.Sha1(Encoding.UTF8.GetBytes($"{account}:{password}"));
        var x = Util.LeToBig(Util.Sha1(Util.Concat(ch.Salt, idHash)));

        var aBytes = new byte[19];
        RandomNumberGenerator.Fill(aBytes);
        var a = Util.LeToBig(aBytes);

        var A = BigInteger.ModPow(g, a, N);
        var aLe = Util.BigToLe(A, 32);

        var u = Util.LeToBig(Util.Sha1(Util.Concat(aLe, ch.B_le)));
        var gx = BigInteger.ModPow(g, x, N);
        var sub = (B - k * gx) % N;
        if (sub < 0) sub += N;
        var S = BigInteger.ModPow(sub, a + u * x, N);
        var sLe = Util.BigToLe(S, 32);

        var K = Util.InterleaveHash(sLe);

        var hN = Util.Sha1(ch.N_le);
        var hG = Util.Sha1(new[] { ch.g });
        var hNg = new byte[20];
        for (var i = 0; i < 20; i++) hNg[i] = (byte)(hN[i] ^ hG[i]);
        var hAcc = Util.Sha1(Encoding.UTF8.GetBytes(account));
        var m1 = Util.Sha1(Util.Concat(hNg, hAcc, ch.Salt, aLe, ch.B_le, K));
        return new SrpResult(aLe, m1, K);
    }
}

/// <summary>Серверная SRP6: знаем пароль (для MITM), генерим свой challenge, ждём A/M1 от клиента.</summary>
internal sealed class Srp6Server
{
    private readonly string _account;
    public byte[] Salt { get; }
    public byte[] B_le { get; }
    public byte g => SrpConstants.g;
    public byte[] N_le => SrpConstants.N_le;

    private readonly BigInteger _b;        // приватный экспонент сервера
    private readonly BigInteger _v;        // verifier g^x mod N
    private readonly BigInteger _N;

    public Srp6Server(string account, string password)
    {
        _account = account.ToUpperInvariant();
        _N = SrpConstants.N;

        // salt — случайный 32-байтный
        Salt = new byte[32];
        RandomNumberGenerator.Fill(Salt);

        // x = SHA1(salt | SHA1(account:password))
        var idHash = Util.Sha1(Encoding.UTF8.GetBytes($"{_account}:{password.ToUpperInvariant()}"));
        var x = Util.LeToBig(Util.Sha1(Util.Concat(Salt, idHash)));
        _v = BigInteger.ModPow(SrpConstants.gBig, x, _N);

        // b — приватный экспонент сервера, 19 байт
        var bBytes = new byte[19];
        RandomNumberGenerator.Fill(bBytes);
        _b = Util.LeToBig(bBytes);

        // B = (k*v + g^b) mod N
        var gB = BigInteger.ModPow(SrpConstants.gBig, _b, _N);
        var B = (SrpConstants.k * _v + gB) % _N;
        B_le = Util.BigToLe(B, 32);
    }

    /// <summary>На основании присланного клиентом A и M1 — посчитать K и проверить proof.</summary>
    public (byte[] K, byte[] M2)? AcceptProof(byte[] A_le, byte[] clientM1)
    {
        var A = Util.LeToBig(A_le);
        if ((A % _N) == 0) return null;

        var u = Util.LeToBig(Util.Sha1(Util.Concat(A_le, B_le)));
        // S = (A * v^u)^b mod N
        var Av = (A * BigInteger.ModPow(_v, u, _N)) % _N;
        var S = BigInteger.ModPow(Av, _b, _N);
        var sLe = Util.BigToLe(S, 32);
        var K = Util.InterleaveHash(sLe);

        var hN = Util.Sha1(N_le);
        var hG = Util.Sha1(new[] { g });
        var hNg = new byte[20];
        for (var i = 0; i < 20; i++) hNg[i] = (byte)(hN[i] ^ hG[i]);
        var hAcc = Util.Sha1(Encoding.UTF8.GetBytes(_account));
        var m1Calc = Util.Sha1(Util.Concat(hNg, hAcc, Salt, A_le, B_le, K));

        if (!CryptographicOperations.FixedTimeEquals(m1Calc, clientM1))
            return null;

        var m2 = Util.Sha1(Util.Concat(A_le, m1Calc, K));
        return (K, m2);
    }
}

internal static class Util
{
    public static byte[] Sha1(byte[] data) => SHA1.HashData(data);

    public static BigInteger LeToBig(byte[] le)
    {
        var buf = new byte[le.Length + 1];
        Array.Copy(le, buf, le.Length);
        return new BigInteger(buf);
    }

    public static byte[] BigToLe(BigInteger value, int size)
    {
        var b = value.ToByteArray();
        var r = new byte[size];
        Array.Copy(b, r, Math.Min(b.Length, size));
        return r;
    }

    public static byte[] InterleaveHash(byte[] s)
    {
        var t = s;
        while (t.Length > 0 && t[0] == 0) t = t.Skip(2).ToArray();

        var even = new byte[t.Length / 2];
        var odd = new byte[t.Length / 2];
        for (var i = 0; i < t.Length / 2; i++)
        {
            even[i] = t[i * 2];
            odd[i] = t[i * 2 + 1];
        }
        var hE = Sha1(even);
        var hO = Sha1(odd);
        var k = new byte[40];
        for (var i = 0; i < 20; i++)
        {
            k[i * 2] = hE[i];
            k[i * 2 + 1] = hO[i];
        }
        return k;
    }

    public static byte[] Concat(params byte[][] parts)
    {
        var len = parts.Sum(p => p.Length);
        var r = new byte[len];
        var off = 0;
        foreach (var p in parts) { Array.Copy(p, 0, r, off, p.Length); off += p.Length; }
        return r;
    }
}
