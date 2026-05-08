// World server client с состоянием (TCP + crypto держим между шагами).
// Поток:
//   1. ConnectAndAuth — TCP, SMSG_AUTH_CHALLENGE, CMSG_AUTH_SESSION, AUTH_RESPONSE
//   2. GetCharacters — CMSG_CHAR_ENUM → SMSG_CHAR_ENUM
//   3. EnterWorld — CMSG_PLAYER_LOGIN → принимаем серию init-пакетов
//      (SMSG_LOGIN_VERIFY_WORLD, SMSG_UPDATE_OBJECT, etc.)
//      отвечаем на SMSG_TIME_SYNC_REQ чтобы не кикнули.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using WowBot.HeadlessPoc.Nav;

namespace WowBot.HeadlessPoc;

internal sealed record CharInfo(
    ulong Guid, string Name, byte Race, byte Class, byte Gender,
    byte Level, uint Zone, uint Map, float X, float Y, float Z);

internal sealed record WorldEntryStats(
    uint Map, float X, float Y, float Z, float Orientation,
    int UpdateObjectPackets, int ObjectsCreated, List<ulong> NearbyGuids);

internal sealed class WorldClient : IDisposable
{
    private const ushort SMSG_AUTH_CHALLENGE     = 0x1EC;
    private const uint   CMSG_AUTH_SESSION       = 0x1ED;
    private const ushort SMSG_AUTH_RESPONSE      = 0x1EE;
    private const uint   CMSG_CHAR_ENUM          = 0x37;
    private const ushort SMSG_CHAR_ENUM          = 0x3B;
    private const uint   CMSG_PLAYER_LOGIN       = 0x3D;
    private const ushort SMSG_LOGIN_VERIFY_WORLD = 0x236;
    private const ushort SMSG_UPDATE_OBJECT      = 0xA9;
    private const ushort SMSG_COMPRESSED_UPDATE_OBJECT = 0x1F6;
    private const ushort SMSG_TIME_SYNC_REQ      = 0x390;
    private const uint   CMSG_TIME_SYNC_RESP     = 0x391;
    private const ushort SMSG_PING               = 0x1DD;
    private const uint   CMSG_KEEP_ALIVE         = 0x40A;
    private const uint   CMSG_GROUP_INVITE       = 0x6E;
    private const ushort SMSG_GROUP_INVITE       = 0x6F;
    private const ushort SMSG_PARTY_COMMAND_RESULT = 0x7F;
    private const uint   MSG_MOVE_HEARTBEAT      = 0xEE;
    private const uint   MSG_MOVE_START_FORWARD  = 0xB5;
    private const uint   MSG_MOVE_START_BACKWARD = 0xB6;
    private const uint   MSG_MOVE_STOP           = 0xB7;
    private const uint   MSG_MOVE_SET_FACING     = 0xDA;
    private const ushort SMSG_MESSAGECHAT        = 0x96;
    private const uint   CMSG_MESSAGECHAT        = 0x95;

    private const uint MOVEMENTFLAG_FORWARD      = 0x00000001;
    private const uint MOVEMENTFLAG_BACKWARD     = 0x00000002;
    private const float DefaultRunSpeed = 7.0f; // 3.3.5 base run speed (yards/sec)

    private const ushort SMSG_FORCE_WALK_SPEED_CHANGE = 0xC2;
    private const ushort SMSG_FORCE_RUN_SPEED_CHANGE  = 0xC4;
    private const ushort SMSG_FORCE_RUN_BACK_SPEED_CHANGE = 0xC6;
    private const ushort SMSG_FORCE_SWIM_SPEED_CHANGE = 0xC8;
    private const uint   CMSG_FORCE_RUN_SPEED_CHANGE_ACK = 0xC5;

    private float _runSpeed = DefaultRunSpeed;
    private const uint   CMSG_WHO                = 0x62;
    private const ushort SMSG_WHO                = 0x63;
    private const uint   CMSG_CONTACT_LIST       = 0x66;
    private const ushort SMSG_CONTACT_LIST       = 0x67;
    private const uint   CMSG_NAME_QUERY         = 0x50;
    private const ushort SMSG_NAME_QUERY_RESPONSE = 0x51;

    private readonly TcpClient _tcp = new();
    private NetworkStream _stream = null!;
    private WowCrypt _crypt = null!;
    private readonly ushort _build;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // состояние перса для heartbeat
    private ulong _charGuid;
    private uint _map;
    private float _x, _y, _z, _ori;

    private CancellationTokenSource? _heartbeatCts;
    private Task? _heartbeatTask;
    private uint _movementFlags = 0;
    private DateTime _lastMoveUpdate = DateTime.UtcNow;

    /// <summary>NavQuery для коррекции Z по навмешу. Опциональный.</summary>
    public NavQuery? Nav { get; set; }

    private byte[]? _lastWardenChallenge;
    public byte[]? LastWardenChallenge => _lastWardenChallenge;
    private WardenCrypt? _warden;
    private byte[]? _sessionKey;

    /// <summary>База pre-computed Warden ответов из vmangos/warden_modules. Опционально.
    /// Если задано — обрабатываем HASH_REQUEST через CR lookup и переключаем RC4 keys.</summary>
    public WardenCrFile? WardenCr { get; set; }

    /// <summary>Pinned (seed → reply) pair захваченная через MITM. Если задано — используется ВНЕ CR lookup,
    /// для серверов с фиксированным seed которого нет в vmangos pool. После HASH_RESULT мы НЕ знаем module keys
    /// (их вычисляет модуль), поэтому RC4 не переключаем — последующие SMSG_WARDEN расшифруются как мусор,
    /// но сервер хотя бы пропустит handshake gate.</summary>
    public (byte[] Seed, byte[] Reply)? WardenPinnedResponse { get; set; }
    private bool _wardenKeysSwitched = false;
    private int _wardenCheatCheckCount = 0;

    /// <summary>Снэпшот мира — все известные сущности рядом.</summary>
    public WorldState World { get; } = new();

    // кэш resolve'нутых имён по GUID
    private readonly Dictionary<ulong, string> _nameCache = new();
    private readonly HashSet<ulong> _pendingNameQueries = new();

    // command mode — реагируем на шёпоты с "!"
    public bool CommandMode { get; set; }
    private readonly Queue<(ulong senderGuid, string command)> _pendingCommands = new();

    public WorldClient(ushort build) { _build = build; }

    public void Dispose()
    {
        _heartbeatCts?.Cancel();
        _tcp.Dispose();
    }

    public async Task ConnectAndAuthAsync(string host, int port, string account, byte realmId, byte[] sessionKey)
    {
        _sessionKey = sessionKey;
        _warden = new WardenCrypt(sessionKey);
        Console.WriteLine($"[WORLD] connecting to {host}:{port}");
        await _tcp.ConnectAsync(host, port);
        _stream = _tcp.GetStream();

        var (chalSize, chalOpcode) = await ReadPlainServerHeader();
        if (chalOpcode != SMSG_AUTH_CHALLENGE)
            throw new Exception($"expected SMSG_AUTH_CHALLENGE, got 0x{chalOpcode:X4}");
        var chalBody = await ReadExactly(chalSize - 2);
        var serverSeed = BinaryPrimitives.ReadUInt32LittleEndian(chalBody.AsSpan(4, 4));
        Console.WriteLine($"[WORLD] <- SMSG_AUTH_CHALLENGE server_seed=0x{serverSeed:X8}");

        var clientSeedBytes = new byte[4];
        RandomNumberGenerator.Fill(clientSeedBytes);
        var clientSeed = BinaryPrimitives.ReadUInt32LittleEndian(clientSeedBytes);

        using var sha = SHA1.Create();
        var accountBytes = Encoding.UTF8.GetBytes(account);
        sha.TransformBlock(accountBytes, 0, accountBytes.Length, null, 0);
        sha.TransformBlock(new byte[4], 0, 4, null, 0);
        sha.TransformBlock(clientSeedBytes, 0, 4, null, 0);
        var serverSeedBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(serverSeedBytes, serverSeed);
        sha.TransformBlock(serverSeedBytes, 0, 4, null, 0);
        sha.TransformFinalBlock(sessionKey, 0, sessionKey.Length);
        var digest = sha.Hash!;

        var addonsCompressed = BuildEmptyAddonBlob();
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((uint)_build);
        w.Write((uint)0);
        w.Write(accountBytes); w.Write((byte)0);
        w.Write((uint)0);
        w.Write(clientSeed);
        w.Write((uint)0);
        w.Write((uint)0);
        w.Write((uint)realmId);
        w.Write((ulong)0);
        w.Write(digest);
        w.Write((uint)addonsCompressed.Length);
        w.Write(addonsCompressed);

        await SendCmsgPlain(CMSG_AUTH_SESSION, ms.ToArray());
        Console.WriteLine($"[WORLD] -> CMSG_AUTH_SESSION");

        _crypt = new WowCrypt(sessionKey);

        for (var i = 0; i < 5; i++)
        {
            var (size, op) = await ReadEncryptedHeader();
            var body = await ReadExactly(size - 2);
            if (op == SMSG_AUTH_RESPONSE)
            {
                var r = body[0];
                Console.WriteLine($"[WORLD] <- AUTH_RESPONSE result=0x{r:X2}");
                if (r != 0x0C && r != 0x0D) throw new Exception($"world auth rejected 0x{r:X2}");
                return;
            }
            if (op == 0x02E6) await HandleWardenData(body);
            else Console.WriteLine($"[WORLD]    skip pre-auth opcode=0x{op:X4} size={size}");
        }
        throw new Exception("no SMSG_AUTH_RESPONSE in 5 packets");
    }

