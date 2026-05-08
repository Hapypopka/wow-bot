// Прямые P/Invoke к unicorn.dll в обход F# биндинга UnicornEngine.Unicorn.
// Биндинг 2.1.3 валит EmuStart на нашей системе по непонятной причине,
// здесь мы вызываем нативный API напрямую — точно так как делает C код.

using System.Runtime.InteropServices;

namespace WowBot.WardenEmulator;

/// <summary>Минимальный direct-P/Invoke к Unicorn. Только то что реально нужно.</summary>
internal static class UnicornNative
{
    private const string Lib = "unicorn";

    // uc_arch
    public const int UC_ARCH_X86 = 4;
    // uc_mode
    public const int UC_MODE_32 = 1 << 2;
    // uc_prot
    public const int UC_PROT_NONE = 0;
    public const int UC_PROT_READ = 1;
    public const int UC_PROT_WRITE = 2;
    public const int UC_PROT_EXEC = 4;
    public const int UC_PROT_ALL = UC_PROT_READ | UC_PROT_WRITE | UC_PROT_EXEC;
    // x86 regs
    public const int UC_X86_REG_EAX = 19;
    public const int UC_X86_REG_EBX = 21;
    public const int UC_X86_REG_ECX = 22;
    public const int UC_X86_REG_EDX = 24;
    public const int UC_X86_REG_EIP = 26;
    public const int UC_X86_REG_ESP = 30;

    // err codes
    public const int UC_ERR_OK = 0;

    [DllImport(Lib, EntryPoint = "uc_open", CallingConvention = CallingConvention.Cdecl)]
    public static extern int uc_open(int arch, int mode, out IntPtr uc);

    [DllImport(Lib, EntryPoint = "uc_close", CallingConvention = CallingConvention.Cdecl)]
    public static extern int uc_close(IntPtr uc);

    [DllImport(Lib, EntryPoint = "uc_mem_map", CallingConvention = CallingConvention.Cdecl)]
    public static extern int uc_mem_map(IntPtr uc, ulong address, UIntPtr size, uint perms);

    [DllImport(Lib, EntryPoint = "uc_mem_write", CallingConvention = CallingConvention.Cdecl)]
    public static extern int uc_mem_write(IntPtr uc, ulong address, byte[] bytes, UIntPtr size);

    [DllImport(Lib, EntryPoint = "uc_mem_read", CallingConvention = CallingConvention.Cdecl)]
    public static extern int uc_mem_read(IntPtr uc, ulong address, byte[] bytes, UIntPtr size);

    [DllImport(Lib, EntryPoint = "uc_emu_start", CallingConvention = CallingConvention.Cdecl)]
    public static extern int uc_emu_start(IntPtr uc, ulong begin, ulong until, ulong timeout, UIntPtr count);

    [DllImport(Lib, EntryPoint = "uc_reg_read", CallingConvention = CallingConvention.Cdecl)]
    public static extern int uc_reg_read(IntPtr uc, int regId, byte[] value);

    [DllImport(Lib, EntryPoint = "uc_reg_write", CallingConvention = CallingConvention.Cdecl)]
    public static extern int uc_reg_write(IntPtr uc, int regId, byte[] value);

    [DllImport(Lib, EntryPoint = "uc_strerror", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr uc_strerror(int code);

    [DllImport(Lib, EntryPoint = "uc_version", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint uc_version(out uint major, out uint minor);

    public static string ErrStr(int code)
    {
        var p = uc_strerror(code);
        return p == IntPtr.Zero ? $"err{code}" : Marshal.PtrToStringAnsi(p) ?? $"err{code}";
    }
}
