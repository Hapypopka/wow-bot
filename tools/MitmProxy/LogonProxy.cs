// Logon-прокси: TCP listener на 3724.
// Выступает фейк-сервером для реального WoW клиента — проводит SRP6 с известным паролем,
// возвращает один realm указывающий на наш world-прокси.
//
// Состояние: получает Session от Program, заполняет K_Client + Realms.
//
// V1 — без второй леги к wowcircle. Просто валидация что наш SRP6 работает.

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WowBot.MitmProxy;

internal sealed class LogonProxy
{
    public const byte CMD_AUTH_LOGON_CHALLENGE = 0x00;
    public const byte CMD_AUTH_LOGON_PROOF     = 0x01;
    public const byte CMD_REALM_LIST           = 0x10;

    private readonly int _port;
    private readonly string _knownAccount;
    private readonly string _knownPassword;
    private readonly string _fakeRealmName;
    private readonly string _fakeRealmAddress;
    private readonly string _realServerHost;
    private readonly int _realServerPort;
    private readonly Action<Session> _onSession;

    public LogonProxy(int port, string account, string password,
                      string fakeRealmAddress, string fakeRealmName,
                      string realServerHost, int realServerPort,
                      Action<Session> onSession)
    {
        _port = port;
        _knownAccount = account.ToUpperInvariant();
        _knownPassword = password.ToUpperInvariant();
        _fakeRealmAddress = fakeRealmAddress;
        _fakeRealmName = fakeRealmName;
        _realServerHost = realServerHost;
        _realServerPort = realServerPort;
        _onSession = onSession;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        Log($"listening on 0.0.0.0:{_port}");

        while (!ct.IsCancellationRequested)
        {
            var tcp = await listener.AcceptTcpClientAsync(ct);
            Log($"client connected: {tcp.Client.RemoteEndPoint}");
            _ = Task.Run(() => HandleClientAsync(tcp, ct));
        }
    }

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
    {
        try
        {
            using var _ = tcp;
            var stream = tcp.GetStream();

            // Stage 1: получить CMD_AUTH_LOGON_CHALLENGE от клиента
            var account = await ReadLogonChallenge(stream);
            if (account == null) { Log("bad challenge — disconnect"); return; }
            Log($"client wants account '{account}'");

            if (!string.Equals(account, _knownAccount, StringComparison.OrdinalIgnoreCase))
            {
                Log($"!! account mismatch: expected '{_knownAccount}', got '{account}'");
                await SendChallengeError(stream, 0x04 /* UNKNOWN_ACCOUNT */);
                return;
            }

            // Stage 2: SRP6 server side
            var srp = new Srp6Server(account, _knownPassword);
            await SendChallengeOk(stream, srp);
            Log($"-> challenge sent (B={Hex(srp.B_le, 8)}.. salt={Hex(srp.Salt, 8)}..)");

            // Stage 3: получить CMD_AUTH_LOGON_PROOF, валидировать
            var proofIn = await ReadLogonProof(stream);
            if (proofIn == null) { Log("bad proof — disconnect"); return; }
            var (clientA, clientM1) = proofIn.Value;
            Log($"<- proof received (A={Hex(clientA, 8)}.. M1={Hex(clientM1, 8)}..)");

            var accept = srp.AcceptProof(clientA, clientM1);
            if (accept == null)
            {
                Log("!! M1 mismatch — wrong password?");
                await SendProofError(stream, 0x04 /* INCORRECT_PASSWORD */);
                return;
            }
            var (K_client, M2) = accept.Value;
            Log($"++ SRP6 OK. K_client={Hex(K_client, 8)}..");

            await SendProofOk(stream, M2);

            // Создаём session
            var session = new Session
            {
                Account = account,
                Password = _knownPassword,
                K_Client = K_client,
            };

            // Stage 3.5: вторая лега — логин к реальному wowcircle для получения K_server и realms
            try
            {
                Log($"connecting to real server {_realServerHost}:{_realServerPort} for K_server...");
                var realResult = await RealLogonClient.LogonAsync(_realServerHost, _realServerPort, account, _knownPassword);
                session.K_Server = realResult.K;
                session.Realms = realResult.Realms;
                Log($"++ real logon OK. K_server={Hex(realResult.K, 8)}.. realms={realResult.Realms.Count}");
                foreach (var r in realResult.Realms)
                    Log($"   #{r.Id} '{r.Name}' chars={r.NumChars} @ {r.Address}");
            }
            catch (Exception ex)
            {
                Log($"!! real logon failed: {ex.Message}");
                // даже без K_server даём клиенту дойти до выбора realm — сможем хотя бы посмотреть что он шлёт
            }

            _onSession(session);

            // Stage 4: ждать REALM_LIST, отдать наш фейк
            // Клиент может пинговать REALM_LIST много раз — в цикле
            while (!ct.IsCancellationRequested)
            {
                var head = await ReadExactly(stream, 5);
                if (head == null) break;
                if (head[0] != CMD_REALM_LIST)
                {
                    Log($"!! unexpected opcode after auth: 0x{head[0]:X2} — closing");
                    break;
                }
                Log("<- REALM_LIST request");
                await SendRealmList(stream, _fakeRealmName, _fakeRealmAddress);
                Log($"-> realm list sent (1 realm: '{_fakeRealmName}' @ {_fakeRealmAddress})");
            }
        }
        catch (Exception ex)
        {
            Log($"!! exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- protocol I/O ----

    private static async Task<string?> ReadLogonChallenge(NetworkStream stream)
    {
        var head = await ReadExactly(stream, 4);
        if (head == null || head[0] != CMD_AUTH_LOGON_CHALLENGE) return null;
        var size = BitConverter.ToUInt16(head, 2);
        var body = await ReadExactly(stream, size);
        if (body == null) return null;
        // body: 4 magic + 3 ver + 2 build + 4 platform + 4 os + 4 country + 4 tz + 4 ip + 1 acc_len + acc
        if (body.Length < 30) return null;
        var accLen = body[29];
        if (body.Length < 30 + accLen) return null;
        return Encoding.UTF8.GetString(body, 30, accLen).ToUpperInvariant();
    }

    private static async Task SendChallengeOk(NetworkStream stream, Srp6Server srp)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)CMD_AUTH_LOGON_CHALLENGE);   // opcode
        w.Write((byte)0x00);                       // unknown
        w.Write((byte)0x00);                       // result = SUCCESS
        w.Write(srp.B_le);                         // 32 bytes B
        w.Write((byte)1);                          // g_len
        w.Write((byte)srp.g);                      // g
        w.Write((byte)srp.N_le.Length);            // N_len = 32
        w.Write(srp.N_le);                         // 32 bytes N
        w.Write(srp.Salt);                         // 32 bytes salt
        w.Write(new byte[16]);                     // VersionChallenge
        w.Write((byte)0x00);                       // SecurityFlags
        await stream.WriteAsync(ms.ToArray());
    }

