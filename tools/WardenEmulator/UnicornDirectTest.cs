// Smoke-тест через прямой P/Invoke (UnicornNative).

using System.Runtime.InteropServices;

namespace WowBot.WardenEmulator;

public static class UnicornDirectTest
{
    public static bool Run(out string report)
    {
        IntPtr uc = IntPtr.Zero;
        try
        {
            // 0. version
            UnicornNative.uc_version(out var major, out var minor);
            Console.WriteLine($"native unicorn version: {major}.{minor}");

            // 1. open
            int err = UnicornNative.uc_open(UnicornNative.UC_ARCH_X86, UnicornNative.UC_MODE_32, out uc);
            if (err != 0) { report = $"uc_open failed: {UnicornNative.ErrStr(err)}"; return false; }
            Console.WriteLine($"uc_open OK, uc={uc:X}");

            // 2. mem_map
            ulong baseAddr = 0x1000;
            err = UnicornNative.uc_mem_map(uc, baseAddr, (UIntPtr)0x1000, (uint)UnicornNative.UC_PROT_ALL);
            if (err != 0) { report = $"uc_mem_map failed: {UnicornNative.ErrStr(err)}"; return false; }
            Console.WriteLine("uc_mem_map OK");

            // 3. mem_write: mov eax, 0xDEADBEEF; nop;
            byte[] code = { 0xB8, 0xEF, 0xBE, 0xAD, 0xDE, 0x90 };
            err = UnicornNative.uc_mem_write(uc, baseAddr, code, (UIntPtr)code.Length);
            if (err != 0) { report = $"uc_mem_write failed: {UnicornNative.ErrStr(err)}"; return false; }
            Console.WriteLine("uc_mem_write OK");

            // 4. emu_start — в отдельном потоке, на случай если .NET runtime / GC на главном потоке
            //    конфликтует с Unicorn'овским JIT.
            int emuErr = -1;
            IntPtr ucCapture = uc;
            ulong begin = baseAddr, until = baseAddr + (ulong)code.Length;
            var t = new Thread(() =>
            {
                try { emuErr = UnicornNative.uc_emu_start(ucCapture, begin, until, 0, (UIntPtr)0); }
                catch (Exception ex) { Console.WriteLine($"thread caught: {ex.Message}"); }
            });
            t.IsBackground = true;
            t.Start();
            if (!t.Join(TimeSpan.FromSeconds(5)))
            {
                report = "uc_emu_start timed out";
                return false;
            }
            err = emuErr;
            if (err != 0) { report = $"uc_emu_start failed: {UnicornNative.ErrStr(err)}"; return false; }
            Console.WriteLine("uc_emu_start OK");

            // 5. reg_read EAX
            var eaxBuf = new byte[4];
            err = UnicornNative.uc_reg_read(uc, UnicornNative.UC_X86_REG_EAX, eaxBuf);
            if (err != 0) { report = $"uc_reg_read failed: {UnicornNative.ErrStr(err)}"; return false; }
            uint eax = BitConverter.ToUInt32(eaxBuf);
            Console.WriteLine($"uc_reg_read EAX = {eax:X8}");

            if (eax == 0xDEADBEEF)
            {
                report = $"OK: native Unicorn эмулировал, EAX=0x{eax:X8}";
                return true;
            }
            report = $"FAIL: ожидали 0xDEADBEEF, получили 0x{eax:X8}";
            return false;
        }
        catch (Exception ex)
        {
            report = $"EXCEPTION: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
        finally
        {
            if (uc != IntPtr.Zero) UnicornNative.uc_close(uc);
        }
    }
}
