// SRP6 logon + realmlist для WoW 3.3.5a.
// Был внутри Program.cs как private helpers — выделено в public class чтобы можно было использовать
// из других проектов (WowBot.Headless и т.п.) не дублируя SRP6 логику.

using System.Net.Sockets;
using System.Text;
using System.Numerics;
using System.Security.Cryptography;

namespace WowBot.HeadlessPoc;

public sealed record Realm(byte Id, string Name, string Address, byte NumChars);
public sealed record LogonChallenge(byte[] B_le, byte g, byte[] N_le, byte[] Salt);
public sealed record SrpResult(byte[] A_le, byte[] M1, byte[] K);

/// <summary>Результат логина: ключ сессии (K) и список доступных realm'ов.</summary>
public sealed record LogonResult(byte[] SessionKey, IReadOnlyList<Realm> Realms);

/// <summary>SRP6 + realmlist клиент. Подключается к logon серверу, авторизуется, возвращает realms.</summary>
public static class LogonClient
{
    private const ushort ClientBuild = 12340; // 3.3.5a

    /// <summary>
    /// Полный logon flow: connect → CMD_AUTH_LOGON_CHALLENGE → CMD_AUTH_LOGON_PROOF → CMD_REALM_LIST.
    /// Возвращает sessionKey (K из SRP6) и realm list.
    /// </summary>
    public static async Task<LogonResult> LoginAsync(string host, int port, string account, string password)
    {
        account = account.ToUpperInvariant();
        password = password.ToUpperInvariant();

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port);
        var stream = tcp.GetStream();

        var challenge = await SendLogonChallenge(stream, account)
            ?? throw new Exception("logon challenge rejected");

        var srp = ComputeProof(account, password, challenge);
        if (!await SendLogonProof(stream, srp))
            throw new Exception("logon proof rejected");

