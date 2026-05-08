// RC4 шифрование заголовков WoW 3.3.5a (копия из HeadlessPoc).
// HMAC-SHA1(magic, K) → RC4 key, drop 1024 байта после init.
//
// В MITM мы используем 4 экземпляра:
//   серверная сторона прокси: speaks к реальному серверу (наш CMSG-encrypt + SMSG-decrypt)
//   клиентская сторона прокси: speaks к реальному клиенту (наш SMSG-encrypt + CMSG-decrypt)
// Все 4 ключа выводятся из одной K — но RC4 state у всех разный.

using System.Security.Cryptography;

namespace WowBot.MitmProxy;

internal sealed class WowCrypt
{
    private static readonly byte[] ServerEncryptKey =
    {
        0xCC, 0x98, 0xAE, 0x04, 0xE8, 0x97, 0xEA, 0xCA,
        0x12, 0xDD, 0xC0, 0x93, 0x42, 0x91, 0x53, 0x57
    };
    private static readonly byte[] ServerDecryptKey =
    {
        0xC2, 0xB3, 0x72, 0x3C, 0xC6, 0xAE, 0xD9, 0xB5,
        0x34, 0x3C, 0x53, 0xEE, 0x2F, 0x43, 0x67, 0xCE
    };

    private readonly Rc4 _smsgRc4;   // SMSG header crypt (server↔client)
    private readonly Rc4 _cmsgRc4;   // CMSG header crypt (client↔server)

    public WowCrypt(byte[] sessionKey)
    {
        var smsgKey = HMACSHA1.HashData(ServerEncryptKey, sessionKey);
        var cmsgKey = HMACSHA1.HashData(ServerDecryptKey, sessionKey);
        _smsgRc4 = new Rc4(smsgKey);
        _cmsgRc4 = new Rc4(cmsgKey);
        var sink = new byte[1024];
        _smsgRc4.Process(sink);
        _cmsgRc4.Process(sink);
    }

    /// <summary>Применить SMSG-поток (server-side encrypt / client-side decrypt).</summary>
    public void ProcessSmsg(Span<byte> data) => _smsgRc4.Process(data);

    /// <summary>Применить CMSG-поток (client-side encrypt / server-side decrypt).</summary>
    public void ProcessCmsg(Span<byte> data) => _cmsgRc4.Process(data);

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
