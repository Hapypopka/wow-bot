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
    private const ushort SMSG_PING               = 0x1DD; // bidirectional in some impl
    private const uint   CMSG_KEEP_ALIVE         = 0x40A;

    private readonly TcpClient _tcp = new();
    private NetworkStream _stream = null!;
    private WowCrypt _crypt = null!;
    private readonly ushort _build;

    public WorldClient(ushort build) { _build = build; }

    public void Dispose() => _tcp.Dispose();

    public async Task ConnectAndAuthAsync(string host, int port, string account, byte realmId, byte[] sessionKey)
    {
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
            Console.WriteLine($"[WORLD]    skip pre-auth opcode=0x{op:X4} size={size}");
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
            if (op == SMSG_CHAR_ENUM)
            {
                var chars = ParseCharEnum(body);
                Console.WriteLine($"[WORLD] <- SMSG_CHAR_ENUM ({chars.Count} chars)");
                return chars;
            }
        }
        throw new Exception("no SMSG_CHAR_ENUM in 30 packets");
    }

    public async Task<WorldEntryStats> EnterWorldAsync(ulong charGuid, TimeSpan observe)
    {
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
        var deadline = DateTime.UtcNow + observe;

        while (DateTime.UtcNow < deadline)
        {
            var readTask = ReadEncryptedHeader();
            var timeoutTask = Task.Delay(deadline - DateTime.UtcNow);
            var done = await Task.WhenAny(readTask.AsTask(), timeoutTask);
            if (done == timeoutTask) break;
            var (size, op) = await readTask;
            var body = await ReadExactly(size - 2);

            switch (op)
            {
                case SMSG_LOGIN_VERIFY_WORLD:
                    map = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0, 4));
                    x = BitConverter.ToSingle(body, 4);
                    y = BitConverter.ToSingle(body, 8);
                    z = BitConverter.ToSingle(body, 12);
                    ori = BitConverter.ToSingle(body, 16);
                    verifyReceived = true;
                    Console.WriteLine($"[WORLD] <- LOGIN_VERIFY_WORLD map={map} pos=({x:F1}, {y:F1}, {z:F1}) ori={ori:F2}");
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

                default:
                    // молча скипаем всё остальное
                    break;
            }
        }

        if (!verifyReceived)
            Console.WriteLine($"[WORLD] !! LOGIN_VERIFY_WORLD не пришёл — возможно сервер не пустил в мир");
        return new WorldEntryStats(map, x, y, z, ori, updatePackets, objectsCreated, nearbyGuids);
    }

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
        var size = payload.Length + 4;
        var header = new byte[6];
        header[0] = (byte)(size >> 8);
        header[1] = (byte)size;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(2), opcode);
        await _stream.WriteAsync(header);
        if (payload.Length > 0) await _stream.WriteAsync(payload);
    }

    private async Task SendCmsgEncrypted(uint opcode, byte[] payload)
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
