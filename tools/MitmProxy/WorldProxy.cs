// World-прокси: TCP listener на 8085.
// Auth phase (plaintext): SMSG_AUTH_CHALLENGE rewrite, CMSG_AUTH_SESSION re-digest.
// Encrypted phase: делегирует в WorldBridge — он пампит и парсит.

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace WowBot.MitmProxy;

internal sealed class WorldProxy
{
    private const ushort SMSG_AUTH_CHALLENGE = 0x01EC;
    private const uint   CMSG_AUTH_SESSION   = 0x01ED;

    public static bool LogVerbose = false;

    /// <summary>Текущий активный мост — если есть. Используется для inject из stdin.</summary>
    public WorldBridge? ActiveBridge { get; private set; }

    private readonly int _port;
    private readonly Func<Session?> _sessionGetter;

    public WorldProxy(int port, Func<Session?> sessionGetter)
    {
        _port = port;
        _sessionGetter = sessionGetter;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        Log($"listening on 0.0.0.0:{_port}");

        while (!ct.IsCancellationRequested)
        {
            var conn = await listener.AcceptTcpClientAsync(ct);
            Log($"client connected: {conn.Client.RemoteEndPoint}");
            _ = Task.Run(() => HandleAsync(conn, ct));
        }
    }

    private async Task HandleAsync(TcpClient clientTcp, CancellationToken ct)
    {
        TcpClient? serverTcp = null;
        try
        {
            using var _ = clientTcp;

            var session = _sessionGetter();
            if (session?.K_Client == null || session.K_Server == null || session.PickedRealm == null)
            {
                Log("!! no session/realm — closing");
                return;
            }

            var (host, port) = ParseAddr(session.PickedRealm.Address);
            Log($"connecting to real world {host}:{port}...");
            serverTcp = new TcpClient();
            await serverTcp.ConnectAsync(host, port);

            var cs = clientTcp.GetStream();
            var ss = serverTcp.GetStream();

            // Auth phase — без шифрования
            if (!await DoAuthHandshakeAsync(session, cs, ss)) return;

            // Encrypted phase — делегируем bridge'у
            var bridge = new WorldBridge(cs, ss, session.K_Client, session.K_Server, Log, LogVerbose);
            ActiveBridge = bridge;
            try
            {
                await bridge.RunAsync(ct);
            }
            finally
            {
                ActiveBridge = null;
            }
            Log("bridge ended");
        }
        catch (Exception ex)
        {
            Log($"!! exception: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            serverTcp?.Dispose();
        }
    }

    private async Task<bool> DoAuthHandshakeAsync(Session session, NetworkStream cs, NetworkStream ss)
    {
        // 1. Принимаем SMSG_AUTH_CHALLENGE от сервера (4-байтовый SMSG header + body)
        var hdrSrv = await ReadExactly(ss, 4);
        if (hdrSrv == null) { Log("!! server closed before challenge"); return false; }
        var sz = (ushort)((hdrSrv[0] << 8) | hdrSrv[1]);
        var op = (ushort)(hdrSrv[2] | (hdrSrv[3] << 8));
        if (op != SMSG_AUTH_CHALLENGE)
        {
            Log($"!! expected SMSG_AUTH_CHALLENGE, got 0x{op:X4} sz={sz}");
            return false;
        }
        var body = await ReadExactly(ss, sz - 2);
        if (body == null) { Log("!! short body"); return false; }
        var realServerSeed = BitConverter.ToUInt32(body, 4);
        Log($"server SMSG_AUTH_CHALLENGE: realServerSeed=0x{realServerSeed:X8}");

        // 2. Шлём клиенту SMSG_AUTH_CHALLENGE с НАШИМ seed
        var ourServerSeed = (uint)RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);
        var bodyToClient = (byte[])body.Clone();
        BitConverter.GetBytes(ourServerSeed).CopyTo(bodyToClient, 4);
        await cs.WriteAsync(hdrSrv);
        await cs.WriteAsync(bodyToClient);
        Log($"sent SMSG_AUTH_CHALLENGE to client: ourServerSeed=0x{ourServerSeed:X8}");

        // 3. Принимаем CMSG_AUTH_SESSION (6-байтовый header + body)
        var hdrCli = await ReadExactly(cs, 6);
        if (hdrCli == null) { Log("!! client closed before auth-session"); return false; }
        var csz = (ushort)((hdrCli[0] << 8) | hdrCli[1]);
        var cop = (uint)(hdrCli[2] | (hdrCli[3] << 8) | (hdrCli[4] << 16) | (hdrCli[5] << 24));
        if (cop != CMSG_AUTH_SESSION)
        {
            Log($"!! expected CMSG_AUTH_SESSION, got 0x{cop:X8} sz={csz}");
            return false;
        }
        var asBody = await ReadExactly(cs, csz - 4);
        if (asBody == null) { Log("!! short auth-session"); return false; }

        // Парсим: build(4) + loginServerID(4) + account(cstring) + loginServerType(4) + clientSeed(4)
        //         + regionID(4) + bgID(4) + realmID(4) + DOS(8) + digest(20) + addonInfo(...)
        var off = 0;
        var build = BitConverter.ToUInt32(asBody, off); off += 4;
        off += 4; // loginServerID
        var accStart = off;
        while (off < asBody.Length && asBody[off] != 0) off++;
        var account = Encoding.UTF8.GetString(asBody, accStart, off - accStart);
        off++;
        off += 4; // loginServerType
        var clientSeed = BitConverter.ToUInt32(asBody, off); off += 4;
        off += 4 + 4 + 4 + 8; // regionID + bgID + realmID + DOS
        var digestPos = off;
        var clientDigest = asBody.AsSpan(off, 20).ToArray();

        Log($"client CMSG_AUTH_SESSION: account='{account}' build={build} clientSeed=0x{clientSeed:X8}");
        Log($"  client digest = {Hex(clientDigest, 8)}..");

        var expected = ComputeAuthDigest(account, clientSeed, ourServerSeed, session.K_Client!);
        if (!CryptographicOperations.FixedTimeEquals(expected, clientDigest))
        {
            Log("!! client digest MISMATCH (K_client wrong?)");
            return false;
        }
        Log("client digest OK ✓");

        // Перешиваем digest и форвардим серверу
        var newDigest = ComputeAuthDigest(account, clientSeed, realServerSeed, session.K_Server!);
        Array.Copy(newDigest, 0, asBody, digestPos, 20);
        await ss.WriteAsync(hdrCli);
        await ss.WriteAsync(asBody);
        Log($"forwarded CMSG_AUTH_SESSION with re-digest = {Hex(newDigest, 8)}..");
        return true;
    }

    // ---- helpers ----

    private static byte[] ComputeAuthDigest(string account, uint clientSeed, uint serverSeed, byte[] K)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.UTF8.GetBytes(account));
        w.Write((uint)0);
        w.Write(clientSeed);
        w.Write(serverSeed);
        w.Write(K);
        return SHA1.HashData(ms.ToArray());
    }

    private static (string host, int port) ParseAddr(string addr)
    {
        var idx = addr.LastIndexOf(':');
        return (addr[..idx], int.Parse(addr[(idx + 1)..]));
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

    private static string Hex(byte[] data, int max) =>
        Convert.ToHexString(data.AsSpan(0, Math.Min(max, data.Length)));

    private static void Log(string msg) => Console.WriteLine($"[world] {msg}");
}
