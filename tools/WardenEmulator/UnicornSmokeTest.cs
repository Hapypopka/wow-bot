// Минимальная проверка что UnicornEngine.Unicorn работает на этой машине:
// эмулируем 3 байта x86 кода (mov eax, 1; ret) и читаем регистр EAX.
//
// Без этого нет смысла идти дальше — весь Phase 3 строится на Unicorn.

using UnicornEngine;
using UnicornEngine.Const;

namespace WowBot.WardenEmulator;

public static class UnicornSmokeTest
{
    /// <summary>Возвращает true если Unicorn успешно эмулировал маленькую x86 программу.</summary>
    public static bool Run(out string report)
    {
        try
        {
            Console.WriteLine("step 1: about to call new Unicorn(...)");
            var u = new Unicorn(Common.UC_ARCH_X86, Common.UC_MODE_32);
            Console.WriteLine("step 2: Unicorn instance created");

            const long baseAddr = 0x1000;
            const int regionSize = 0x1000;
            Console.WriteLine("step 3: about to MemMap");
            u.MemMap(baseAddr, regionSize, Common.UC_PROT_ALL);
            Console.WriteLine("step 4: MemMap done");

            byte[] code = { 0xB8, 0xEF, 0xBE, 0xAD, 0xDE, 0x90 };
            Console.WriteLine("step 5: about to MemWrite");
            u.MemWrite(baseAddr, code);
            Console.WriteLine("step 6: MemWrite done");

            Console.WriteLine("step 7: about to EmuStart");
            u.EmuStart(baseAddr, baseAddr + code.Length, 0, 0);
            Console.WriteLine("step 8: EmuStart done");

            var eax = new byte[4];
            Console.WriteLine("step 9: about to RegRead");
            u.RegRead(X86.UC_X86_REG_EAX, eax);
            Console.WriteLine("step 10: RegRead done");
            uint val = (uint)(eax[0] | (eax[1] << 8) | (eax[2] << 16) | (eax[3] << 24));

            u.Close();

            if (val == 0xDEADBEEF)
            {
                report = $"OK: Unicorn эмулировал x86, EAX={val:X8}";
                return true;
            }
            report = $"FAIL: ожидали 0xDEADBEEF, получили {val:X8}";
            return false;
        }
        catch (Exception ex)
        {
            report = $"EXCEPTION: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }
}
