// Bridge между клиентом и сервером в encrypted-фазе.
// Держит обе пары WowCrypt и оба сокета, умеет:
//   - пампить пакеты в обе стороны (decrypt header → re-encrypt → forward)
//   - парсить интересные SMSG (chat etc.)
//   - инъектировать CMSG (отправлять от лица пользователя)

using System.Net.Sockets;
using System.Text;

namespace WowBot.MitmProxy;

internal sealed class WorldBridge
{
    public const ushort SMSG_MESSAGECHAT  = 0x096;
    public const uint   CMSG_MESSAGECHAT  = 0x095;
    public const ushort SMSG_WARDEN_DATA  = 0x2E6;
    public const uint   CMSG_WARDEN_DATA  = 0x2E7;

    public enum ChatType : byte
    {
        System = 0x00, Say = 0x01, Party = 0x02, Raid = 0x03, Guild = 0x04,
        Officer = 0x05, Yell = 0x06, Whisper = 0x07, WhisperForeign = 0x08,
        WhisperInform = 0x09, Emote = 0x0A, TextEmote = 0x0B, MonsterSay = 0x0C,
        MonsterParty = 0x0D, MonsterYell = 0x0E, MonsterWhisper = 0x0F,
        MonsterEmote = 0x10, Channel = 0x11, ChannelJoin = 0x12, ChannelLeave = 0x13,
        ChannelList = 0x14, ChannelNotice = 0x15, ChannelNoticeUser = 0x16,
        Afk = 0x17, Dnd = 0x18, Ignored = 0x19,
        Achievement = 0x30, GuildAchievement = 0x31,
        BgSystemNeutral = 0x32, BgSystemAlliance = 0x33, BgSystemHorde = 0x34,
        RaidBossEmote = 0x37, RaidBossWhisper = 0x38,
    }

    public enum Lang : uint
    {
        Universal = 0, Common = 7, Orcish = 1, Dwarvish = 9, Darnassian = 2,
        Taurahe = 3, Thalassian = 0x8, Trollish = 0xE, Gutterspeak = 0x21,
        Draenei = 0x23, Addon = 0xFFFFFFFF,
    }

    private readonly NetworkStream _clientStream;
    private readonly NetworkStream _serverStream;
    private readonly WowCrypt _clientCrypt;
    private readonly WowCrypt _serverCrypt;
    private readonly WardenCrypt _wardenClient;   // RC4 Warden со стороны клиента (K_client)
    private readonly WardenCrypt _wardenServer;   // RC4 Warden со стороны сервера (K_server)
    // После HASH_RESULT TC re-init'ит Warden RC4 ключами из модуля (одинаковые для клиента и сервера).
    // Мы их не знаем → переходим в passthrough: не трогаем тела Warden, оба endpoint'а шифруют тем же модульным ключом.
    private volatile bool _wardenPassthrough = false;

    // Module capture mode (--capture-module): подменяем MODULE_OK от клиента на MODULE_MISSING,
    // сервер начинает стримить модуль чанками SMSG MODULE_CACHE, мы их склеиваем и сохраняем.
    private readonly bool _captureModule;
    private byte[]? _moduleHash;
    private byte[]? _moduleKey;
    private uint _moduleSize;
    private readonly List<byte> _capturedModuleChunks = new();
    private bool _moduleOkSwappedToMissing = false;
    private bool _moduleSaved = false;

    // HASH capture: запомнить (seed → reply) пару чтобы headless мог пройти handshake без эмуляции модуля.
    private byte[]? _capturedHashSeed;
    private bool _hashPairSaved = false;
    private readonly SemaphoreSlim _serverWriteLock = new(1, 1);
    private readonly SemaphoreSlim _clientWriteLock = new(1, 1);
    private readonly Action<string> _log;
    private readonly bool _verbose;

    public WorldBridge(NetworkStream clientStream, NetworkStream serverStream,
                       byte[] kClient, byte[] kServer,
                       Action<string> log, bool verbose, bool captureModule = false)
    {
        _clientStream = clientStream;
        _serverStream = serverStream;
        _clientCrypt = new WowCrypt(kClient);
        _serverCrypt = new WowCrypt(kServer);
        _wardenClient = new WardenCrypt(kClient);
        _wardenServer = new WardenCrypt(kServer);
        _log = log;
        _verbose = verbose;
        _captureModule = captureModule;
        if (_captureModule) _log("module capture enabled — will force MODULE_MISSING and save bytes");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var t1 = Task.Run(() => PumpServerToClientAsync(ct));
        var t2 = Task.Run(() => PumpClientToServerAsync(ct));
        await Task.WhenAny(t1, t2);
    }

