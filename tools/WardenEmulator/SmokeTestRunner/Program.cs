using System.Runtime.InteropServices;
using WowBot.WardenEmulator;

Console.WriteLine("=== Unicorn.NET smoke test ===");
Console.WriteLine($"Process arch: {RuntimeInformation.ProcessArchitecture}");
Console.WriteLine($"Working dir: {Environment.CurrentDirectory}");
Console.WriteLine($"unicorn.dll exists: {File.Exists("unicorn.dll")}");

try
{
    var ok = UnicornSmokeTest.Run(out var report);
    Console.WriteLine(report);
    Console.WriteLine(ok ? "[PASS]" : "[FAIL]");
    return ok ? 0 : 1;
}
catch (Exception ex)
{
    Console.WriteLine($"OUTER EXCEPTION: {ex.GetType().FullName}");
    Console.WriteLine($"  Message: {ex.Message}");
    Console.WriteLine($"  Stack: {ex.StackTrace}");
    if (ex.InnerException != null)
        Console.WriteLine($"  Inner: {ex.InnerException.Message}");
    return 99;
}
