// Warden anti-cheat crypto.
// Используется для отдельного RC4-потока внутри SMSG_WARDEN_DATA / CMSG_WARDEN_DATA.
//
// Ключи деривируются из session key K через SessionKeyGenerator (SHA1-PRF из TC):
//   o1 = SHA1(K[0..20])
//   o2 = SHA1(K[20..40])
//   o0 = SHA1(o1 || zeros || o2)
//   далее каждые 20 байт обновляем o0 = SHA1(o1 || o0 || o2)
//
// Из этого PRF берём:
//   inputKey  (16 байт) — для CMSG (наш encrypt → server decrypt)
//   outputKey (16 байт) — для SMSG (server encrypt → наш decrypt)
//   seed      (16 байт) — используется в HASH_REQUEST/RESULT

using System.Security.Cryptography;

namespace WowBot.HeadlessPoc;

internal sealed class WardenCrypt
{
    private readonly Rc4 _outgoing;  // CMSG_WARDEN_DATA encrypt
    private readonly Rc4 _incoming;  // SMSG_WARDEN_DATA decrypt
    public byte[] Seed { get; }
    public byte[] OutgoingKey { get; }
    public byte[] IncomingKey { get; }

    public WardenCrypt(byte[] sessionKey)
    {
        var gen = new SessionKeyGen(sessionKey);
        OutgoingKey = new byte[16]; gen.Generate(OutgoingKey);
        IncomingKey = new byte[16]; gen.Generate(IncomingKey);
        Seed        = new byte[16]; gen.Generate(Seed);

        _outgoing = new Rc4(OutgoingKey);
        _incoming = new Rc4(IncomingKey);
    }

    public void Decrypt(Span<byte> data) => _incoming.Process(data);
    public void Encrypt(Span<byte> data) => _outgoing.Process(data);

    // ---- helpers ----

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
