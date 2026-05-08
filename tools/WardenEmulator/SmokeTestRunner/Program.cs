using WowBot.WardenEmulator;

Console.WriteLine("=== WardenEmulator smoke test (Python bridge) ===");

try
{
    using var bridge = new PythonEmulatorBridge();

    // 1. Ping — проверяем что хелпер отвечает
    Console.WriteLine($"[1/3] ping → unicorn version: {bridge.Ping()}");

    // 2. Smoke — встроенный mov eax, 0xDEADBEEF; nop
    var eax = bridge.Smoke();
    Console.WriteLine($"[2/3] smoke → EAX = 0x{eax:X8} {(eax == 0xDEADBEEF ? "✓" : "✗")}");
    if (eax != 0xDEADBEEF) return 1;

    // 3. Полный сценарий через явные вызовы
    int uc = bridge.Open("x86", 32);
    bridge.Map(uc, 0x1000, 0x1000);
    // mov ebx, 0x12345678; mov ecx, ebx; xor eax, eax; nop
    byte[] code = { 0xBB, 0x78, 0x56, 0x34, 0x12, 0x89, 0xD9, 0x31, 0xC0, 0x90 };
    bridge.Write(uc, 0x1000, code);
    bridge.Emu(uc, 0x1000, 0x1000 + (ulong)code.Length);
    var ebx = bridge.RegRead(uc, "ebx");
    var ecx = bridge.RegRead(uc, "ecx");
    var eaxAfter = bridge.RegRead(uc, "eax");
    bridge.Close(uc);
    Console.WriteLine($"[3/3] full scenario → EBX=0x{ebx:X8} ECX=0x{ecx:X8} EAX=0x{eaxAfter:X8}");
    var allOk = ebx == 0x12345678 && ecx == 0x12345678 && eaxAfter == 0;
    Console.WriteLine(allOk ? "[PASS]" : "[FAIL]");
    return allOk ? 0 : 1;
}
catch (Exception ex)
{
    Console.WriteLine($"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
    if (ex.InnerException != null) Console.WriteLine($"  Inner: {ex.InnerException.Message}");
    return 99;
}
