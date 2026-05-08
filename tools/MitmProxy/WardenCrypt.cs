// Warden anti-cheat crypto — копия из HeadlessPoc.
// Отдельный RC4-поток внутри SMSG_WARDEN_DATA / CMSG_WARDEN_DATA, ключи деривируются из K через SHA1-PRF.
//
// В MITM мы держим ДВЕ инстанции:
//   wardenServer = WardenCrypt(K_server) — для общения с реальным сервером
//   wardenClient = WardenCrypt(K_client) — для общения с реальным клиентом
//
// При relay'е Warden-пакетов:
//   SMSG_WARDEN_DATA (server → us → client):
//     wardenServer.Decrypt(body)   // расшифровать
//     wardenClient.Encrypt(body)   // перешифровать под клиента
//   CMSG_WARDEN_DATA (client → us → server):
//     wardenClient.Decrypt(body)
//     wardenServer.Encrypt(body)

using System.Security.Cryptography;

namespace WowBot.MitmProxy;

internal sealed class WardenCrypt
{
    private readonly Rc4 _outgoing;
    private readonly Rc4 _incoming;

    public WardenCrypt(byte[] sessionKey)
    {
        var gen = new SessionKeyGen(sessionKey);
        var outgoingKey = new byte[16]; gen.Generate(outgoingKey);
        var incomingKey = new byte[16]; gen.Generate(incomingKey);
        var seed = new byte[16]; gen.Generate(seed); // не используется тут, но порядок важен

        _outgoing = new Rc4(outgoingKey);
        _incoming = new Rc4(incomingKey);
    }

    /// <summary>Расшифровать пакет SMSG_WARDEN_DATA (то что сервер прислал).</summary>
    public void Decrypt(Span<byte> data) => _incoming.Process(data);

    /// <summary>Зашифровать пакет CMSG_WARDEN_DATA (то что клиент шлёт серверу).</summary>
    public void Encrypt(Span<byte> data) => _outgoing.Process(data);

    private sealed class SessionKeyGen
    {
        private readonly byte[] _o0 = new byte[20];
        private readonly byte[] _o1;
        private readonly byte[] _o2;
        private int _o0It;

        public SessionKeyGen(byte[] key)
        {
            var half = key.Length / 2;
            _o1 = SHA1.HashData(key.AsSpan(0, half));
            _o2 = SHA1.HashData(key.AsSpan(half));
            FillUp();
        }

        private void FillUp()
        {
            var input = new byte[60];
            Array.Copy(_o1, 0, input, 0, 20);
            Array.Copy(_o0, 0, input, 20, 20);
            Array.Copy(_o2, 0, input, 40, 20);
            var h = SHA1.HashData(input);
            Array.Copy(h, 0, _o0, 0, 20);
            _o0It = 0;
        }

        public void Generate(byte[] buf)
        {
            for (var i = 0; i < buf.Length; i++)
            {
                if (_o0It == 20) FillUp();
                buf[i] = _o0[_o0It++];
            }
        }
    }

    private sealed class Rc4
    {
        private readonly byte[] _s = new byte[256];
        private int _i, _j;

        public Rc4(byte[] key)
        {
            for (var k = 0; k < 256; k++) _s[k] = (byte)k;
            var jj = 0;
            for (var i = 0; i < 256; i++)
            {
                jj = (jj + _s[i] + key[i % key.Length]) & 0xFF;
                (_s[i], _s[jj]) = (_s[jj], _s[i]);
            }
        }

        public void Process(Span<byte> data)
        {
            for (var n = 0; n < data.Length; n++)
            {
                _i = (_i + 1) & 0xFF;
                _j = (_j + _s[_i]) & 0xFF;
                (_s[_i], _s[_j]) = (_s[_j], _s[_i]);
                var k = _s[(_s[_i] + _s[_j]) & 0xFF];
                data[n] = (byte)(data[n] ^ k);
            }
        }
    }
}
