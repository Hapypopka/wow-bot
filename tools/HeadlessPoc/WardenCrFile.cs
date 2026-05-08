// Парсер .cr файла из vmangos/warden_modules коллекции.
// Один .cr на каждый Warden module hash — содержит 1000 pre-computed (seed → reply + RC4 keys) записей
// плюс 9 мапящих опкодов модуля для типов проверок.
//
// Формат:
//   Header (17 bytes):
//     [0..3]  uint32 memoryRead     (LE)
//     [4..7]  uint32 pageScanCheck  (LE)
//     [8..16] byte[9] check opcodes (MEM, MODULE, PAGE_A, PAGE_B, MPQ, LUA, PROC, DRIVER, TIMING)
//
//   Entries (68 bytes each):
//     [0..15]  byte[16] seed
//     [16..35] byte[20] reply (SHA1 result for HASH_REQUEST)
//     [36..51] byte[16] clientKey (post-handshake CMSG RC4)
//     [52..67] byte[16] serverKey (post-handshake SMSG RC4)

namespace WowBot.HeadlessPoc;

internal sealed class WardenCrFile
{
    public uint MemoryRead { get; }
    public uint PageScanCheck { get; }
    public byte[] CheckOpcodes { get; } // 9 байт: MEM, MODULE, PAGE_A, PAGE_B, MPQ, LUA, PROC, DRIVER, TIMING
    public IReadOnlyList<CrEntry> Entries { get; }

    public sealed record CrEntry(byte[] Seed, byte[] Reply, byte[] ClientKey, byte[] ServerKey);

    private const int HEADER_SIZE = 17;
    private const int ENTRY_SIZE = 68;

    private WardenCrFile(uint mem, uint page, byte[] opcodes, IReadOnlyList<CrEntry> entries)
    {
        MemoryRead = mem;
        PageScanCheck = page;
        CheckOpcodes = opcodes;
        Entries = entries;
    }

    public static WardenCrFile Load(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < HEADER_SIZE)
            throw new InvalidDataException($"CR file too small ({data.Length} bytes)");

        var memRead   = BitConverter.ToUInt32(data, 0);
        var pageScan  = BitConverter.ToUInt32(data, 4);
        var opcodes   = data.AsSpan(8, 9).ToArray();

        var entryCount = (data.Length - HEADER_SIZE) / ENTRY_SIZE;
        if (entryCount == 0)
            throw new InvalidDataException("CR file has no entries");

        var entries = new List<CrEntry>(entryCount);
        var off = HEADER_SIZE;
        for (var i = 0; i < entryCount; i++)
        {
            var seed   = data.AsSpan(off,      16).ToArray();
            var reply  = data.AsSpan(off + 16, 20).ToArray();
            var ckey   = data.AsSpan(off + 36, 16).ToArray();
            var skey   = data.AsSpan(off + 52, 16).ToArray();
            entries.Add(new CrEntry(seed, reply, ckey, skey));
            off += ENTRY_SIZE;
        }
        return new WardenCrFile(memRead, pageScan, opcodes, entries);
    }

    /// <summary>Найти запись по seed. Returns null если seed не в базе.</summary>
    public CrEntry? LookupBySeed(byte[] seed)
    {
        if (seed.Length != 16) return null;
        foreach (var e in Entries)
        {
            if (Equal(e.Seed, seed)) return e;
        }
        return null;
    }

    private static bool Equal(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>Описание модульного опкода — для лога.</summary>
    public string DescribeCheckOpcode(byte op)
    {
        var names = new[] { "MEM", "MODULE", "PAGE_A", "PAGE_B", "MPQ", "LUA", "PROC", "DRIVER", "TIMING" };
        for (var i = 0; i < CheckOpcodes.Length; i++)
            if (CheckOpcodes[i] == op) return names[i];
        return $"UNK_0x{op:X2}";
    }
}