    private static async Task SendChallengeError(NetworkStream stream, byte errorCode)
    {
        var packet = new byte[] { CMD_AUTH_LOGON_CHALLENGE, 0x00, errorCode };
        await stream.WriteAsync(packet);
    }

    private static async Task<(byte[] A, byte[] M1)?> ReadLogonProof(NetworkStream stream)
    {
        // 1 opcode + 32 A + 20 M1 + 20 crc + 1 numKeys + 1 secFlags
        var pkt = await ReadExactly(stream, 1 + 32 + 20 + 20 + 1 + 1);
        if (pkt == null || pkt[0] != CMD_AUTH_LOGON_PROOF) return null;
        var A = pkt.AsSpan(1, 32).ToArray();
        var M1 = pkt.AsSpan(33, 20).ToArray();
        return (A, M1);
    }

    private static async Task SendProofOk(NetworkStream stream, byte[] M2)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)CMD_AUTH_LOGON_PROOF);   // opcode
        w.Write((byte)0x00);                   // result = SUCCESS
        w.Write(M2);                           // 20 bytes M2
        w.Write((uint)0x00800000);             // AccountFlags
        w.Write((uint)0);                      // SurveyId
        w.Write((ushort)0);                    // LoginFlags
        await stream.WriteAsync(ms.ToArray());
    }

    private static async Task SendProofError(NetworkStream stream, byte errorCode)
    {
        var pkt = new byte[] { CMD_AUTH_LOGON_PROOF, errorCode, 0x03, 0x00 };
        await stream.WriteAsync(pkt);
    }

    private static async Task SendRealmList(NetworkStream stream, string name, string address)
    {
        using var bodyMs = new MemoryStream();
        var bw = new BinaryWriter(bodyMs);
        bw.Write((uint)0);                       // padding
        bw.Write((ushort)1);                     // num realms
        bw.Write((byte)0);                       // type (PvE)
        bw.Write((byte)0);                       // locked
        bw.Write((byte)0);                       // flags (NONE)
        bw.Write(Encoding.UTF8.GetBytes(name)); bw.Write((byte)0);
        bw.Write(Encoding.UTF8.GetBytes(address)); bw.Write((byte)0);
        bw.Write(0.5f);                          // population
        bw.Write((byte)1);                       // numChars
        bw.Write((byte)1);                       // timezone
        bw.Write((byte)1);                       // id
        bw.Write((byte)0x10);                    // unk1
        bw.Write((byte)0x00);                    // unk2

        var body = bodyMs.ToArray();
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)CMD_REALM_LIST);           // opcode
        w.Write((ushort)body.Length);            // size
        w.Write(body);
        await stream.WriteAsync(ms.ToArray());
    }

    // ---- helpers ----

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

    private static void Log(string msg) => Console.WriteLine($"[logon] {msg}");
}
