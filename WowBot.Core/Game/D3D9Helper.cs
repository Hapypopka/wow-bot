using System.Runtime.InteropServices;
using WowBot.Core.Memory;

namespace WowBot.Core.Game;

/// <summary>
/// Находит адрес EndScene в чужом процессе через создание фейкового D3D9 устройства.
/// Работает независимо от оверлеев (Discord, GeForce и т.д.)
/// </summary>
public static class D3D9Helper
{
    private const uint D3D_SDK_VERSION = 32;
    private const int ENDSCENE_VTABLE_INDEX = 42; // IDirect3DDevice9::EndScene

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DPRESENT_PARAMETERS
    {
        public uint BackBufferWidth, BackBufferHeight, BackBufferFormat, BackBufferCount;
        public uint MultiSampleType, MultiSampleQuality;
        public uint SwapEffect;
        public IntPtr hDeviceWindow;
        public int Windowed;
        public int EnableAutoDepthStencil;
        public uint AutoDepthStencilFormat;
        public uint Flags;
        public uint FullScreen_RefreshRateInHz;
        public uint PresentationInterval;
    }

    /// <summary>
    /// Находит EndScene в процессе WoW через dummy D3D9 device.
    /// 1. Создаёт фейковое D3D9 устройство в нашем процессе
    /// 2. Читает EndScene из vtable (индекс 42)
    /// 3. Вычисляет смещение от базы d3d9.dll
    /// 4. Находит d3d9.dll в WoW и применяет смещение
    /// </summary>
    public static uint FindEndSceneInProcess(int processId)
    {
        Logger.Info($"D3D9Helper: starting for PID={processId}");

        // 1. Создаём фейковое D3D9 устройство в нашем процессе
        IntPtr endSceneLocal = GetEndSceneFromDummyDevice();
        if (endSceneLocal == IntPtr.Zero)
        {
            Logger.Error("D3D9Helper: failed to create dummy device");
            return 0;
        }

        // 2. Находим базу d3d9.dll в нашем процессе
        IntPtr d3d9Local = WinApi.GetModuleHandleA("d3d9.dll");
        if (d3d9Local == IntPtr.Zero)
        {
            Logger.Error("D3D9Helper: d3d9.dll not loaded in our process");
            return 0;
        }

        uint offset = (uint)((long)endSceneLocal - (long)d3d9Local);
        Logger.Info($"D3D9Helper: local EndScene=0x{endSceneLocal:X}, d3d9Base=0x{d3d9Local:X}, offset=0x{offset:X}");

        // 3. Находим базу d3d9.dll в WoW — пробуем разные имена
        string[] names = { "d3d9.dll", "D3D9.dll", "D3D9.DLL", "d3d9" };
        uint d3d9Remote = 0;
        foreach (var name in names)
        {
            d3d9Remote = FindModuleBase(processId, name);
            if (d3d9Remote != 0)
            {
                Logger.Info($"D3D9Helper: found '{name}' in WoW at 0x{d3d9Remote:X8}");
                break;
            }
        }

        // 4. Если не нашли по имени — ищем по сигнатуре (MZ header) среди всех модулей
        if (d3d9Remote == 0)
        {
            Logger.Warn("D3D9Helper: d3d9.dll not found by name, trying by content match...");
            d3d9Remote = FindD3D9BySignature(processId, offset);
        }

        if (d3d9Remote == 0)
        {
            Logger.Error("D3D9Helper: d3d9.dll not found in WoW process by any method");
            return 0;
        }

        uint endSceneRemote = d3d9Remote + offset;
        Logger.Info($"D3D9Helper: remote d3d9Base=0x{d3d9Remote:X8}, EndScene=0x{endSceneRemote:X8}");

        // Верификация: читаем первые байты EndScene — должен быть пролог функции
        // (не можем прочитать напрямую из чужого процесса без MemoryReader, но вернём адрес)
        return endSceneRemote;
    }

    /// <summary>
    /// Ищет d3d9.dll среди модулей по содержимому — проверяет что по адресу base+offset
    /// находится код похожий на EndScene (тот же пролог что и у нас)
    /// </summary>
    private static uint FindD3D9BySignature(int processId, uint endSceneOffset)
    {
        IntPtr snap = WinApi.CreateToolhelp32Snapshot(0x00000008 | 0x00000010, (uint)processId);
        if (snap == IntPtr.Zero || snap == (IntPtr)(-1)) return 0;

        // Открываем процесс для чтения
        IntPtr hProc = WinApi.OpenProcess(WinApi.PROCESS_VM_READ, false, processId);
        if (hProc == IntPtr.Zero) { WinApi.CloseHandle(snap); return 0; }

        // Читаем первые 8 байт нашего EndScene для сравнения
        IntPtr localES = WinApi.GetModuleHandleA("d3d9.dll") + (int)endSceneOffset;
        byte[] localBytes = new byte[8];
        Marshal.Copy(localES, localBytes, 0, 8);
        Logger.Info($"D3D9Helper: local EndScene bytes: {BitConverter.ToString(localBytes)}");

        try
        {
            var entry = new WinApi.MODULEENTRY32 { dwSize = (uint)Marshal.SizeOf<WinApi.MODULEENTRY32>() };
            if (!WinApi.Module32First(snap, ref entry)) return 0;

            do
            {
                // Пробуем прочитать по base+offset и сравнить с нашим EndScene
                uint candidate = (uint)entry.modBaseAddr + endSceneOffset;
                byte[] remoteBytes = new byte[8];
                if (WinApi.ReadProcessMemory(hProc, (IntPtr)candidate, remoteBytes, 8, out int read) && read == 8)
                {
                    if (remoteBytes[0] == localBytes[0] && remoteBytes[1] == localBytes[1] &&
                        remoteBytes[2] == localBytes[2] && remoteBytes[3] == localBytes[3])
                    {
                        Logger.Info($"D3D9Helper: signature match! module={entry.szModule} base=0x{entry.modBaseAddr:X8} bytes={BitConverter.ToString(remoteBytes)}");
                        return (uint)entry.modBaseAddr;
                    }
                }
            } while (WinApi.Module32Next(snap, ref entry));

            return 0;
        }
        finally
        {
            WinApi.CloseHandle(hProc);
            WinApi.CloseHandle(snap);
        }
    }