    // ----- Public injection API -----

    public Task SendChatSayAsync(string message)        => SendChatAsync(ChatType.Say, message);
    public Task SendChatYellAsync(string message)       => SendChatAsync(ChatType.Yell, message);
    public Task SendChatPartyAsync(string message)      => SendChatAsync(ChatType.Party, message);
    public Task SendChatGuildAsync(string message)      => SendChatAsync(ChatType.Guild, message);

    public Task SendChatWhisperAsync(string target, string message)
        => SendChatAsync(ChatType.Whisper, message, target);

    public async Task SendChatAsync(ChatType type, string message, string? target = null)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((uint)type);
        // Universal = только для GM. Обычные игроки шлют с расовым языком (Common=7 для альянса, Orcish=1 для орды).
        w.Write((uint)Lang.Common);
        if (type == ChatType.Whisper && target != null)
        {
            w.Write(Encoding.UTF8.GetBytes(target));
            w.Write((byte)0);
        }
        w.Write(Encoding.UTF8.GetBytes(message));
        w.Write((byte)0);
        await SendCmsgAsync(CMSG_MESSAGECHAT, ms.ToArray());
        _log($"[INJECT] CMSG_MESSAGECHAT type={type} target='{target}' msg='{message}'");
    }

    /// <summary>Отправить произвольный CMSG (header + body) серверу.</summary>
    public async Task SendCmsgAsync(uint opcode, byte[] body)
    {
        var totalSize = body.Length + 4;
        var hdr = new byte[6];
        hdr[0] = (byte)((totalSize >> 8) & 0xFF);
        hdr[1] = (byte)(totalSize & 0xFF);
        hdr[2] = (byte)(opcode & 0xFF);
        hdr[3] = (byte)((opcode >> 8) & 0xFF);
        hdr[4] = (byte)((opcode >> 16) & 0xFF);
        hdr[5] = (byte)((opcode >> 24) & 0xFF);

        await _serverWriteLock.WaitAsync();
        try
        {
            _serverCrypt.ProcessCmsg(hdr);
            await _serverStream.WriteAsync(hdr);
            if (body.Length > 0) await _serverStream.WriteAsync(body);
        }
        finally { _serverWriteLock.Release(); }
    }

    // ----- Pumps -----

    // SMSG: server → client. Header 4 (или 5 если size > 0x7FFF).
    private async Task PumpServerToClientAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var b0Buf = await ReadExactly(_serverStream, 1);
                if (b0Buf == null) return;
                _serverCrypt.ProcessSmsg(b0Buf);
                int hdrLen = (b0Buf[0] & 0x80) != 0 ? 5 : 4;
                var hdr = new byte[hdrLen];
                hdr[0] = b0Buf[0];
                var rest = await ReadExactly(_serverStream, hdrLen - 1);
                if (rest == null) return;
                Array.Copy(rest, 0, hdr, 1, rest.Length);
                _serverCrypt.ProcessSmsg(hdr.AsSpan(1));

                int size; ushort opcode;
                if (hdrLen == 4)
                {
                    size = (hdr[0] << 8) | hdr[1];
                    opcode = (ushort)(hdr[2] | (hdr[3] << 8));
                }
                else
                {
                    size = ((hdr[0] & 0x7F) << 16) | (hdr[1] << 8) | hdr[2];
                    opcode = (ushort)(hdr[3] | (hdr[4] << 8));
                }

                var bodyLen = size - 2;
                var body = bodyLen > 0 ? await ReadExactly(_serverStream, bodyLen) : Array.Empty<byte>();
                if (bodyLen > 0 && body == null) return;

                // Parse interesting opcodes
                if (opcode == SMSG_MESSAGECHAT)
                    TryParseChat(body!);

                // Warden re-encryption only до HASH_RESULT. После этого оба endpoint'а используют ключи модуля.
                if (opcode == SMSG_WARDEN_DATA && body!.Length > 0 && !_wardenPassthrough)
                {
                    _wardenServer.Decrypt(body);
                    if (_verbose) _log($"WARDEN ↓ {body.Length}b code=0x{body[0]:X2} (re-encrypt phase)");
                    HandleSmsgWardenPlaintext(body);
                    _wardenClient.Decrypt(body);
                }
                else if (opcode == SMSG_WARDEN_DATA && _verbose)
                {
                    _log($"WARDEN ↓ {body!.Length}b passthrough");
                }

                // Re-encrypt header for client side
                var hdrPlain = new byte[hdrLen];
                Array.Copy(hdr, hdrPlain, hdrLen);

                await _clientWriteLock.WaitAsync(ct);
                try
                {
                    _clientCrypt.ProcessSmsg(hdrPlain);
                    await _clientStream.WriteAsync(hdrPlain, ct);
                    if (body!.Length > 0) await _clientStream.WriteAsync(body, ct);
                }
                finally { _clientWriteLock.Release(); }

                if (_verbose) _log($"SMSG 0x{opcode:X4} size={size}");
            }
        }
        catch (Exception ex) { _log($"server→client pump ended: {ex.Message}"); }
    }

    // CMSG: client → server. Header всегда 6 байт.
    private async Task PumpClientToServerAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var hdr = await ReadExactly(_clientStream, 6);
                if (hdr == null) return;
                _clientCrypt.ProcessCmsg(hdr);

                int size = (hdr[0] << 8) | hdr[1];
                uint opcode = (uint)(hdr[2] | (hdr[3] << 8) | (hdr[4] << 16) | (hdr[5] << 24));

                var bodyLen = size - 4;
                var body = bodyLen > 0 ? await ReadExactly(_clientStream, bodyLen) : Array.Empty<byte>();
                if (bodyLen > 0 && body == null) return;

                // Warden re-encryption only до HASH_RESULT. После — passthrough (модульный ключ симметричный).
                if (opcode == CMSG_WARDEN_DATA && body!.Length > 0 && !_wardenPassthrough)
                {
                    _wardenClient.Encrypt(body);
                    var code = body[0];

                    // Capture mode: подменяем MODULE_OK (0x01) на MODULE_MISSING (0x00) — сервер начнёт стримить модуль
                    if (_captureModule && code == 0x01 && !_moduleOkSwappedToMissing && body.Length == 1)
                    {
                        body[0] = 0x00;
                        _moduleOkSwappedToMissing = true;
                        _log("CAPTURE: swapped CMSG MODULE_OK → MODULE_MISSING — server will stream module");
                        code = 0x00;
                    }

                    if (_verbose) _log($"WARDEN ↑ {body.Length}b code=0x{code:X2} (re-encrypt phase)");
                    HandleCmsgWardenPlaintext(body);
                    _wardenServer.Encrypt(body);

                    // HASH_RESULT (0x04) — последний пакет на исходных ключах. После него TC переключается на module keys.
                    if (code == 0x04)
                    {
                        _wardenPassthrough = true;
                        _log("Warden: HASH_RESULT processed → switching to passthrough (module keys take over)");
                    }
                }
                else if (opcode == CMSG_WARDEN_DATA && _verbose)
                {
                    _log($"WARDEN ↑ {body!.Length}b passthrough");
                }

                var hdrPlain = (byte[])hdr.Clone();

                await _serverWriteLock.WaitAsync(ct);
                try
                {
                    _serverCrypt.ProcessCmsg(hdrPlain);
                    await _serverStream.WriteAsync(hdrPlain, ct);
                    if (body!.Length > 0) await _serverStream.WriteAsync(body, ct);
                }
                finally { _serverWriteLock.Release(); }

                if (_verbose) _log($"CMSG 0x{opcode:X4} size={size}");
            }
        }
        catch (Exception ex) { _log($"client→server pump ended: {ex.Message}"); }
    }

    // ----- Warden module capture -----

    private void HandleSmsgWardenPlaintext(byte[] body)
    {
        if (body.Length < 1) return;
        var code = body[0];

        // MODULE_USE (0x00): cmd + module_hash[16] + module_key[16] + module_size[uint32 LE]
        if (code == 0x00 && body.Length >= 37)
        {
            _moduleHash = body.AsSpan(1, 16).ToArray();
            _moduleKey  = body.AsSpan(17, 16).ToArray();
            _moduleSize = BitConverter.ToUInt32(body, 33);
            _log($"MODULE_USE: hash={Convert.ToHexString(_moduleHash)} key={Convert.ToHexString(_moduleKey)} size={_moduleSize}b");
            return;
        }

        // MODULE_CACHE (0x01): cmd + chunk_size[uint16 LE] + chunk_data[chunk_size]
        if (code == 0x01 && _captureModule && !_moduleSaved && body.Length >= 3)
        {
            var chunkSize = BitConverter.ToUInt16(body, 1);
            if (chunkSize == 0 || body.Length < 3 + chunkSize) return;

            for (int i = 0; i < chunkSize; i++)
                _capturedModuleChunks.Add(body[3 + i]);

            _log($"CAPTURE: MODULE_CACHE chunk +{chunkSize}b (total {_capturedModuleChunks.Count}/{_moduleSize})");

            if (_moduleSize > 0 && _capturedModuleChunks.Count >= _moduleSize)
            {
                SaveCapturedModule();
                _moduleSaved = true;
            }
        }

        // HASH_REQUEST (0x05): cmd + seed[16] — сохраняем seed чтобы потом смэтчить с CMSG HASH_RESULT
        if (code == 0x05 && body.Length >= 17 && _capturedHashSeed == null)
        {
            _capturedHashSeed = body.AsSpan(1, 16).ToArray();
            _log($"HASH_REQUEST captured: seed={Convert.ToHexString(_capturedHashSeed)}");
        }
    }

    /// <summary>Захватываем CMSG HASH_RESULT (cmd=4, body=cmd+reply[20]) и сохраняем пару (seed, reply) на диск
    /// для headless pinned-response fallback.</summary>
    private void HandleCmsgWardenPlaintext(byte[] body)
    {
        if (body.Length < 1) return;
        var code = body[0];

        if (code == 0x04 && body.Length >= 21 && _capturedHashSeed != null && !_hashPairSaved)
        {
            var reply = body.AsSpan(1, 20).ToArray();
            _log($"HASH_RESULT captured: reply={Convert.ToHexString(reply)}");
            try
            {
                var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
                var path = System.IO.Path.Combine(dir, "warden_pinned_response.bin");
                // Формат: seed[16] || reply[20] = 36 байт. Простой бинарный.
                using var fs = System.IO.File.Create(path);
                fs.Write(_capturedHashSeed);
                fs.Write(reply);
                _log($"HASH pair saved: {path} (seed[16]+reply[20]=36b)");

                var infoPath = System.IO.Path.Combine(dir, "warden_pinned_response_info.txt");
                System.IO.File.WriteAllText(infoPath,
                    $"module_hash: {(_moduleHash != null ? Convert.ToHexString(_moduleHash) : "(unknown)")}\n" +
                    $"seed:        {Convert.ToHexString(_capturedHashSeed)}\n" +
                    $"reply:       {Convert.ToHexString(reply)}\n");
                _log($"HASH pair info → {infoPath}");
                _hashPairSaved = true;
            }
            catch (Exception ex)
            {
                _log($"HASH pair save failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private void SaveCapturedModule()
    {
        try
        {
            var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
            var compressedPath = System.IO.Path.Combine(dir, "warden_module_compressed.bin");
            var modulePath     = System.IO.Path.Combine(dir, "warden_module.bin");
            var infoPath       = System.IO.Path.Combine(dir, "warden_module_info.txt");

            var bytes = _capturedModuleChunks.ToArray();
            var rc4 = new ModuleRc4(_moduleKey!);
            rc4.Process(bytes); // RC4-decrypt with module_key → zlib-compressed bytes

            System.IO.File.WriteAllBytes(compressedPath, bytes);
            _log($"CAPTURE: saved {bytes.Length}b RC4-decrypted → {compressedPath}");

            // Формат: первые 4 байта (LE uint32) = uncompressed size, далее zlib stream
            try
            {
                if (bytes.Length < 4) throw new Exception("too small for header");
                var uncompressedSize = BitConverter.ToUInt32(bytes, 0);
                _log($"CAPTURE: header says uncompressed size = {uncompressedSize}b");

                using var ms = new System.IO.MemoryStream(bytes, 4, bytes.Length - 4);
                using var zs = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionMode.Decompress);
                using var output = new System.IO.MemoryStream();
                zs.CopyTo(output);
                System.IO.File.WriteAllBytes(modulePath, output.ToArray());
                _log($"CAPTURE: zlib decompressed {output.Length}b → {modulePath}");
            }
            catch (Exception ex)
            {
                _log($"CAPTURE: zlib decompress failed ({ex.Message}) — only compressed saved");
            }

            System.IO.File.WriteAllText(infoPath,
                $"hash: {Convert.ToHexString(_moduleHash!)}\n" +
                $"key:  {Convert.ToHexString(_moduleKey!)}\n" +
                $"size: {_moduleSize}\n");
            _log($"CAPTURE: module info → {infoPath}");
            _log("CAPTURE: done. Live session may break — restart WoW to play normally.");
        }
        catch (Exception ex)
        {
            _log($"CAPTURE: SaveCapturedModule failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Минимальный RC4 для дешифровки тела модуля (отдельно от WowCrypt/WardenCrypt чтобы не плодить инстансы).
    private sealed class ModuleRc4
    {
        private readonly byte[] _s = new byte[256];
        private int _i, _j;
        public ModuleRc4(byte[] key)
        {
            for (var k = 0; k < 256; k++) _s[k] = (byte)k;
            var jj = 0;
            for (var i = 0; i < 256; i++)
            {
                jj = (jj + _s[i] + key[i % key.Length]) & 0xFF;
                (_s[i], _s[jj]) = (_s[jj], _s[i]);
            }
        }
        public void Process(byte[] data)
        {
            for (var n = 0; n < data.Length; n++)
            {
                _i = (_i + 1) & 0xFF;
                _j = (_j + _s[_i]) & 0xFF;
                (_s[_i], _s[_j]) = (_s[_j], _s[_i]);
                data[n] = (byte)(data[n] ^ _s[(_s[_i] + _s[_j]) & 0xFF]);
            }
        }
    }

    // ----- Chat parser -----
    // Default-ветка SMSG_MESSAGECHAT (TC ChatHandler::BuildChatPacket):
    //   uint8 type, int32 lang, uint64 senderGuid, uint32 0,
    //   uint64 receiverGuid, uint32 msgLen, char[msgLen] msg, uint8 tag

    private void TryParseChat(byte[] body)
    {
        try
        {
            if (body.Length < 1 + 4 + 8 + 4 + 8 + 4 + 1) return;
            var br = new BinaryReader(new MemoryStream(body));
            var type = (ChatType)br.ReadByte();
            var lang = br.ReadUInt32();
            var senderGuid = br.ReadUInt64();
            br.ReadUInt32(); // padding/flags

            // Спец-ветки с lengthprefixed senderName в начале — пропустим (CHAT_MSG_MONSTER_*, ...)
            if (IsMonsterOrAchievementType(type)) return;

            ulong receiverGuid = 0;
            string? channel = null;
            if (type == ChatType.Channel)
            {
                channel = ReadCString(br);
                receiverGuid = br.ReadUInt64();
            }
            else
            {
                receiverGuid = br.ReadUInt64();
            }

            if (br.BaseStream.Position + 4 > body.Length) return;
            var msgLen = br.ReadUInt32();
            if (msgLen > body.Length || br.BaseStream.Position + msgLen > body.Length) return;
            var raw = br.ReadBytes((int)msgLen);
            var msg = Encoding.UTF8.GetString(raw).TrimEnd('\0');

            var langName = lang == 0 ? "" : $" lang={(Lang)lang}";
            var ch = channel != null ? $" #{channel}" : "";
            _log($"[CHAT] [{type}]{ch} sender=0x{senderGuid:X16}{langName}: {msg}");
        }
        catch { /* malformed — ignore */ }
    }

    private static bool IsMonsterOrAchievementType(ChatType t) => t switch
    {
        ChatType.MonsterSay or ChatType.MonsterParty or ChatType.MonsterYell or
        ChatType.MonsterWhisper or ChatType.MonsterEmote or
        ChatType.RaidBossEmote or ChatType.RaidBossWhisper or
        ChatType.Achievement or ChatType.GuildAchievement or
        ChatType.BgSystemNeutral or ChatType.BgSystemAlliance or ChatType.BgSystemHorde => true,
        _ => false
    };

    // ----- helpers -----

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
