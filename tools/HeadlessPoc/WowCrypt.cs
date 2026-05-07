// RC4-шифрование заголовков пакетов в WoW 3.3.5.
// Ключи получаем как HMAC-SHA1(magic_constant, K), где K — session key из SRP6.
// Используются разные magic для server→client и client→server.
// После инициализации RC4 пропускаем 1024 байта (RFC 4345 mitigation).

using System.Security.Cryptography;

namespace WowBot.HeadlessPoc;

internal sealed class WowCrypt
{
    // Magic-константы для HMAC из leaked-кода клиента 3.3.5
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

    private readonly Rc4 _decryptIn;   // дешифрует SMSG-заголовки от сервера
    private readonly Rc4 _encryptOut;  // шифрует CMSG-заголовки клиента

    public WowCrypt(byte[] sessionKey)
    {
        var inKey = HMACSHA1.HashData(ServerEncryptKey, sessionKey);
        var outKey = HMACSHA1.HashData(ServerDecryptKey, sessionKey);

        _decryptIn = new Rc4(inKey);
        _encryptOut = new Rc4(outKey);

        // Drop 1024 bytes — этим занимаются обе стороны, для совместимости.
        var sink = new byte[1024];
        _decryptIn.Process(sink);
        _encryptOut.Process(sink);
    }

    public void DecryptHeader(Span<byte> header) => _decryptIn.Process(header);
    public void EncryptHeader(Span<byte> header) => _encryptOut.Process(header);

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
