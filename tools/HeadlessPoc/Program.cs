// Headless WoW 3.3.5a POC.
// Делает: logon → SRP6 → realmlist → world auth → CMSG_CHAR_ENUM → печать персонажей.
// НЕ делает: вход в мир, движение, кастинг.
//
// Запуск: dotnet run -- <account> <password> [logon_host=127.0.0.1] [logon_port=3724] [realm_id=auto]

using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace WowBot.HeadlessPoc;

internal static class Program
{
    private const ushort ClientBuild = 12340; // 3.3.5a

    static async Task<int> Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "nav-test") return NavTest.Run();
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: HeadlessPoc <account> <password> [logon_host] [logon_port] [realm_id]");
            Console.WriteLine("       HeadlessPoc nav-test  (проверить связь с AmeisenNavServer)");
            return 1;
        }

        var account = args[0].ToUpperInvariant();
        var password = args[1].ToUpperInvariant();
        var logonHost = args.Length > 2 ? args[2] : "127.0.0.1";
        var logonPort = args.Length > 3 ? int.Parse(args[3]) : 3724;
        byte? wantedRealmId = args.Length > 4 ? byte.Parse(args[4]) : null;

        Console.WriteLine($"[POC] Connecting to {logonHost}:{logonPort} as '{account}'");

        try
        {
            // ---- Stage 1: logon ----
            byte[] sessionKey;
            List<Realm> realms;
            using (var tcp = new TcpClient())
            {
                await tcp.ConnectAsync(logonHost, logonPort);
                var stream = tcp.GetStream();

                var challenge = await SendLogonChallenge(stream, account);
                if (challenge == null) return 2;

                var srp = Srp6Client.ComputeProof(account, password, challenge);
                Console.WriteLine($"[POC] SRP6 computed. A={Hex(srp.A_le, 8)}.. M1={Hex(srp.M1, 8)}..");

                if (!await SendLogonProof(stream, srp)) return 3;
                sessionKey = srp.K;

                realms = await ReadRealmList(stream);
                Console.WriteLine($"[POC] <- {realms.Count} realm(s) total");
            }

            // ---- Stage 2: pick realm ----
            Realm? target;
            if (wantedRealmId.HasValue)
            {
                target = realms.FirstOrDefault(r => r.Id == wantedRealmId.Value);
                if (target == null) { Console.WriteLine($"[POC] !! realm id={wantedRealmId} not found"); return 4; }
            }
            else
            {
                target = realms.OrderByDescending(r => r.NumChars).FirstOrDefault(r => r.NumChars > 0);
                if (target == null) { Console.WriteLine("[POC] !! no realm with characters found"); return 4; }
            }
            Console.WriteLine($"[POC] picked realm #{target.Id} '{target.Name}' (chars={target.NumChars}) @ {target.Address}");

            // ---- Stage 3: world server ----
            var (worldHost, worldPort) = ParseAddress(target.Address);
            using var world = new WorldClient(ClientBuild);
            await world.ConnectAndAuthAsync(worldHost, worldPort, account, target.Id, sessionKey);
            var characters = await world.GetCharactersAsync();

            Console.WriteLine($"\n=== {characters.Count} character(s) on '{target.Name}' ===");
            foreach (var c in characters)
            {
                Console.WriteLine($"  guid=0x{c.Guid:X16}  {c.Name,-15}  lvl {c.Level,2}  {RaceName(c.Race),-10} {ClassName(c.Class),-12}  map={c.Map}");
            }

            if (characters.Count == 0) return 0;

            // ---- Stage 4: enter world ----
            var first = characters[0];
            Console.WriteLine($"\n[POC] entering world as '{first.Name}' (guid 0x{first.Guid:X16})");
            var stats = await world.EnterWorldAsync(first.Guid);
            Console.WriteLine($"  position : map={stats.Map} ({stats.X:F1}, {stats.Y:F1}, {stats.Z:F1})");

            // ---- Stage 5: optional chat send ----
            var sayMsg = Environment.GetEnvironmentVariable("SAY");
            var yellMsg = Environment.GetEnvironmentVariable("YELL");
            var whisperTo = Environment.GetEnvironmentVariable("WHISPER_TO");
            var whisperMsg = Environment.GetEnvironmentVariable("WHISPER_MSG");

            if (!string.IsNullOrEmpty(sayMsg)) await world.SayAsync(sayMsg);
            if (!string.IsNullOrEmpty(yellMsg)) await world.YellAsync(yellMsg);
            if (!string.IsNullOrEmpty(whisperTo) && !string.IsNullOrEmpty(whisperMsg))
                await world.WhisperAsync(whisperTo, whisperMsg);

            if (Environment.GetEnvironmentVariable("FRIENDS") == "1")
                await world.ContactListAsync();
            var whoMin = Environment.GetEnvironmentVariable("WHO_MIN");
            var whoMax = Environment.GetEnvironmentVariable("WHO_MAX");
            if (!string.IsNullOrEmpty(whoMin) && !string.IsNullOrEmpty(whoMax))
                await world.WhoAsync(uint.Parse(whoMin), uint.Parse(whoMax),
                    Environment.GetEnvironmentVariable("WHO_NAME") ?? "");

            // ---- Stage 6: heartbeat ON (нужен для движения) + опц. движение ----
            world.StartHeartbeat();

            // дать серверу осознать что мы залогинились (LOGIN_VERIFY_WORLD должен прийти)
            await Task.Delay(2000);

            if (int.TryParse(Environment.GetEnvironmentVariable("MOVE_FORWARD"), out var mf) && mf > 0)
                await world.MoveForwardAsync(TimeSpan.FromSeconds(mf));

            var moveTo = Environment.GetEnvironmentVariable("MOVE_TO");
            if (!string.IsNullOrEmpty(moveTo))
            {
                var parts = moveTo.Split(',');
                if (parts.Length == 2 && float.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var tx)
                                       && float.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var ty))
                    await world.MoveToAsync(tx, ty);
            }

            world.CommandMode = Environment.GetEnvironmentVariable("COMMAND_MODE") == "1";
            var idleSeconds = int.TryParse(Environment.GetEnvironmentVariable("IDLE_SEC"), out var s) ? s : 90;
            Console.WriteLine($"\n[POC] idle {idleSeconds}s +heartbeat{(world.CommandMode ? " +commands" : "")}");
            await world.IdleAsync(TimeSpan.FromSeconds(idleSeconds));
            await world.StopHeartbeatAsync();
            Console.WriteLine($"[POC] idle done, still connected — heartbeat works ✓");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[POC] FAILED: {ex.GetType().Name}: {ex.Message}");
            return 99;
        }
    }

    // ----- CMD_AUTH_LOGON_CHALLENGE (0x00) -----

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

        var packet = ms.ToArray();
        Console.WriteLine($"[POC] -> CMD_AUTH_LOGON_CHALLENGE ({packet.Length} bytes)");
        await stream.WriteAsync(packet);

        var header = await ReadExactly(stream, 3);
        if (header[0] != 0x00) { Console.WriteLine($"[POC] !! bad opcode 0x{header[0]:X2}"); return null; }
        if (header[2] != 0x00)
        {
            Console.WriteLine($"[POC] !! logon challenge rejected: 0x{header[2]:X2} ({DescribeResult(header[2])})");
            return null;
        }

        var B = await ReadExactly(stream, 32);
        var gLenBuf = await ReadExactly(stream, 1);
        var gBuf = await ReadExactly(stream, gLenBuf[0]);
        var nLenBuf = await ReadExactly(stream, 1);
        var N = await ReadExactly(stream, nLenBuf[0]);
        var salt = await ReadExactly(stream, 32);
        await ReadExactly(stream, 16 + 1);
        Console.WriteLine($"[POC] <- challenge OK. B={Hex(B, 8)}.. salt={Hex(salt, 8)}..");
        return new LogonChallenge(B, gBuf[0], N, salt);
    }

    // ----- CMD_AUTH_LOGON_PROOF (0x01) -----

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

        var packet = ms.ToArray();
        await stream.WriteAsync(packet);
        Console.WriteLine($"[POC] -> CMD_AUTH_LOGON_PROOF ({packet.Length} bytes)");

        var head = await ReadExactly(stream, 2);
        if (head[0] != 0x01) { Console.WriteLine($"[POC] !! bad opcode 0x{head[0]:X2}"); return false; }
        if (head[1] != 0x00)
        {
            Console.WriteLine($"[POC] !! logon proof rejected: 0x{head[1]:X2} ({DescribeResult(head[1])})");
            return false;
        }
        var body = await ReadExactly(stream, 30);
        Console.WriteLine($"[POC] <- proof OK. M2={Hex(body.AsSpan(0, 20).ToArray(), 8)}..");
        return true;
    }

    // ----- CMD_REALM_LIST (0x10) -----

    private static async Task<List<Realm>> ReadRealmList(NetworkStream stream)
    {
        await stream.WriteAsync(new byte[] { 0x10, 0, 0, 0, 0 });
        Console.WriteLine($"[POC] -> CMD_REALM_LIST");

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
            var type = br.ReadByte();
            var locked = br.ReadByte();
            var flags = br.ReadByte();
            var name = ReadCString(br);
            var addr = ReadCString(br);
            var pop = br.ReadSingle();
            var chars = br.ReadByte();
            var tz = br.ReadByte();
            var id = br.ReadByte();
            result.Add(new Realm(id, name, addr, chars));
        }
        return result;
    }

    // ----- helpers -----

    private static (string host, int port) ParseAddress(string addr)
    {
        var idx = addr.LastIndexOf(':');
        return (addr[..idx], int.Parse(addr[(idx + 1)..]));
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

    private static string Hex(byte[] data, int max) =>
        Convert.ToHexString(data.AsSpan(0, Math.Min(max, data.Length)));

    private static string DescribeResult(byte r) => r switch
    {
        0x00 => "SUCCESS",
        0x04 => "WOW_FAIL_UNKNOWN_ACCOUNT",
        0x05 => "WOW_FAIL_INCORRECT_PASSWORD",
        0x06 => "WOW_FAIL_BANNED",
        0x09 => "WOW_FAIL_VERSION_INVALID",
        0x0B => "WOW_FAIL_VERSION_UPDATE",
        _ => "unknown"
    };

    private static string RaceName(byte r) => r switch
    {
        1 => "Human", 2 => "Orc", 3 => "Dwarf", 4 => "NightElf",
        5 => "Undead", 6 => "Tauren", 7 => "Gnome", 8 => "Troll",
        10 => "BloodElf", 11 => "Draenei", _ => $"r{r}"
    };

    private static string ClassName(byte c) => c switch
    {
        1 => "Warrior", 2 => "Paladin", 3 => "Hunter", 4 => "Rogue",
        5 => "Priest", 6 => "DeathKnight", 7 => "Shaman", 8 => "Mage",
        9 => "Warlock", 11 => "Druid", _ => $"c{c}"
    };
}

internal sealed record LogonChallenge(byte[] B_le, byte g, byte[] N_le, byte[] Salt);
internal sealed record SrpResult(byte[] A_le, byte[] M1, byte[] K);
internal sealed record Realm(byte Id, string Name, string Address, byte NumChars);

internal static class Srp6Client
{
    public static SrpResult ComputeProof(string account, string password, LogonChallenge ch)
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