        var realms = await ReadRealmList(stream);
        return new LogonResult(srp.K, realms);
    }

    public static (string host, int port) ParseAddress(string addr)
    {
        var idx = addr.LastIndexOf(':');
        return (addr[..idx], int.Parse(addr[(idx + 1)..]));
    }

    // ----- internals -----

    private static async Task<LogonChallenge?> SendLogonChallenge(NetworkStream stream, string account)
    {
        var accountBytes = Encoding.UTF8.GetBytes(account);
        var size = (ushort)(30 + accountBytes.Length);

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)0x00);
        w.Write((byte)0x08);
        w.Write(size);
        w.Write(Encoding.ASCII.GetBytes("WoW\0"));
        w.Write((byte)3); w.Write((byte)3); w.Write((byte)5);
        w.Write(ClientBuild);
        w.Write(Encoding.ASCII.GetBytes("68x\0"));
        w.Write(Encoding.ASCII.GetBytes("niW\0"));
        w.Write(Encoding.ASCII.GetBytes("BGne"));
        w.Write((uint)0);
        w.Write((uint)0x0100007F);
        w.Write((byte)accountBytes.Length);
        w.Write(accountBytes);

        await stream.WriteAsync(ms.ToArray());

        var header = await ReadExactly(stream, 3);
        if (header[0] != 0x00) return null;
        if (header[2] != 0x00) return null;

        var B = await ReadExactly(stream, 32);
        var gLenBuf = await ReadExactly(stream, 1);
        var gBuf = await ReadExactly(stream, gLenBuf[0]);
        var nLenBuf = await ReadExactly(stream, 1);
        var N = await ReadExactly(stream, nLenBuf[0]);
        var salt = await ReadExactly(stream, 32);
        await ReadExactly(stream, 16 + 1);
        return new LogonChallenge(B, gBuf[0], N, salt);
    }

    private static async Task<bool> SendLogonProof(NetworkStream stream, SrpResult srp)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)0x01);
        w.Write(srp.A_le);
        w.Write(srp.M1);
        w.Write(new byte[20]);
        w.Write((byte)0);
        w.Write((byte)0);

        await stream.WriteAsync(ms.ToArray());

        var head = await ReadExactly(stream, 2);
        if (head[0] != 0x01) return false;
        if (head[1] != 0x00) return false;
        await ReadExactly(stream, 30);
        return true;
    }

    private static async Task<List<Realm>> ReadRealmList(NetworkStream stream)
    {
        await stream.WriteAsync(new byte[] { 0x10, 0, 0, 0, 0 });

        var header = await ReadExactly(stream, 3);
        if (header[0] != 0x10) throw new Exception($"bad realmlist opcode 0x{header[0]:X2}");
        var size = BitConverter.ToUInt16(header, 1);
        var body = await ReadExactly(stream, size);

        var br = new BinaryReader(new MemoryStream(body));
        br.ReadUInt32();
        var n = br.ReadUInt16();
        var result = new List<Realm>();
        for (var i = 0; i < n; i++)
        {
            br.ReadByte(); br.ReadByte(); br.ReadByte(); // type, locked, flags
            var name = ReadCString(br);
            var addr = ReadCString(br);
            br.ReadSingle();
            var chars = br.ReadByte();
            br.ReadByte(); // tz
            var id = br.ReadByte();
            result.Add(new Realm(id, name, addr, chars));
        }
        return result;
    }

    private static async Task<byte[]> ReadExactly(NetworkStream s, int n)
    {
        var buf = new byte[n];
        var off = 0;
        while (off < n)
        {
            var r = await s.ReadAsync(buf.AsMemory(off, n - off));
            if (r == 0) throw new EndOfStreamException("server closed connection");
            off += r;
        }
        return buf;
    }

    private static string ReadCString(BinaryReader br)
    {
        var bytes = new List<byte>();
        byte b;
        while ((b = br.ReadByte()) != 0) bytes.Add(b);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    // ----- SRP6 -----

    private static SrpResult ComputeProof(string account, string password, LogonChallenge ch)
    {
        var N = ToBig(ch.N_le);
        var g = new BigInteger(ch.g);
        var B = ToBig(ch.B_le);
        var k = new BigInteger(3);

        var idHash = Sha1(Encoding.UTF8.GetBytes($"{account}:{password}"));
        var x = ToBig(Sha1(Concat(ch.Salt, idHash)));

        var aBytes = new byte[19];
        RandomNumberGenerator.Fill(aBytes);
        var a = ToBig(aBytes);

        var A = BigInteger.ModPow(g, a, N);
        var aLe = ToBytesLe(A, 32);

        var u = ToBig(Sha1(Concat(aLe, ch.B_le)));

        var gx = BigInteger.ModPow(g, x, N);
        var sub = (B - k * gx) % N;
        if (sub < 0) sub += N;
        var S = BigInteger.ModPow(sub, a + u * x, N);
        var sLe = ToBytesLe(S, 32);

        var K = InterleaveHash(sLe);

        var hN = Sha1(ch.N_le);
        var hG = Sha1(new[] { ch.g });
        var hNg = new byte[20];
        for (var i = 0; i < 20; i++) hNg[i] = (byte)(hN[i] ^ hG[i]);
        var hAcc = Sha1(Encoding.UTF8.GetBytes(account));

        var m1 = Sha1(Concat(hNg, hAcc, ch.Salt, aLe, ch.B_le, K));
        return new SrpResult(aLe, m1, K);
    }

    private static BigInteger ToBig(byte[] le)
    {
        var buf = new byte[le.Length + 1];
        Array.Copy(le, buf, le.Length);
        return new BigInteger(buf);
    }

    private static byte[] ToBytesLe(BigInteger value, int size)
    {
        var b = value.ToByteArray();
        var r = new byte[size];
        Array.Copy(b, r, Math.Min(b.Length, size));
        return r;
    }

    private static byte[] InterleaveHash(byte[] s)
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

    private static byte[] Sha1(byte[] data) => SHA1.HashData(data);

    private static byte[] Concat(params byte[][] parts)
    {
        var len = parts.Sum(p => p.Length);
        var r = new byte[len];
        var off = 0;
        foreach (var p in parts) { Array.Copy(p, 0, r, off, p.Length); off += p.Length; }
        return r;
    }
}
