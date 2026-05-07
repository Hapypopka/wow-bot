// Загрузчик TrinityCore mmap файлов в DotRecast DtNavMesh.
//
// Формат TC 3.3.5a:
//   {map:D3}.mmap         — DtNavMeshParams (28 байт): orig[3]+tw+th+maxTiles+maxPolys
//   {map:D3}{x:D2}{y:D2}.mmtile — 16 байт MmapTileHeader + N байт Detour tile data
//
// MmapTileHeader (16 байт):
//   uint magic = 0x4D4D4150 ("PAMM" в LE)
//   uint dtVersion
//   uint mmapVersion = 15
//   uint size       — длина следующего блока tile data
//   byte usesLiquids + 3 padding
//
// Detour tile data — стандартный формат, парсим через DtMeshDataReader.

using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Io;

namespace WowBot.HeadlessPoc.Nav;

internal static class MmapLoader
{
    private const uint MMAP_MAGIC = 0x4D4D4150; // 'P','A','M','M' (LE)
    private const int  MMAP_VERSION_MIN = 1; // SPP использует версию 8, оригинал TC — 15. Нам важно только что Detour-данные внутри валидны.
    private const int  TILE_GRID_SIZE = 64;

    public static DtNavMesh? LoadMap(string mmapDir, int mapId, out int loadedTiles)
    {
        loadedTiles = 0;
        var mmapPath = Path.Combine(mmapDir, $"{mapId:D3}.mmap");
        if (!File.Exists(mmapPath))
        {
            Console.WriteLine($"[MMAP] {mmapPath} не найден");
            return null;
        }

        DtNavMeshParams navParams;
        using (var fs = File.OpenRead(mmapPath))
        using (var br = new BinaryReader(fs))
        {
            navParams = new DtNavMeshParams
            {
                orig = new RcVec3f(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                tileWidth = br.ReadSingle(),
                tileHeight = br.ReadSingle(),
                maxTiles = br.ReadInt32(),
                maxPolys = br.ReadInt32(),
            };
        }

        var mesh = new DtNavMesh();
        var initStatus = mesh.Init(navParams, DtDetour.DT_VERTS_PER_POLYGON);
        if (initStatus.Failed())
        {
            Console.WriteLine($"[MMAP] mesh.Init failed для map {mapId}");
            return null;
        }

        for (var x = 0; x < TILE_GRID_SIZE; x++)
        {
            for (var y = 0; y < TILE_GRID_SIZE; y++)
            {
                var tilePath = Path.Combine(mmapDir, $"{mapId:D3}{x:D2}{y:D2}.mmtile");
                if (!File.Exists(tilePath)) continue;

                try
                {
                    using var fs = File.OpenRead(tilePath);
                    using var br = new BinaryReader(fs);
                    var magic = br.ReadUInt32();
                    if (magic != MMAP_MAGIC) continue;
                    /*var dtVersion =*/ br.ReadUInt32();
                    var mmapVersion = br.ReadUInt32();
                    if (mmapVersion < MMAP_VERSION_MIN) continue;
                    var size = br.ReadInt32();
                    /*var usesLiquids =*/ br.ReadByte();
                    br.ReadBytes(3); // padding

                    var tileData = br.ReadBytes(size);
                    if (tileData.Length != size) continue;

                    // Detour tile data — стандартный формат, парсим
                    var buf = new RcByteBuffer(tileData);
                    var data = new DtMeshDataReader().Read(buf, DtDetour.DT_VERTS_PER_POLYGON);

                    var addStatus = mesh.AddTile(data, 0, 0, out _);
                    if (!addStatus.Failed()) loadedTiles++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MMAP] tile {x},{y} ошибка: {ex.Message}");
                }
            }
        }

        return mesh;
    }
}
