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
    private readonly SemaphoreSlim _serverWriteLock = new(1, 1);
    private readonly SemaphoreSlim _clientWriteLock = new(1, 1);
    private readonly Action<string> _log;
    private readonly bool _verbose;

    public WorldBridge(NetworkStream clientStream, NetworkStream serverStream,
                       byte[] kClient, byte[] kServer,
                       Action<string> log, bool verbose)
    {
        _clientStream = clientStream;
        _serverStream = serverStream;
        _clientCrypt = new WowCrypt(kClient);
        _serverCrypt = new WowCrypt(kServer);
        _wardenClient = new WardenCrypt(kClient);
        _wardenServer = new WardenCrypt(kServer);
        _log = log;
        _verbose = verbose;
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
                    if (_verbose) _log($"WARDEN ↑ {body.Length}b code=0x{code:X2} (re-encrypt phase)");
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
