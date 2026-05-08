// Высокоуровневая обёртка над DtNavMesh / DtNavMeshQuery для нашего use-case.
//
// ВАЖНО: WoW использует Z-up систему координат (X=восток-запад, Y=север-юг, Z=высота).
// Detour использует Y-up (X, Y, Z где Y вверх). TrinityCore при генерации mmap'ов
// делает swap: WoW(X,Y,Z) → Detour(Y,Z,X). То есть наш Wow.X = Detour.Y, Wow.Y = Detour.Z, Wow.Z = Detour.X.
// Источник: TrinityCore MapBuilder.cpp.

using System.Collections.Concurrent;
using DotRecast.Core.Numerics;
using DotRecast.Detour;

namespace WowBot.HeadlessPoc.Nav;

public sealed class NavQuery : IDisposable
{
    private readonly string _mmapDir;
    private readonly ConcurrentDictionary<int, DtNavMesh> _meshCache = new();

    public NavQuery(string mmapDir) { _mmapDir = mmapDir; }

    public void Dispose() { _meshCache.Clear(); }

    private DtNavMesh? GetMesh(int mapId)
    {
        if (_meshCache.TryGetValue(mapId, out var mesh)) return mesh;
        var loaded = MmapLoader.LoadMap(_mmapDir, mapId, out var tileCount);
        if (loaded == null) return null;
        Console.WriteLine($"[NAV] map {mapId}: загружено {tileCount} тайлов");
        _meshCache[mapId] = loaded;
        return loaded;
    }

    /// <summary>WoW (X, Y, Z) → Detour (Y, Z, X)</summary>
    private static RcVec3f WowToDetour(float x, float y, float z) => new(y, z, x);

    /// <summary>Detour (X, Y, Z) → WoW (Z, X, Y)</summary>
    private static (float wx, float wy, float wz) DetourToWow(RcVec3f v) => (v.Z, v.X, v.Y);

    /// <summary>Возвращает высоту террейна в точке (wowX, wowY) на карте map. NaN если нет.</summary>
    public float GetHeight(int mapId, float wowX, float wowY, float zHint = 100f)
    {
        var mesh = GetMesh(mapId);
        if (mesh == null) return float.NaN;
        var query = new DtNavMeshQuery(mesh);
        var center = WowToDetour(wowX, wowY, zHint);
        var halfExtents = new RcVec3f(2f, 1000f, 2f); // широкий поиск по высоте
        var filter = new DtQueryDefaultFilter();

        var st = query.FindNearestPoly(center, halfExtents, filter,
            out var polyRef, out var nearestPt, out _);
        if (st.Failed() || polyRef == 0) return float.NaN;

        var st2 = query.GetPolyHeight(polyRef, nearestPt, out var height);
        if (st2.Failed()) return float.NaN;
        return height; // в Detour Y-up = WoW Z
    }

    /// <summary>Прямой raycast по навмешу. Возвращает true если путь свободен.</summary>
    public bool CastRay(int mapId, float fromX, float fromY, float fromZ,
                        float toX, float toY, float toZ)
    {
        var mesh = GetMesh(mapId);
        if (mesh == null) return false;
        var query = new DtNavMeshQuery(mesh);
        var filter = new DtQueryDefaultFilter();

        var start = WowToDetour(fromX, fromY, fromZ);
        var end = WowToDetour(toX, toY, toZ);
        var halfExtents = new RcVec3f(2f, 100f, 2f);

        if (query.FindNearestPoly(start, halfExtents, filter, out var startRef, out var startPt, out _).Failed())
            return false;
        if (startRef == 0) return false;

        Span<long> path = stackalloc long[16];
        var rst = query.Raycast(startRef, startPt, end, filter,
            out var t, out _, path, out _, 16);
        return !rst.Failed() && t >= 1.0f; // t<1 означает попали в стену
    }

    /// <summary>Возвращает waypoints от start до end в WoW координатах. null если не получилось.</summary>
    public List<(float x, float y, float z)>? FindPath(
        int mapId,
        float startX, float startY, float startZ,
        float endX, float endY, float endZ)
    {
        var mesh = GetMesh(mapId);
        if (mesh == null) return null;
        var query = new DtNavMeshQuery(mesh);
        var filter = new DtQueryDefaultFilter();

        var startD = WowToDetour(startX, startY, startZ);
        var endD = WowToDetour(endX, endY, endZ);
        var halfExtents = new RcVec3f(5f, 100f, 5f);

        if (query.FindNearestPoly(startD, halfExtents, filter, out var startRef, out var startPt, out _).Failed() || startRef == 0)
            return null;
        if (query.FindNearestPoly(endD, halfExtents, filter, out var endRef, out var endPt, out _).Failed() || endRef == 0)
            return null;

        Span<long> polys = stackalloc long[256];
        if (query.FindPath(startRef, endRef, startPt, endPt, filter, polys, out var polyCount, 256).Failed() || polyCount == 0)
            return null;

        // Получили цепочку полигонов — теперь сглаживаем её до точечного пути
        Span<DtStraightPath> straightPath = stackalloc DtStraightPath[256];
        var st = query.FindStraightPath(startPt, endPt, polys, polyCount,
            straightPath, out var straightCount, 256, 0);
        if (st.Failed() || straightCount == 0) return null;

        var result = new List<(float, float, float)>(straightCount);
        for (var i = 0; i < straightCount; i++)
        {
            var (wx, wy, wz) = DetourToWow(straightPath[i].pos);
            result.Add((wx, wy, wz));
        }
        return result;
    }
}