    private static IntPtr GetEndSceneFromDummyDevice()
    {
        IntPtr hwnd = IntPtr.Zero;
        IntPtr d3d = IntPtr.Zero;
        IntPtr device = IntPtr.Zero;

        try
        {
            // Скрытое окно для D3D9
            hwnd = WinApi.CreateWindowExA(0, "STATIC", "WB_D3D", 0, 0, 0, 1, 1,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) return IntPtr.Zero;

            d3d = WinApi.Direct3DCreate9(D3D_SDK_VERSION);
            if (d3d == IntPtr.Zero) return IntPtr.Zero;

            // IDirect3D9::CreateDevice — vtable index 16
            IntPtr d3dVtable = Marshal.ReadIntPtr(d3d);
            IntPtr createDevicePtr = Marshal.ReadIntPtr(d3dVtable, 16 * IntPtr.Size);

            var pp = new D3DPRESENT_PARAMETERS
            {
                BackBufferWidth = 1,
                BackBufferHeight = 1,
                BackBufferFormat = 0, // D3DFMT_UNKNOWN
                BackBufferCount = 1,
                SwapEffect = 1, // D3DSWAPEFFECT_DISCARD
                hDeviceWindow = hwnd,
                Windowed = 1,
                PresentationInterval = 0x80000000, // D3DPRESENT_INTERVAL_IMMEDIATE
            };

            // CreateDevice(adapter=0, devType=1(HAL), hwnd, behaviorFlags=0x20(SOFTWARE_VERTEXPROCESSING), &pp, &device)
            var createDevice = Marshal.GetDelegateForFunctionPointer<CreateDeviceDelegate>(createDevicePtr);
            int hr = createDevice(d3d, 0, 1, hwnd, 0x20, ref pp, out device);
            if (hr != 0 || device == IntPtr.Zero)
            {
                Logger.Error($"D3D9Helper: CreateDevice failed HR=0x{hr:X8}");
                return IntPtr.Zero;
            }

            // EndScene = vtable[42]
            IntPtr deviceVtable = Marshal.ReadIntPtr(device);
            IntPtr endScene = Marshal.ReadIntPtr(deviceVtable, ENDSCENE_VTABLE_INDEX * IntPtr.Size);
            return endScene;
        }
        catch (Exception ex)
        {
            Logger.Error($"D3D9Helper: {ex.Message}");
            return IntPtr.Zero;
        }
        finally
        {
            // Cleanup: Release COM objects
            if (device != IntPtr.Zero) Marshal.Release(device);
            if (d3d != IntPtr.Zero) Marshal.Release(d3d);
            if (hwnd != IntPtr.Zero) WinApi.DestroyWindow(hwnd);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateDeviceDelegate(
        IntPtr self, uint adapter, uint deviceType, IntPtr hFocusWindow,
        uint behaviorFlags, ref D3DPRESENT_PARAMETERS pp, out IntPtr ppReturnedDeviceInterface);

    private static uint FindModuleBase(int processId, string moduleName)
    {
        // TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32 для 32-битных процессов
        IntPtr snap = WinApi.CreateToolhelp32Snapshot(0x00000008 | 0x00000010, (uint)processId);
        if (snap == IntPtr.Zero || snap == (IntPtr)(-1))
        {
            Logger.Error($"D3D9Helper: CreateToolhelp32Snapshot failed, error={Marshal.GetLastWin32Error()}");
            return 0;
        }

        try
        {
            var entry = new WinApi.MODULEENTRY32 { dwSize = (uint)Marshal.SizeOf<WinApi.MODULEENTRY32>() };
            if (!WinApi.Module32First(snap, ref entry))
            {
                Logger.Error($"D3D9Helper: Module32First failed, error={Marshal.GetLastWin32Error()}");
                return 0;
            }

            int count = 0;
            var allModules = new List<string>();
            do
            {
                count++;
                allModules.Add($"{entry.szModule}@0x{entry.modBaseAddr:X8}");

                if (entry.szModule.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                    return (uint)entry.modBaseAddr;

                // Поиск по частичному совпадению пути
                if (entry.szExePath != null && entry.szExePath.Contains("d3d9", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info($"D3D9Helper: d3d9 in path: {entry.szModule} -> {entry.szExePath} at 0x{entry.modBaseAddr:X8}");
                    return (uint)entry.modBaseAddr;
                }
            } while (WinApi.Module32Next(snap, ref entry));

            // Дампим все модули для диагностики
            Logger.Error($"D3D9Helper: '{moduleName}' not found among {count} modules");
            Logger.Info($"D3D9Helper: all modules: {string.Join(", ", allModules)}");
            return 0;
        }
        finally
        {
            WinApi.CloseHandle(snap);
        }
    }
}
