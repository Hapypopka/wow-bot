// SRP6 client side к реальному wowcircle.com:3724.
// Используется для получения K_server (ключ для общения с реальным world-сервером)
// и для парсинга реального realm list.
//
// Логика взята из HeadlessPoc/Program.cs.

using System.Net.Sockets;
using System.Text;

namespace WowBot.MitmProxy;

internal static class RealLogonClient
{
    private const ushort ClientBuild = 12340;

    public sealed class Result
    {
        public required byte[] K { get; init; }
        public required List<RealmInfo> Realms { get; init; }
    }

    public static async Task<Result> LogonAsync(string host, int port, string account, string password)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port);
        var stream = tcp.GetStream();

        var ch = await SendChallengeAsync(stream, account);
        if (ch == null) throw new Exception("real logon: bad challenge response");

        var srp = Srp6Client.ComputeProof(account.ToUpperInvariant(), password.ToUpperInvariant(), ch);

        if (!await SendProofAsync(stream, srp))
            throw new Exception("real logon: proof rejected (wrong password?)");

        var realms = await ReadRealmListAsync(stream);
        return new Result { K = srp.K, Realms = realms };
    }

    private static async Task<LogonChallenge?> SendChallengeAsync(NetworkStream stream, string account)
    {
        account = account.ToUpperInvariant();
        var accountBytes = Encoding.UTF8.GetBytes(account);
        var size = (ushort)(30 + accountBytes.Length);

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)0x00);                                  // CMD_AUTH_LOGON_CHALLENGE
        w.Write((byte)0x08);                                  // protocol
        w.Write(size);                                        // size of remainder
        w.Write(Encoding.ASCII.GetBytes("WoW\0"));
        w.Write((byte)3); w.Write((byte)3); w.Write((byte)5); // 3.3.5
        w.Write(ClientBuild);                                 // 12340
        w.Write(Encoding.ASCII.GetBytes("68x\0"));            // x86 reversed
        w.Write(Encoding.ASCII.GetBytes("niW\0"));            // Win reversed
        w.Write(Encoding.ASCII.GetBytes("BGne"));             // enGB reversed (нашему серверу пофиг)
        w.Write((uint)0);                                     // tz
        w.Write((uint)0x0100007F);                            // ip 127.0.0.1
        w.Write((byte)accountBytes.Length);
        w.Write(accountBytes);
        await stream.WriteAsync(ms.ToArray());

        var head = await ReadExactly(stream, 3);
        if (head == null || head[0] != 0x00) return null;
        if (head[2] != 0x00) throw new Exception($"real logon challenge result=0x{head[2]:X2}");

        var B = await ReadExactly(stream, 32);
        var gLenBuf = await ReadExactly(stream, 1);
        var gBuf = await ReadExactly(stream, gLenBuf![0]);
        var nLenBuf = await ReadExactly(stream, 1);
        var N = await ReadExactly(stream, nLenBuf![0]);
        var salt = await ReadExactly(stream, 32);
        await ReadExactly(stream, 16 + 1); // VersionChallenge + secFlags
        return new LogonChallenge(B!, gBuf![0], N!, salt!);
    }

    private static async Task<bool> SendProofAsync(NetworkStream stream, SrpResult srp)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)0x01);
        w.Write(srp.A_le);
        w.Write(srp.M1);
        w.Write(new byte[20]);   // crc hash
        w.Write((byte)0);        // numKeys
        w.Write((byte)0);        // secFlags
        await stream.WriteAsync(ms.ToArray());

        var head = await ReadExactly(stream, 2);
        if (head == null || head[0] != 0x01) return false;
        if (head[1] != 0x00) throw new Exception($"real logon proof result=0x{head[1]:X2}");
        await ReadExactly(stream, 30); // M2 + flags
        return true;
    }

    private static async Task<List<RealmInfo>> ReadRealmListAsync(NetworkStream stream)
    {
        await stream.WriteAsync(new byte[] { 0x10, 0, 0, 0, 0 });

        var head = await ReadExactly(stream, 3);
        if (head == null || head[0] != 0x10) throw new Exception("bad realm-list opcode");
        var size = BitConverter.ToUInt16(head, 1);
        var body = await ReadExactly(stream, size);
        if (body == null) throw new Exception("realm-list short read");

        var br = new BinaryReader(new MemoryStream(body));
        br.ReadUInt32(); // padding
        var n = br.ReadUInt16();
        var result = new List<RealmInfo>();
        for (var i = 0; i < n; i++)
        {
            br.ReadByte();        // type
            br.ReadByte();        // locked
            br.ReadByte();        // flags
            var name = ReadCString(br);
            var addr = ReadCString(br);
            br.ReadSingle();      // population
            var chars = br.ReadByte();
            br.ReadByte();        // tz
            var id = br.ReadByte();
            result.Add(new RealmInfo(id, name, addr, chars));
        }
        return result;
    }

    private static async Task<byte[]?> ReadExactly(NetworkStream s, int n)
    {
        var buf = new byte[n];
        var off = 0;
        while (off < n)
        {
            var r = await s.ReadAsync(buf.AsMemory(off, n - off));
            if (r == 0) return null;
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
}
