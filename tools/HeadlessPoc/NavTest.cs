// Smoke test для NavQuery (DotRecast + TC mmap).
// dotnet run --project HeadlessPoc -- nav-test

using WowBot.HeadlessPoc.Nav;

namespace WowBot.HeadlessPoc;

internal static class NavTest
{
    public static int Run()
    {
        var mmapDir = Environment.GetEnvironmentVariable("MMAP_DIR")
                      ?? @"D:\SPP\SPP_Classics_V2\SPP_Server\Modules\wotlk\mmaps";
        Console.WriteLine($"[NAV TEST] mmap dir: {mmapDir}");
        if (!Directory.Exists(mmapDir))
        {
            Console.WriteLine($"[NAV TEST] папка не существует");
            return 1;
        }

        using var nav = new NavQuery(mmapDir);

        // Stormwind
        var z0 = nav.GetHeight(0, -8950f, 520f);
        Console.WriteLine($"[NAV TEST] GetHeight(Stormwind -8950, 520) = {z0:F2}  (ожидается ~96)");

        // Stranglethorn
        var z1 = nav.GetHeight(0, -11099.9f, -1562.6f);
        Console.WriteLine($"[NAV TEST] GetHeight(STV -11100, -1562) = {z1:F2}  (ожидается ~28)");

        // Outland — текущая позиция Узянбаевой под лестницей
        var z2 = nav.GetHeight(530, -3984.6f, -11672.4f);
        Console.WriteLine($"[NAV TEST] GetHeight(Outland -3984, -11672) = {z2:F2}");

        // Northrend Dalaran
        var z3 = nav.GetHeight(571, 5797f, 656f);
        Console.WriteLine($"[NAV TEST] GetHeight(Dalaran 5797, 656) = {z3:F2}  (ожидается ~657)");

        // Path в STV
        var path = nav.FindPath(0, -11099.9f, -1562.6f, 28f, -11050f, -1530f, 28f);
        if (path == null) Console.WriteLine($"[NAV TEST] FindPath: null");
        else
        {
            Console.WriteLine($"[NAV TEST] FindPath: {path.Count} waypoints");
            foreach (var (x, y, z) in path) Console.WriteLine($"   ({x:F1}, {y:F1}, {z:F2})");
        }
        return 0;
    }
}