    public async Task<List<CharInfo>> GetCharactersAsync()
    {
        await SendCmsgEncrypted(CMSG_CHAR_ENUM, Array.Empty<byte>());
        Console.WriteLine($"[WORLD] -> CMSG_CHAR_ENUM");

        for (var i = 0; i < 30; i++)
        {
            var (size, op) = await ReadEncryptedHeader();
            var body = await ReadExactly(size - 2);
            await TryHandleWardenAsync(op, body); // КРИТИЧНО: иначе RC4 desync если сервер шлёт warden между AUTH_RESPONSE и CHAR_ENUM
            if (op == SMSG_CHAR_ENUM)
            {
                var chars = ParseCharEnum(body);
                Console.WriteLine($"[WORLD] <- SMSG_CHAR_ENUM ({chars.Count} chars)");
                return chars;
            }
        }
        throw new Exception("no SMSG_CHAR_ENUM in 30 packets");
    }

    public async Task<WorldEntryStats> EnterWorldAsync(ulong charGuid, int maxPackets = 300)
    {
        _charGuid = charGuid;
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, charGuid);
        await SendCmsgEncrypted(CMSG_PLAYER_LOGIN, payload);
        Console.WriteLine($"[WORLD] -> CMSG_PLAYER_LOGIN guid=0x{charGuid:X16}");

        uint map = 0;
        float x = 0, y = 0, z = 0, ori = 0;
        var verifyReceived = false;
        var updatePackets = 0;
        var objectsCreated = 0;
        var nearbyGuids = new List<ulong>();

        // Читаем N пакетов или до LOGIN_VERIFY_WORLD — БЕЗ таймаутов на чтение,
        // иначе orphan read десинкает RC4.
        var debug = Environment.GetEnvironmentVariable("DEBUG_OPCODES") == "1";
        for (var i = 0; i < maxPackets; i++)
        {
            var (size, op) = await ReadEncryptedHeader();
            var body = await ReadExactly(size - 2);
            if (debug) Console.WriteLine($"[ENTER #{i}] op=0x{op:X4} size={size}");
            if (op == 0x02E6) await HandleWardenData(body);

            switch (op)
            {
                case SMSG_LOGIN_VERIFY_WORLD:
                    map = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0, 4));
                    x = BitConverter.ToSingle(body, 4);
                    y = BitConverter.ToSingle(body, 8);
                    z = BitConverter.ToSingle(body, 12);
                    ori = BitConverter.ToSingle(body, 16);
                    verifyReceived = true;
                    _map = map; _x = x; _y = y; _z = z; _ori = ori;
                    Console.WriteLine($"[WORLD] <- LOGIN_VERIFY_WORLD map={map} pos=({x:F1}, {y:F1}, {z:F1}) ori={ori:F2}");
                    if (Nav != null)
                    {
                        var groundZ = Nav.GetHeight((int)_map, _x, _y, _z);
                        if (!float.IsNaN(groundZ))
                        {
                            Console.WriteLine($"[WORLD]    nav: ground Z = {groundZ:F2} (delta {groundZ - _z:F1})");
                            _z = groundZ;
                        }
                    }
                    break;

                case SMSG_UPDATE_OBJECT:
                    updatePackets++;
                    objectsCreated += TryExtractGuids(body, nearbyGuids);
                    break;

                case SMSG_COMPRESSED_UPDATE_OBJECT:
                    updatePackets++;
                    var decompressed = ZlibDecompress(body);
                    objectsCreated += TryExtractGuids(decompressed, nearbyGuids);
                    break;

