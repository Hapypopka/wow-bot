// Smoke test для AmeisenNavClient
// dotnet run --project HeadlessPoc -- nav-test

using WowBot.HeadlessPoc.Nav;

namespace WowBot.HeadlessPoc;

internal static class NavTest
{
    public static int Run()
    {
        using var nav = new AmeisenNavClient();
        if (!nav.TryConnect())
        {
            Console.WriteLine("[NAV TEST] не смогли подключиться к 127.0.0.1:47110");
            return 1;
        }
        Console.WriteLine("[NAV TEST] connected");
        nav.SetClientState(ClientState.Normal);
        nav.SetAreaCosts(ground: 1f, road: 0.75f, water: 1.6f, badLiquid: 4f);
        nav.ApplyFilter();
        Console.WriteLine($"[NAV TEST] filter applied (dirty={nav.IsFilterDirty})");

        // Stormwind в центре города
        var z0 = nav.GetHeight(0, new Vector3(-8950f, 520f, 96f));
        Console.WriteLine($"[NAV TEST] GetHeight(Stormwind) = ({z0.X:F1}, {z0.Y:F1}, {z0.Z:F2})");

        // Stranglethorn
        var z1 = nav.GetHeight(0, new Vector3(-11099.9f, -1562.6f, 28f));
        Console.WriteLine($"[NAV TEST] GetHeight(STV) = ({z1.X:F1}, {z1.Y:F1}, {z1.Z:F2})");

        // Outland
        var z2 = nav.GetHeight(530, new Vector3(-3984.6f, -11672.4f, -139f));
        Console.WriteLine($"[NAV TEST] GetHeight(Outland) = ({z2.X:F1}, {z2.Y:F1}, {z2.Z:F2})");

        // Northrend Dalaran
        var z3 = nav.GetHeight(571, new Vector3(5797f, 656f, 657f));
        Console.WriteLine($"[NAV TEST] GetHeight(Dalaran) = ({z3.X:F1}, {z3.Y:F1}, {z3.Z:F2})");

        // Path в Stranglethorn
        var path = nav.GetPath(0, new Vector3(-11099.9f, -1562.6f, 28f), new Vector3(-11050f, -1530f, 28f));
        if (path == null) Console.WriteLine($"[NAV TEST] GetPath: null");
        else
        {
            Console.WriteLine($"[NAV TEST] GetPath: {path.Length} waypoints");
            foreach (var p in path) Console.WriteLine($"   ({p.X:F1}, {p.Y:F1}, {p.Z:F1})");
        }
        return 0;
    }
}