                case SMSG_TIME_SYNC_REQ:
                {
                    // body: uint32 counter — отвечаем CMSG_TIME_SYNC_RESP (counter, ticks)
                    var counter = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0, 4));
                    var resp = new byte[8];
                    BinaryPrimitives.WriteUInt32LittleEndian(resp.AsSpan(0, 4), counter);
                    BinaryPrimitives.WriteUInt32LittleEndian(resp.AsSpan(4, 4), (uint)Environment.TickCount);
                    await SendCmsgEncrypted(CMSG_TIME_SYNC_RESP, resp);
                    break;
                }

                case SMSG_FORCE_RUN_SPEED_CHANGE:
                    await HandleForceRunSpeedChange(body, DateTime.UtcNow);
                    break;

                default:
                    // ловим пропущенные опкоды чтобы не упустить warden под нестандартным id
                    if (op != SMSG_UPDATE_OBJECT && op != SMSG_COMPRESSED_UPDATE_OBJECT)
                    {
                        var hex = Convert.ToHexString(body.AsSpan(0, Math.Min(body.Length, 32)));
                        Console.WriteLine($"[ENTER #{i}] unhandled op=0x{op:X4} size={size} body[0..32]={hex}");
                    }
                    break;
            }
            // ранний выход после LOGIN_VERIFY_WORLD + хотя бы 1 update пакета
            if (verifyReceived && updatePackets >= 1) break;
        }

        if (!verifyReceived)
            Console.WriteLine($"[WORLD] !! LOGIN_VERIFY_WORLD не пришёл — возможно сервер не пустил в мир");
        return new WorldEntryStats(map, x, y, z, ori, updatePackets, objectsCreated, nearbyGuids);
    }

    public async Task WhoAsync(uint minLevel, uint maxLevel, string namePattern = "", TimeSpan? wait = null)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(minLevel);
        w.Write(maxLevel);
        w.Write(Encoding.UTF8.GetBytes(namePattern)); w.Write((byte)0); // playerName
        w.Write((byte)0);                                                // guildName (empty cstring)
        w.Write(0xFFFFFFFFu);                                            // race mask = any
        w.Write(0xFFFFFFFFu);                                            // class mask = any
        w.Write((uint)0);                                                // zone count
        w.Write((uint)0);                                                // string count
        await SendCmsgEncrypted(CMSG_WHO, ms.ToArray());
        Console.WriteLine($"[/who] -> levels {minLevel}-{maxLevel}{(namePattern != "" ? $" name~'{namePattern}'" : "")}");

        var deadline = DateTime.UtcNow + (wait ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            var (size, op) = await ReadEncryptedHeader();
            var body = await ReadExactly(size - 2);
            await TryHandleWardenAsync(op, body);
            if (op == SMSG_WHO)
            {
                ParseWhoResponse(body);
                return;
            }
            if (op == SMSG_TIME_SYNC_REQ)
            {
                var counter = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0, 4));
                var resp = new byte[8];
                BinaryPrimitives.WriteUInt32LittleEndian(resp.AsSpan(0, 4), counter);
                BinaryPrimitives.WriteUInt32LittleEndian(resp.AsSpan(4, 4), (uint)Environment.TickCount);
                await SendCmsgEncrypted(CMSG_TIME_SYNC_RESP, resp);
            }
        }
        Console.WriteLine($"[/who] !! ответ не пришёл");
    }

    private static void ParseWhoResponse(byte[] body)
    {
        var br = new BinaryReader(new MemoryStream(body));
        var displayCount = br.ReadUInt32();
        var matchCount = br.ReadUInt32();
        Console.WriteLine($"[/who] <- найдено {matchCount} (показываем {displayCount}):");
        for (var i = 0; i < displayCount; i++)
        {
            var name = ReadCString(br);
            var guild = ReadCString(br);
            var level = br.ReadUInt32();
            var cls = br.ReadUInt32();
            var race = br.ReadUInt32();
            var gender = br.ReadByte();
            var zone = br.ReadUInt32();
            var g = guild.Length > 0 ? $" <{guild}>" : "";
            Console.WriteLine($"   {name,-15}{g} lvl{level} cls={cls} race={race} zone={zone}");
        }
    }

    public async Task ContactListAsync(TimeSpan? wait = null)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, 0x07);
        await SendCmsgEncrypted(CMSG_CONTACT_LIST, payload);
        Console.WriteLine($"[/friend] -> CMSG_CONTACT_LIST");

        var deadline = DateTime.UtcNow + (wait ?? TimeSpan.FromSeconds(5));
        List<ContactEntry>? entries = null;
        while (DateTime.UtcNow < deadline && entries == null)
        {
            var (size, op) = await ReadEncryptedHeader();
            var body = await ReadExactly(size - 2);
            await TryHandleWardenAsync(op, body);
            if (op == SMSG_CONTACT_LIST) entries = ParseContactListBody(body);
            else if (op == SMSG_TIME_SYNC_REQ) await RespondTimeSync(body);
            else if (op == SMSG_NAME_QUERY_RESPONSE) HandleNameQueryResponse(body);
            else if (op == SMSG_MESSAGECHAT) HandleChatMessage(body);
        }
        if (entries == null) { Console.WriteLine($"[/friend] !! ответ не пришёл"); return; }

        // Запросили имена для всех неизвестных guid'ов
        foreach (var e in entries) await NameQueryAsync(e.Guid);

        // Соберём ответы — ждём пока в кэше появятся имена либо до таймаута
        var resolveDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < resolveDeadline && entries.Any(e => !_nameCache.ContainsKey(e.Guid)))
        {
            var (size, op) = await ReadEncryptedHeader();
            var body = await ReadExactly(size - 2);
            await TryHandleWardenAsync(op, body);
            if (op == SMSG_NAME_QUERY_RESPONSE) HandleNameQueryResponse(body);
            else if (op == SMSG_TIME_SYNC_REQ) await RespondTimeSync(body);
            else if (op == SMSG_MESSAGECHAT) HandleChatMessage(body);
        }

        Console.WriteLine($"[/friend] <- {entries.Count} контактов:");
        foreach (var e in entries)
        {
            var name = _nameCache.TryGetValue(e.Guid, out var n) ? n : $"0x{e.Guid:X16}";
            var kind = (e.Flags & 1) != 0 ? "friend" : (e.Flags & 2) != 0 ? "ignore" : "mute";
            var status = e.Online ? $"online lvl{e.Level} cls={e.Cls} area={e.Area}" : "offline";
            var noteStr = e.Note.Length > 0 ? $" note='{e.Note}'" : "";
            Console.WriteLine($"   [{kind}] {name,-15} {status}{noteStr}");
        }
    }

    private sealed record ContactEntry(ulong Guid, uint Flags, string Note, bool Online, byte Status, uint Area, uint Level, uint Cls);

    private static List<ContactEntry> ParseContactListBody(byte[] body)
    {
        var br = new BinaryReader(new MemoryStream(body));
        br.ReadUInt32(); // list flags
        var count = br.ReadUInt32();
        var list = new List<ContactEntry>();
        for (var i = 0; i < count; i++)
        {
            var guid = br.ReadUInt64();
            var contactFlags = br.ReadUInt32();
            var note = ReadCString(br);
            byte status = 0; uint area = 0, level = 0, cls = 0; bool online = false;
            if ((contactFlags & 1) != 0)
            {
                status = br.ReadByte();
                if (status > 0)
                {
                    online = true;
                    area = br.ReadUInt32();
                    level = br.ReadUInt32();
                    cls = br.ReadUInt32();
                }
            }
            list.Add(new ContactEntry(guid, contactFlags, note, online, status, area, level, cls));
        }
        return list;
    }

    /// <summary>True если опкод — это движение другого юнита (нам пересылается сервером).</summary>
    private static bool IsMovementOpcode(ushort op) => op switch
    {
        0xB5 => true, // MSG_MOVE_START_FORWARD
        0xB6 => true, // MSG_MOVE_START_BACKWARD
        0xB7 => true, // MSG_MOVE_STOP
        0xB8 => true, // MSG_MOVE_START_STRAFE_LEFT
        0xB9 => true, // MSG_MOVE_START_STRAFE_RIGHT
        0xBA => true, // MSG_MOVE_STOP_STRAFE
        0xBB => true, // MSG_MOVE_JUMP
        0xBC => true, // MSG_MOVE_START_TURN_LEFT
        0xBD => true, // MSG_MOVE_START_TURN_RIGHT
        0xBE => true, // MSG_MOVE_STOP_TURN
        0xBF => true, // MSG_MOVE_START_PITCH_UP
        0xC0 => true, // MSG_MOVE_START_PITCH_DOWN
        0xC1 => true, // MSG_MOVE_STOP_PITCH
        0xDA => true, // MSG_MOVE_SET_FACING
        0xDB => true, // MSG_MOVE_SET_PITCH
        0xEE => true, // MSG_MOVE_HEARTBEAT
        0xEF => true, // MSG_MOVE_FALL_LAND
        _ => false
    };

    // Warden opcodes (первый байт расшифрованного payload)
    private const byte WARDEN_SMSG_MODULE_USE         = 0;
    private const byte WARDEN_SMSG_MODULE_CACHE       = 1;
    private const byte WARDEN_SMSG_CHEAT_CHECKS_REQ   = 2;
    private const byte WARDEN_SMSG_MODULE_INIT        = 3;
    private const byte WARDEN_SMSG_HASH_REQUEST       = 5;

    private const byte WARDEN_CMSG_MODULE_MISSING     = 0;
    private const byte WARDEN_CMSG_MODULE_OK          = 1;
    private const byte WARDEN_CMSG_CHEAT_CHECKS_RES   = 2;
    private const byte WARDEN_CMSG_HASH_RESULT        = 4;
    private const byte WARDEN_CMSG_MODULE_FAILED      = 5;

    /// <summary>Безопасно вызывается из любого read цикла. Сохраняет Warden RC4 sync даже если SMSG_WARDEN_DATA прилетел в неожиданном месте.</summary>
    private async Task TryHandleWardenAsync(ushort op, byte[] body)
    {
        if (op == 0x02E6) await HandleWardenData(body);
    }

    private static bool SeedsEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private async Task HandleWardenData(byte[] body)
    {
        if (_warden == null) return;
        var encHex = Convert.ToHexString(body.AsSpan(0, Math.Min(body.Length, 32)));
        var copy = body.ToArray();
        _warden.Decrypt(copy);
        var serverCmd = copy[0];
        _lastWardenChallenge = copy;
        var decHex = Convert.ToHexString(copy.AsSpan(0, Math.Min(copy.Length, 32)));
        Console.WriteLine($"[WARDEN] <- 0x02E6 size={body.Length} enc={encHex} dec={decHex}");

        // Если CR база не задана — пассивный режим (старое поведение, кик через ~2 минуты)
        if (WardenCr == null)
        {
            Console.WriteLine($"[WARDEN] <- cmd=0x{serverCmd:X2} ({DescribeWardenServerCmd(serverCmd)}) size={copy.Length} [PASSIVE — no CR loaded]");
            return;
        }

        switch (serverCmd)
        {
            case WARDEN_SMSG_MODULE_USE:
                // Сервер шлёт module hash + key + size. Мы притворяемся что модуль уже cached,
                // отвечаем MODULE_OK — сервер пропускает MODULE_DATA фазу и сразу шлёт HASH_REQUEST.
                // Env WARDEN_MODULE_MISSING=1 — диагностический режим: шлём MODULE_MISSING (0x00),
                // тогда сервер начинает стримить MODULE_CACHE. Если стриминг идёт — крипта на CMSG ОК.
                if (Environment.GetEnvironmentVariable("WARDEN_MODULE_MISSING") == "1")
                {
                    Console.WriteLine($"[WARDEN] <- MODULE_USE — отвечаем MODULE_MISSING (диагностика крипты)");
                    await SendWardenAsync(new byte[] { WARDEN_CMSG_MODULE_MISSING });
                    break;
                }
                Console.WriteLine($"[WARDEN] <- MODULE_USE — отвечаем MODULE_OK (cached)");
                await SendWardenAsync(new byte[] { WARDEN_CMSG_MODULE_OK });
                break;

            case WARDEN_SMSG_HASH_REQUEST:
                // Body: 1 byte cmd + 16 bytes seed
                if (copy.Length < 17) { Console.WriteLine("[WARDEN] !! HASH_REQUEST too short"); break; }
                var seed = copy.AsSpan(1, 16).ToArray();
                var seedHex = Convert.ToHexString(seed);

                // Приоритет: pinned response (захваченный через MITM для конкретного seed) > CR lookup
                if (WardenPinnedResponse.HasValue && SeedsEqual(WardenPinnedResponse.Value.Seed, seed))
                {
                    var pinnedReply = WardenPinnedResponse.Value.Reply;
                    Console.WriteLine($"[WARDEN] <- HASH_REQUEST seed={seedHex[..16]}.. — PINNED ответ из MITM-захвата");
                    var resp2 = new byte[21];
                    resp2[0] = WARDEN_CMSG_HASH_RESULT;
                    Array.Copy(pinnedReply, 0, resp2, 1, 20);
                    await SendWardenAsync(resp2);
                    Console.WriteLine($"[WARDEN] HASH_RESULT отправлен. Module keys неизвестны — следующие SMSG будут мусором, но handshake gate пройден");
                    break;
                }

                var entry = WardenCr.LookupBySeed(seed);
                if (entry == null)
                {
                    Console.WriteLine($"[WARDEN] <- HASH_REQUEST seed={seedHex[..16]}.. — НЕ найдено в CR ({WardenCr.Entries.Count} записей). Молчим.");
                    break;
                }
                Console.WriteLine($"[WARDEN] <- HASH_REQUEST seed={seedHex[..16]}.. — найден в CR, шлём pre-computed reply");
                var resp = new byte[21];
                resp[0] = WARDEN_CMSG_HASH_RESULT;
                Array.Copy(entry.Reply, 0, resp, 1, 20);
                await SendWardenAsync(resp);
                // ВАЖНО: переключаем RC4 keys ПОСЛЕ отправки HASH_RESULT,
                // следующие SMSG_WARDEN сервер шлёт уже под module-keys.
                _warden.ReplaceKeys(entry.ClientKey, entry.ServerKey);
                _wardenKeysSwitched = true;
                Console.WriteLine($"[WARDEN] переключили RC4 на module-keys из CR (clientKey={Convert.ToHexString(entry.ClientKey)[..16]}.. serverKey={Convert.ToHexString(entry.ServerKey)[..16]}..)");
                break;

            case WARDEN_SMSG_CHEAT_CHECKS_REQ:
                _wardenCheatCheckCount++;
                Console.WriteLine($"[WARDEN] <- CHEAT_CHECKS_REQUEST #{_wardenCheatCheckCount} ({copy.Length}b) — пока не обрабатываем (тест толерантности)");
                // TODO Phase 4.3: парсить чек-типы и отвечать через port WoWee handlers
                break;

            case WARDEN_SMSG_MODULE_INIT:
                Console.WriteLine($"[WARDEN] <- MODULE_INITIALIZE ({copy.Length}b) — игнорируем");
                break;

            case WARDEN_SMSG_MODULE_CACHE:
                Console.WriteLine($"[WARDEN] <- MODULE_CACHE chunk ({copy.Length}b) — не должны были получить (отказались от MODULE_OK)");
                break;

            default:
                Console.WriteLine($"[WARDEN] <- unknown cmd=0x{serverCmd:X2} size={copy.Length}");
                break;
        }
    }

    /// <summary>Зашифровать body под текущим WardenCrypt и отправить как CMSG_WARDEN_DATA.</summary>
    private async Task SendWardenAsync(byte[] plaintextBody)
    {
        if (_warden == null) return;
        var encBody = (byte[])plaintextBody.Clone();
        _warden.Encrypt(encBody);
        Console.WriteLine($"[WARDEN] -> CMSG (plain={Convert.ToHexString(plaintextBody)} enc={Convert.ToHexString(encBody)})");
        await SendCmsgEncrypted(0x02E7 /* CMSG_WARDEN_DATA */, encBody);
        Console.WriteLine($"[WARDEN] -> sent OK");
    }

    private static string DescribeWardenServerCmd(byte c) => c switch
    {
        0 => "MODULE_USE",
        1 => "MODULE_CACHE",
        2 => "CHEAT_CHECKS_REQUEST",
        3 => "MODULE_INITIALIZE",
        5 => "HASH_REQUEST",
        _ => $"unk{c}"
    };

    private void HandleEntityMovement(byte[] body)
    {
        try
        {
            var br = new BinaryReader(new MemoryStream(body));
            var guid = ReadPackedGuid(br);
            if (guid == _charGuid) return;
            var entity = World.Get(guid);
            if (entity == null) return;

            var movFlags = br.ReadUInt32();
            br.ReadUInt16(); // flags2
            br.ReadUInt32(); // timestamp
            var x = br.ReadSingle();
            var y = br.ReadSingle();
            var z = br.ReadSingle();
            var ori = br.ReadSingle();

            // Санити-проверка — координаты должны быть в разумных пределах
            if (MathF.Abs(x) > 20000 || MathF.Abs(y) > 20000 || MathF.Abs(z) > 20000) return;
            // Если телепортнуло на сотни ярдов — игнорим (вероятно desync)
            var dx = x - entity.X; var dy = y - entity.Y;
            if (entity.X != 0 && (dx*dx + dy*dy) > 10000) return; // > 100y скачок

            entity.X = x; entity.Y = y; entity.Z = z; entity.Orientation = ori;
            entity.LastSeen = DateTime.UtcNow;
        }
        catch { /* ignore desync */ }
    }

    private async Task RespondTimeSync(byte[] body)
    {
        var counter = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0, 4));
        var resp = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(resp.AsSpan(0, 4), counter);
        BinaryPrimitives.WriteUInt32LittleEndian(resp.AsSpan(4, 4), (uint)Environment.TickCount);
        await SendCmsgEncrypted(CMSG_TIME_SYNC_RESP, resp);
    }

    public Task SayAsync(string text)         => SendChatAsync(0x00, null, text);
    public Task YellAsync(string text)        => SendChatAsync(0x05, null, text);
    public Task GuildAsync(string text)       => SendChatAsync(0x03, null, text);
    public Task WhisperAsync(string to, string text) => SendChatAsync(0x06, to, text);

    private async Task SendChatAsync(byte type, string? target, string text)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((uint)type);     // chat type
        w.Write((uint)0x07);     // language: Common
        if (target != null)
        {
            w.Write(Encoding.UTF8.GetBytes(target));
            w.Write((byte)0);
        }
        w.Write(Encoding.UTF8.GetBytes(text));
        w.Write((byte)0);
        await SendCmsgEncrypted(CMSG_MESSAGECHAT, ms.ToArray());
        Console.WriteLine($"[CHAT] -> type={ChatTypeName(type)} {(target != null ? $"to={target} " : "")}msg='{text}'");
    }

    public async Task InvitePlayerAsync(string targetName, TimeSpan wait)
    {
        var nameBytes = Encoding.UTF8.GetBytes(targetName);
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(nameBytes); w.Write((byte)0); // cstring
        w.Write((uint)0);                     // unk in 3.3.5
        await SendCmsgEncrypted(CMSG_GROUP_INVITE, ms.ToArray());
        Console.WriteLine($"[WORLD] -> CMSG_GROUP_INVITE name='{targetName}'");

        var deadline = DateTime.UtcNow + wait;
        while (DateTime.UtcNow < deadline)
        {
            var (size, op) = await ReadEncryptedHeader();
            var body = await ReadExactly(size - 2);
            await TryHandleWardenAsync(op, body);

            Console.WriteLine($"[WORLD]    <- opcode=0x{op:X4} size={size}");
            switch (op)
            {
                case SMSG_PARTY_COMMAND_RESULT:
                {
                    var br = new BinaryReader(new MemoryStream(body));
                    var operation = br.ReadUInt32();
                    var member = ReadCString(br);
                    var result = br.ReadUInt32();
                    Console.WriteLine($"[WORLD] <- PARTY_COMMAND_RESULT op={operation} member='{member}' result={result} ({DescribePartyResult(result)})");
                    return;
                }
                case SMSG_TIME_SYNC_REQ:
                {
                    var counter = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0, 4));
                    var resp = new byte[8];
                    BinaryPrimitives.WriteUInt32LittleEndian(resp.AsSpan(0, 4), counter);
                    BinaryPrimitives.WriteUInt32LittleEndian(resp.AsSpan(4, 4), (uint)Environment.TickCount);
                    await SendCmsgEncrypted(CMSG_TIME_SYNC_RESP, resp);
                    break;
                }
            }
        }
        Console.WriteLine($"[WORLD] !! PARTY_COMMAND_RESULT не пришёл за {wait.TotalSeconds}s");
    }

    private async Task ProcessPendingCommands()
    {
        // Перекладываем в локальный список — нельзя dequeue пока имя не зарезолвлено
        var stillPending = new Queue<(ulong, string)>();
        while (_pendingCommands.Count > 0)
        {
            var (guid, cmd) = _pendingCommands.Dequeue();
            if (!_nameCache.TryGetValue(guid, out var senderName))
            {
                stillPending.Enqueue((guid, cmd)); // ждём имя
                continue;
            }
            if (senderName == "?") continue; // имя не нашлось

            await ExecuteCommand(senderName, guid, cmd);
        }
        foreach (var p in stillPending) _pendingCommands.Enqueue(p);
    }

    private async Task ExecuteCommand(string senderName, ulong senderGuid, string cmd)
    {
        Console.WriteLine($"  [CMD] {senderName} <- '{cmd}'");
        var parts = cmd[1..].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        var verb = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1] : "";

        switch (verb)
        {
            case "ping":   await Reply(senderName, "pong"); break;
            case "help":   await Reply(senderName, "!ping !pos !nearby !who !invite !come !followme !stop !help"); break;
            case "pos":    await Reply(senderName, $"map={_map} pos=({_x:F0}, {_y:F0}, {_z:F0})"); break;
            case "who":    await Reply(senderName, await DoWhoQuick(arg)); break;
            case "invite": await Reply(senderName, await DoInvite(arg)); break;
            case "nearby": await Reply(senderName, DoNearby(arg)); break;
            case "come":
            case "ко":
                await DoCome(senderName, senderGuid);
                break;
            case "followme":
            case "follow":
                await DoFollow(senderName, senderGuid);
                break;
            case "stop":
                _followCts?.Cancel();
                await Reply(senderName, "стою");
                break;
            default:
                await Reply(senderName, $"Неизвестная команда '{verb}'. !help");
                break;
        }
    }

    private async Task Reply(string to, string text)
    {
        Console.WriteLine($"  [CMD] -> '{text}'");
        await WhisperAsync(to, text);
    }

    private async Task DoCome(string senderName, ulong senderGuid)
    {
        var target = World.Get(senderGuid);
        if (target == null || (target.X == 0 && target.Y == 0))
        {
            await Reply(senderName, "не вижу тебя");
            return;
        }
        var dx = target.X - _x;
        var dy = target.Y - _y;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist < 5f) { await Reply(senderName, $"уже рядом ({dist:F0}y)"); return; }
        await Reply(senderName, $"иду, {dist:F0}y");
        var stopX = target.X - dx / dist * 3;
        var stopY = target.Y - dy / dist * 3;
        await MoveToAsync(stopX, stopY, tolerance: 2f);
        await Reply(senderName, "пришла");
    }

    private CancellationTokenSource? _followCts;

    private async Task DoFollow(string senderName, ulong senderGuid)
    {
        _followCts?.Cancel();
        _followCts = new CancellationTokenSource();
        var ct = _followCts.Token;
        await Reply(senderName, "следую за тобой (!stop чтобы остановить)");
        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var target = World.Get(senderGuid);
                    if (target != null)
                    {
                        var dx = target.X - _x;
                        var dy = target.Y - _y;
                        var dist = MathF.Sqrt(dx * dx + dy * dy);
                        if (dist > 5f) // догоняем если >5 ярдов
                        {
                            var stopX = target.X - dx / dist * 3;
                            var stopY = target.Y - dy / dist * 3;
                            await MoveToAsync(stopX, stopY, tolerance: 2f);
                        }
                    }
                    try { await Task.Delay(1000, ct); } catch { return; }
                }
            }
            catch (Exception ex) { Console.WriteLine($"[FOLLOW] error: {ex.Message}"); }
        }, ct);
    }

    private string DoNearby(string arg)
    {
        var radius = float.TryParse(arg, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 50f;
        var units = World.NearbyUnits(_x, _y, _z, radius);
        if (units.Count == 0) return $"вокруг {radius:F0}y никого не вижу (всего в worldstate {World.Count})";
        var top = units
            .OrderBy(u => (u.X - _x) * (u.X - _x) + (u.Y - _y) * (u.Y - _y))
            .Take(5)
            .Select(u => {
                var name = u.Name ?? (_nameCache.TryGetValue(u.Guid, out var n) ? n : $"0x{u.Guid:X8}");
                var hp = u.MaxHealth > 0 ? $" {100 * u.Health / u.MaxHealth}%" : "";
                var dist = MathF.Sqrt((u.X-_x)*(u.X-_x) + (u.Y-_y)*(u.Y-_y));
                return $"{name} lvl{u.Level}{hp} {dist:F0}y";
            });
        return string.Join("; ", top);
    }

    private async Task<string> DoWhoQuick(string nameOrLevel)
    {
        // быстро — пошлём /who и парсим первый ответ
        uint min = 1, max = 80;
        var pattern = "";
        if (uint.TryParse(nameOrLevel, out var lv)) { min = lv; max = lv; }
        else pattern = nameOrLevel;

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(min); w.Write(max);
        w.Write(Encoding.UTF8.GetBytes(pattern)); w.Write((byte)0);
        w.Write((byte)0);
        w.Write(0xFFFFFFFFu); w.Write(0xFFFFFFFFu);
        w.Write((uint)0); w.Write((uint)0);
        await SendCmsgEncrypted(CMSG_WHO, ms.ToArray());

        // Ждём SMSG_WHO до 2 сек
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            var (size, op) = await ReadEncryptedHeader();
            var body = await ReadExactly(size - 2);
            await TryHandleWardenAsync(op, body);
            if (op == SMSG_WHO)
            {
                var br = new BinaryReader(new MemoryStream(body));
                var disp = br.ReadUInt32();
                var match = br.ReadUInt32();
                return $"найдено {match} (показано {disp})";
            }
            if (op == SMSG_TIME_SYNC_REQ) await RespondTimeSync(body);
            else if (op == SMSG_NAME_QUERY_RESPONSE) HandleNameQueryResponse(body);
            else if (op == SMSG_MESSAGECHAT) HandleChatMessage(body);
        }
        return "(/who timeout)";
    }

    private async Task<string> DoInvite(string targetName)
    {
        if (string.IsNullOrEmpty(targetName)) return "укажи имя: !invite <name>";
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.UTF8.GetBytes(targetName)); w.Write((byte)0);
        w.Write((uint)0);
        await SendCmsgEncrypted(CMSG_GROUP_INVITE, ms.ToArray());
        return $"инвайт ушёл -> {targetName}";
    }

    public async Task NameQueryAsync(ulong guid)
    {
        if (_nameCache.ContainsKey(guid) || _pendingNameQueries.Contains(guid)) return;
        _pendingNameQueries.Add(guid);
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, guid);
        await SendCmsgEncrypted(CMSG_NAME_QUERY, payload);
    }

    public string ResolveCached(ulong guid) =>
        _nameCache.TryGetValue(guid, out var n) ? n : $"<0x{guid:X16}>";

    private void HandleNameQueryResponse(byte[] body)
    {
        try
        {
            var br = new BinaryReader(new MemoryStream(body));
            var guid = ReadPackedGuid(br);
            var notFound = br.ReadByte(); // 0=ok, 1=not found
            if (notFound != 0)
            {
                _nameCache[guid] = "?";
                _pendingNameQueries.Remove(guid);
                return;
            }
            var name = ReadCString(br);
            _nameCache[guid] = name;
            _pendingNameQueries.Remove(guid);
        }
        catch { }
    }

    private void HandleChatMessage(byte[] body)
    {
        try
        {
            var br = new BinaryReader(new MemoryStream(body));
            var type = br.ReadByte();
            br.ReadUInt32(); // language
            var senderGuid = br.ReadUInt64();
            br.ReadUInt32(); // unk1

            if (senderGuid != 0 && !_nameCache.ContainsKey(senderGuid) && !_pendingNameQueries.Contains(senderGuid))
                _ = NameQueryAsync(senderGuid);

            // Извлечём текст сообщения для command mode (только для WHISPER_FROM = 0x07)
            if (CommandMode && type == 0x07)
            {
                var msg = TryExtractMessageText(body, type);
                if (!string.IsNullOrEmpty(msg) && msg.StartsWith("!"))
                {
                    _pendingCommands.Enqueue((senderGuid, msg));
                    Console.WriteLine($"  [CMD] queued '{msg}' from 0x{senderGuid:X16}");
                }
            }

            br.BaseStream.Position = 0;
            PrintChatMessageImpl(br, body, this);
        }
        catch
        {
            Console.WriteLine($"  [chat] парсер не справился (body {body.Length} байт)");
        }
    }

    private static string? TryExtractMessageText(byte[] body, byte type)
    {
        try
        {
            var br = new BinaryReader(new MemoryStream(body));
            br.ReadByte(); br.ReadUInt32(); br.ReadUInt64(); br.ReadUInt32();
            // для WHISPER (0x06, 0x07) — повтор guid
            if (type is 0x06 or 0x07 or 0x00 or 0x01 or 0x02 or 0x03 or 0x05 or 0x09 or 0x0A)
                br.ReadUInt64();
            else return null;
            var len = br.ReadUInt32();
            if (len == 0 || len > body.Length) return null;
            var bytes = br.ReadBytes((int)len);
            return Encoding.UTF8.GetString(bytes).TrimEnd('\0', '\n', '\r');
        }
        catch { return null; }
    }

    private static void PrintChatMessageImpl(BinaryReader br, byte[] body, WorldClient self)
    {
        try
        {
            var type = br.ReadByte();
            var language = br.ReadUInt32();
            var senderGuid = br.ReadUInt64();
            br.ReadUInt32(); // unk1

            string? channel = null;
            if (type is 0x10 or 0x11 or 0x12 or 0x13 or 0x14 or 0x15) // CHANNEL_*
            {
                channel = ReadCString(br);
                br.ReadUInt32(); // player rank
            }

            // ACHIEVEMENT-типы (0x2D, 0x2E) и MONSTER-типы (0x0B-0x0F) имеют другую структуру.
            // Для базовых SAY/YELL/EMOTE/WHISPER/PARTY/GUILD/RAID/CHANNEL — повторный sender_guid.
            if (type is < 0x0B or 0x16 or 0x17 or > 0x15 and not (>= 0x0B and <= 0x0F))
            {
                if (br.BaseStream.Position + 8 <= br.BaseStream.Length)
                    br.ReadUInt64(); // sender guid повтор
            }
            else if (type is >= 0x0B and <= 0x0F) // MONSTER_*
            {
                var nameLen = br.ReadUInt32();
                if (nameLen > 0 && nameLen < 100)
                    br.ReadBytes((int)nameLen); // sender name
                if (br.BaseStream.Position + 8 <= br.BaseStream.Length)
                    br.ReadUInt64(); // receiver guid
            }

            uint msgLen = 0;
            if (br.BaseStream.Position + 4 <= br.BaseStream.Length)
                msgLen = br.ReadUInt32();
            if (msgLen == 0 || msgLen > body.Length) return;

            var msgBytes = br.ReadBytes((int)Math.Min(msgLen, br.BaseStream.Length - br.BaseStream.Position));
            var message = Encoding.UTF8.GetString(msgBytes).TrimEnd('\0', '\n', '\r');

            var prefix = channel != null ? $"#{channel} " : "";
            var typeName = ChatTypeName(type);
            var senderName = senderGuid != 0
                ? (self._nameCache.TryGetValue(senderGuid, out var n) ? n : $"<0x{senderGuid:X16}>")
                : "система";
            Console.WriteLine($"  [{typeName}] {prefix}{senderName}: {message}");
        }
        catch
        {
            Console.WriteLine($"  [chat] парсер не справился (body {body.Length} байт)");
        }
    }

    private static string ChatTypeName(byte t) => t switch
    {
        0x00 => "SAY",        0x01 => "PARTY",      0x02 => "RAID",       0x03 => "GUILD",
        0x04 => "OFFICER",    0x05 => "YELL",       0x06 => "WHISPER",    0x07 => "WHISPER_FROM",
        0x08 => "WHISPER_OK", 0x09 => "EMOTE",      0x0A => "TEXT_EMOTE",
        0x0B => "MOB_SAY",    0x0C => "MOB_PARTY",  0x0D => "MOB_YELL",
        0x0E => "MOB_WHISPER",0x0F => "MOB_EMOTE",
        0x10 => "CHANNEL",    0x11 => "CHAN_JOIN",  0x12 => "CHAN_LEAVE",
        0x13 => "CHAN_LIST",  0x14 => "CHAN_NOTICE",0x15 => "CHAN_NOTICE_USR",
        0x16 => "AFK",        0x17 => "DND",        0x18 => "IGNORED",
        0x26 => "RAID_LEAD",  0x27 => "RAID_WARN",  0x28 => "BOSS_EMOTE", 0x29 => "BOSS_WHISPER",
        0x2D => "ACHIEV",     0x2E => "GUILD_ACHIEV",
        0x32 => "SYSTEM",
        _ => $"t0x{t:X2}"
    };

    private static string DescribePartyResult(uint r) => r switch
    {
        0 => "OK",
        1 => "BAD_PLAYER_NAME (имя не найдено)",
        2 => "TARGET_NOT_IN_GROUP",
        3 => "TARGET_NOT_IN_INSTANCE",
        4 => "GROUP_FULL",
        5 => "ALREADY_IN_GROUP",
        6 => "NOT_IN_GROUP",
        7 => "NOT_LEADER",
        8 => "WRONG_FACTION",
        9 => "IGNORING_YOU",
        _ => $"err={r}"
    };

    // --- update object: best-effort парсинг только GUID'ов ---
    // Полный парсинг movement+values блока — недели работы. Вместо этого пробуем
    // минимально: для каждого блока читаем update_type, packed_guid, дальше
    // НЕ можем корректно скипнуть остаток без полного парсинга, поэтому
    // если встретили валидный GUID — записываем и **выходим из блока**.
    private static int TryExtractGuids(byte[] data, List<ulong> guids)
    {
        try
        {
            var br = new BinaryReader(new MemoryStream(data));
            var numBlocks = br.ReadUInt32();
            // ограничение разумности
            if (numBlocks > 1000) return 0;
            for (var i = 0; i < numBlocks && br.BaseStream.Position < br.BaseStream.Length; i++)
            {
                var updateType = br.ReadByte();
                if (updateType == 4) // OUT_OF_RANGE_OBJECTS
                {
                    var n = br.ReadUInt32();
                    for (var j = 0; j < n; j++) ReadPackedGuid(br);
                    continue;
                }
                if (updateType == 5) // NEAR_OBJECTS
                {
                    var n = br.ReadUInt32();
                    for (var j = 0; j < n; j++) ReadPackedGuid(br);
                    continue;
                }
                if (updateType <= 3)
                {
                    var guid = ReadPackedGuid(br);
                    if (guid != 0 && !guids.Contains(guid)) guids.Add(guid);
                    // дальше movement+values блок переменной длины — не можем корректно
                    // распарсить, ломаемся и выходим из пакета (неполный охват)
                    break;
                }
            }
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    private static ulong ReadPackedGuid(BinaryReader br)
    {
        var mask = br.ReadByte();
        ulong guid = 0;
        for (var i = 0; i < 8; i++)
        {
            if ((mask & (1 << i)) != 0)
                guid |= (ulong)br.ReadByte() << (i * 8);
        }
        return guid;
    }

    private static byte[] ZlibDecompress(byte[] data)
    {
        // SMSG_COMPRESSED_UPDATE_OBJECT: uint32 uncompressed_size, byte[] zlib_compressed
        var uncompSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));
        using var input = new MemoryStream(data, 4, data.Length - 4);
        // skip zlib header (2 bytes)
        input.ReadByte(); input.ReadByte();
        using var ds = new DeflateStream(input, CompressionMode.Decompress);
        var output = new byte[uncompSize];
        var off = 0;
        while (off < output.Length)
        {
            var r = ds.Read(output, off, output.Length - off);
            if (r == 0) break;
            off += r;
        }
        return output;
    }

    // ----- low-level packet IO -----

    private static byte[] BuildEmptyAddonBlob()
    {
        var raw = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 };
        using var ms = new MemoryStream();
        ms.WriteByte(0x78); ms.WriteByte(0x9C);
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            ds.Write(raw);
        uint a = 1, b = 0;
        foreach (var x in raw) { a = (a + x) % 65521; b = (b + a) % 65521; }
        var adler = (b << 16) | a;
        ms.WriteByte((byte)(adler >> 24));
        ms.WriteByte((byte)(adler >> 16));
        ms.WriteByte((byte)(adler >> 8));
        ms.WriteByte((byte)adler);
        return ms.ToArray();
    }

    private async Task SendCmsgPlain(uint opcode, byte[] payload)
    {
        await _sendLock.WaitAsync();
        try
        {
            var size = payload.Length + 4;
            var header = new byte[6];
            header[0] = (byte)(size >> 8);
            header[1] = (byte)size;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(2), opcode);
            await _stream.WriteAsync(header);
            if (payload.Length > 0) await _stream.WriteAsync(payload);
        }
        finally { _sendLock.Release(); }
    }

    private async Task SendCmsgEncrypted(uint opcode, byte[] payload)
    {
        await _sendLock.WaitAsync();
        try
        {
            var size = payload.Length + 4;
            var header = new byte[6];
            header[0] = (byte)(size >> 8);
            header[1] = (byte)size;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(2), opcode);
            _crypt.EncryptHeader(header);
            await _stream.WriteAsync(header);
            if (payload.Length > 0) await _stream.WriteAsync(payload);
        }
        finally { _sendLock.Release(); }
    }

    // ------- heartbeat + idle loop -------

    public void StartHeartbeat()
    {
        _heartbeatCts = new CancellationTokenSource();
        _lastMoveUpdate = DateTime.UtcNow;
        var ct = _heartbeatCts.Token;
        _heartbeatTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try { AdvancePosition(); await SendMovementPacketAsync(MSG_MOVE_HEARTBEAT); }
                catch (Exception e) { Console.WriteLine($"[HB] error: {e.Message}"); return; }
                try { await Task.Delay(200, ct); } catch { return; }
            }
        }, ct);
        Console.WriteLine($"[HB] heartbeat started (every 200ms)");
    }

    public async Task StopHeartbeatAsync()
    {
        _heartbeatCts?.Cancel();
        if (_heartbeatTask != null) try { await _heartbeatTask; } catch { }
    }

    private void AdvancePosition()
    {
        var now = DateTime.UtcNow;
        var dt = (float)(now - _lastMoveUpdate).TotalSeconds;
        _lastMoveUpdate = now;
        if (dt <= 0 || dt > 1.0f) return; // санити: либо первый тик, либо большой gap

        var moved = false;
        if ((_movementFlags & MOVEMENTFLAG_FORWARD) != 0)
        {
            _x += _runSpeed * dt * MathF.Cos(_ori);
            _y += _runSpeed * dt * MathF.Sin(_ori);
            moved = true;
        }
        else if ((_movementFlags & MOVEMENTFLAG_BACKWARD) != 0)
        {
            _x -= _runSpeed * 0.5f * dt * MathF.Cos(_ori);
            _y -= _runSpeed * 0.5f * dt * MathF.Sin(_ori);
            moved = true;
        }

        if (moved && Nav != null)
        {
            var groundZ = Nav.GetHeight((int)_map, _x, _y, _z);
            if (!float.IsNaN(groundZ))
            {
                // плавно корректируем Z (max 5 ярдов за тик чтобы не «прыгать»)
                var delta = groundZ - _z;
                if (MathF.Abs(delta) <= 5f) _z = groundZ;
                else _z += MathF.Sign(delta) * 5f;
            }
        }
    }

    private async Task SendMovementPacketAsync(uint opcode)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        WritePackedGuid(w, _charGuid);
        w.Write(_movementFlags);
        w.Write((ushort)0);                  // flags2
        w.Write((uint)Environment.TickCount);
        w.Write(_x); w.Write(_y); w.Write(_z); w.Write(_ori);
        w.Write((uint)0);                    // fall_time
        await SendCmsgEncrypted(opcode, ms.ToArray());
    }

    private async Task HandleForceRunSpeedChange(byte[] body, DateTime sessionStart)
    {
        try
        {
            var br = new BinaryReader(new MemoryStream(body));
            var guid = ReadPackedGuid(br);
            var counter = br.ReadUInt32();
            br.ReadByte();             // unk (только для run-speed)
            var newSpeed = br.ReadSingle();

            if (guid != _charGuid) return; // это про другого юнита

            var t = (DateTime.UtcNow - sessionStart).TotalSeconds;
            Console.WriteLine($"[IDLE @ {t:F1}s] FORCE_RUN_SPEED_CHANGE: {_runSpeed:F2} -> {newSpeed:F2} y/s");
            _runSpeed = newSpeed;

            // ACK — без правильного формата сервер обычно не disconnect'ит,
            // но если сервер строгий, тут можно собрать MovementInfo и эхо counter+speed.
            // Делаем минимальный ACK: packed_guid + counter + минимальная MovementInfo + speed.
            using var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            WritePackedGuid(w, _charGuid);
            w.Write(counter);
            // MovementInfo:
            w.Write(_movementFlags);
            w.Write((ushort)0);                    // flags2
            w.Write((uint)Environment.TickCount);
            w.Write(_x); w.Write(_y); w.Write(_z); w.Write(_ori);
            w.Write((uint)0);                      // fall_time
            // speed
            w.Write(newSpeed);
            await SendCmsgEncrypted(CMSG_FORCE_RUN_SPEED_CHANGE_ACK, ms.ToArray());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SPEED] parse/ack error: {ex.Message}");
        }
    }

    public async Task SetFacingAsync(float orientationRad)
    {
        _ori = orientationRad;
        await SendMovementPacketAsync(MSG_MOVE_SET_FACING);
    }

    public async Task MoveForwardAsync(TimeSpan duration)
    {
        Console.WriteLine($"[MOVE] start forward from ({_x:F1},{_y:F1}) ori={_ori:F2} for {duration.TotalSeconds}s");
        _lastMoveUpdate = DateTime.UtcNow;
        _movementFlags |= MOVEMENTFLAG_FORWARD;
        await SendMovementPacketAsync(MSG_MOVE_START_FORWARD);
        await Task.Delay(duration);
        _movementFlags &= ~MOVEMENTFLAG_FORWARD;
        AdvancePosition(); // финальная позиция
        await SendMovementPacketAsync(MSG_MOVE_STOP);
        Console.WriteLine($"[MOVE] stopped at ({_x:F1},{_y:F1})");
    }

    public async Task MoveToAsync(float targetX, float targetY, float tolerance = 2.0f)
    {
        // Если есть NavQuery — пробуем pathfinding через навмеш (обход стен)
        if (Nav != null)
        {
            var path = Nav.FindPath((int)_map, _x, _y, _z, targetX, targetY, _z);
            if (path != null && path.Count > 1)
            {
                Console.WriteLine($"[MOVE] FindPath: {path.Count} waypoints");
                foreach (var (wx, wy, wz) in path.Skip(1)) // первая точка — наша start
                {
                    await MoveStraightToAsync(wx, wy, tolerance);
                }
                return;
            }
            Console.WriteLine($"[MOVE] FindPath не вернул путь — иду напрямую");
        }
        await MoveStraightToAsync(targetX, targetY, tolerance);
    }

    private async Task MoveStraightToAsync(float targetX, float targetY, float tolerance)
    {
        var dx = targetX - _x;
        var dy = targetY - _y;
        var totalDist = MathF.Sqrt(dx * dx + dy * dy);
        if (totalDist < tolerance) return;

        var ori = MathF.Atan2(dy, dx);
        Console.WriteLine($"[MOVE]   -> ({targetX:F1},{targetY:F1}) dist={totalDist:F1}");
        await SetFacingAsync(ori);
        await Task.Delay(100);

        _lastMoveUpdate = DateTime.UtcNow;
        _movementFlags |= MOVEMENTFLAG_FORWARD;
        await SendMovementPacketAsync(MSG_MOVE_START_FORWARD);

        var maxSeconds = (totalDist / _runSpeed) + 3.0f;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(maxSeconds);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            var rdx = targetX - _x;
            var rdy = targetY - _y;
            var rem = MathF.Sqrt(rdx * rdx + rdy * rdy);
            if (rem < tolerance) break;
        }

        _movementFlags &= ~MOVEMENTFLAG_FORWARD;
        AdvancePosition();
        await SendMovementPacketAsync(MSG_MOVE_STOP);
        Console.WriteLine($"[MOVE]   stopped at ({_x:F1},{_y:F1},{_z:F1})");
    }

    private static void WritePackedGuid(BinaryWriter w, ulong guid)
    {
        var maskPos = w.BaseStream.Position;
        w.Write((byte)0); // placeholder для маски
        byte mask = 0;
        for (var i = 0; i < 8; i++)
        {
            var b = (byte)((guid >> (i * 8)) & 0xFF);
            if (b != 0) { w.Write(b); mask |= (byte)(1 << i); }
        }
        var endPos = w.BaseStream.Position;
        w.BaseStream.Position = maskPos;
        w.Write(mask);
        w.BaseStream.Position = endPos;
    }

    // ------- общий цикл чтения пакетов -------

    public sealed class OpcodeStat
    {
        public int Count;
        public double FirstAt;
        public double LastAt;
        public int FirstBodySize;
        public byte[]? FirstBodyHex;  // первые 32 байта первого пакета
    }
    private readonly Dictionary<ushort, OpcodeStat> _opStats = new();

    public IReadOnlyDictionary<ushort, OpcodeStat> OpcodeStatistics => _opStats;

    public async Task IdleAsync(TimeSpan duration)
    {
        var start = DateTime.UtcNow;
        var deadline = start + duration;
        var stats = new Dictionary<ushort, int>();

        while (DateTime.UtcNow < deadline)
        {
            // Никаких таймаутов на read — иначе orphan read десинкает RC4.
            // Сервер в нормальной игре всегда что-то шлёт (TIME_SYNC каждые ~10с,
            // UpdateObject постоянно). Если внезапно тихо — выйдем по факту разрыва TCP.
            int size; ushort op; byte[] body;
            try
            {
                (size, op) = await ReadEncryptedHeader();
                body = await ReadExactly(size - 2);
            }
            catch (Exception e)
            {
                var t = (DateTime.UtcNow - start).TotalSeconds;
                Console.WriteLine($"[IDLE @ {t:F1}s] read error: {e.Message}");
                return;
            }

            stats.TryGetValue(op, out var c);
            stats[op] = c + 1;

            // Детальная статистика для анализа anti-cheat'а
            var elapsedNow = (DateTime.UtcNow - start).TotalSeconds;
            if (!_opStats.TryGetValue(op, out var os))
            {
                os = new OpcodeStat { FirstAt = elapsedNow, FirstBodySize = body.Length,
                    FirstBodyHex = body.AsSpan(0, Math.Min(body.Length, 32)).ToArray() };
                _opStats[op] = os;
            }
            os.Count++;
            os.LastAt = elapsedNow;

            if (op == SMSG_TIME_SYNC_REQ)
            {
                var counter = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0, 4));
                var resp = new byte[8];
                BinaryPrimitives.WriteUInt32LittleEndian(resp.AsSpan(0, 4), counter);
                BinaryPrimitives.WriteUInt32LittleEndian(resp.AsSpan(4, 4), (uint)Environment.TickCount);
                await SendCmsgEncrypted(CMSG_TIME_SYNC_RESP, resp);
            }
            else if (op == SMSG_MESSAGECHAT)
            {
                HandleChatMessage(body);
            }
            else if (op == SMSG_NAME_QUERY_RESPONSE)
            {
                HandleNameQueryResponse(body);
            }
            else if (op == SMSG_UPDATE_OBJECT)
            {
                var st = UpdateObjectParser.Parse(body, World);
                if (st.Created > 0 || st.Removed > 0)
                {
                    var t = (DateTime.UtcNow - start).TotalSeconds;
                    Console.WriteLine($"[IDLE @ {t:F1}s] world: +{st.Created}/-{st.Removed} (всего {World.Count})");
                }
            }
            else if (op == SMSG_COMPRESSED_UPDATE_OBJECT)
            {
                var decompressed = ZlibDecompress(body);
                var st = UpdateObjectParser.Parse(decompressed, World);
                if (st.Created > 0 || st.Removed > 0)
                {
                    var t = (DateTime.UtcNow - start).TotalSeconds;
                    Console.WriteLine($"[IDLE @ {t:F1}s] world(compressed): +{st.Created}/-{st.Removed} (всего {World.Count})");
                }
            }
            else if (IsMovementOpcode(op))
            {
                HandleEntityMovement(body);
            }
            else if (op == 0x02E6)
            {
                await HandleWardenData(body);
            }
            else if (op == SMSG_FORCE_RUN_SPEED_CHANGE)
            {
                await HandleForceRunSpeedChange(body, start);
            }
            else if (op == SMSG_FORCE_WALK_SPEED_CHANGE
                  || op == SMSG_FORCE_RUN_BACK_SPEED_CHANGE
                  || op == SMSG_FORCE_SWIM_SPEED_CHANGE)
            {
                // парсим но не применяем — мы только бегаем вперёд
                var t = (DateTime.UtcNow - start).TotalSeconds;
                Console.WriteLine($"[IDLE @ {t:F1}s] FORCE_*_SPEED_CHANGE op=0x{op:X4} (игнорируем)");
            }

            // обработка очереди команд (пытаемся ответить на шёпоты с "!...")
            if (CommandMode) await ProcessPendingCommands();
            else if (op == SMSG_LOGIN_VERIFY_WORLD && body.Length >= 20)
            {
                _map = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0, 4));
                _x = BitConverter.ToSingle(body, 4);
                _y = BitConverter.ToSingle(body, 8);
                _z = BitConverter.ToSingle(body, 12);
                _ori = BitConverter.ToSingle(body, 16);
                var t = (DateTime.UtcNow - start).TotalSeconds;
                Console.WriteLine($"[IDLE @ {t:F1}s] LOGIN_VERIFY_WORLD map={_map} pos=({_x:F0}, {_y:F0}, {_z:F0})");
            }
            else if (c == 0 && op != SMSG_UPDATE_OBJECT && op != SMSG_COMPRESSED_UPDATE_OBJECT)
            {
                var t = (DateTime.UtcNow - start).TotalSeconds;
                var hex = Convert.ToHexString(body.AsSpan(0, Math.Min(body.Length, 32)));
                Console.WriteLine($"[IDLE @ {t:F1}s] новый опкод 0x{op:X4} size={size} body[0..32]={hex}");
            }
        }
        var totalTime = (DateTime.UtcNow - start).TotalSeconds;
        Console.WriteLine($"[IDLE] идл завершён за {totalTime:F1}s. Опкоды по частоте:");
        foreach (var kv in stats.OrderByDescending(p => p.Value).Take(15))
            Console.WriteLine($"  0x{kv.Key:X4}  x{kv.Value}");
    }

    private async Task<(int size, ushort opcode)> ReadPlainServerHeader()
    {
        var head = await ReadExactly(4);
        return ((head[0] << 8) | head[1], (ushort)(head[2] | (head[3] << 8)));
    }

    private async ValueTask<(int size, ushort opcode)> ReadEncryptedHeader()
    {
        var first = await ReadExactly(1);
        _crypt.DecryptHeader(first);
        if ((first[0] & 0x80) != 0)
        {
            var rest = await ReadExactly(4);
            _crypt.DecryptHeader(rest);
            var size = ((first[0] & 0x7F) << 16) | (rest[0] << 8) | rest[1];
            var op = (ushort)(rest[2] | (rest[3] << 8));
            return (size, op);
        }
        else
        {
            var rest = await ReadExactly(3);
            _crypt.DecryptHeader(rest);
            var size = (first[0] << 8) | rest[0];
            var op = (ushort)(rest[1] | (rest[2] << 8));
            return (size, op);
        }
    }

    private async Task<byte[]> ReadExactly(int n)
    {
        var buf = new byte[n];
        var off = 0;
        while (off < n)
        {
            var r = await _stream.ReadAsync(buf.AsMemory(off, n - off));
            if (r == 0) throw new EndOfStreamException("server closed connection");
            off += r;
        }
        return buf;
    }

    // ----- char enum parsing -----

    private static List<CharInfo> ParseCharEnum(byte[] body)
    {
        var result = new List<CharInfo>();
        var br = new BinaryReader(new MemoryStream(body));
        var num = br.ReadByte();
        for (var i = 0; i < num; i++)
        {
            var guid = br.ReadUInt64();
            var name = ReadCString(br);
            var race = br.ReadByte();
            var cls = br.ReadByte();
            var gender = br.ReadByte();
            br.ReadBytes(5);
            var level = br.ReadByte();
            var zone = br.ReadUInt32();
            var map = br.ReadUInt32();
            var x = br.ReadSingle();
            var y = br.ReadSingle();
            var z = br.ReadSingle();
            br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32();
            br.ReadByte();
            br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32();
            for (var s = 0; s < 23; s++)
            {
                br.ReadUInt32(); br.ReadByte(); br.ReadUInt32();
            }
            result.Add(new CharInfo(guid, name, race, cls, gender, level, zone, map, x, y, z));
        }
        return result;
    }

    private static string ReadCString(BinaryReader br)
    {
        var bytes = new List<byte>();
        byte b;
        while ((b = br.ReadByte()) != 0) bytes.Add(b);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
