using System.Diagnostics;
using System.Text;
using WowBot.Core;

namespace WowBot.Core.Memory;

public class MemoryReader : IDisposable
{
    private IntPtr _processHandle;
    private Process? _process;
    private DateTime _lastReadWarning = DateTime.MinValue;

    public bool IsAttached => _processHandle != IntPtr.Zero;
    public Process? Process => _process;

    public bool Attach(Process process)
    {
        Detach();

        _process = process;
        _processHandle = WinApi.OpenProcess(
            WinApi.PROCESS_VM_READ | WinApi.PROCESS_QUERY_INFORMATION,
            false, process.Id);

        return _processHandle != IntPtr.Zero;
    }

    public bool AttachForInject(Process process)
    {
        Detach();

        _process = process;
        _processHandle = WinApi.OpenProcess(WinApi.PROCESS_ALL_ACCESS, false, process.Id);

        return _processHandle != IntPtr.Zero;
    }

    public void Detach()
    {
        if (_processHandle != IntPtr.Zero)
        {
            WinApi.CloseHandle(_processHandle);
            _processHandle = IntPtr.Zero;
        }
        _process = null;
    }

    public byte[] ReadBytes(uint address, int count)
    {
        var buffer = new byte[count];
        bool success = WinApi.ReadProcessMemory(_processHandle, (IntPtr)address, buffer, count, out _);
        if (!success)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastReadWarning).TotalSeconds >= 1.0)
            {
                _lastReadWarning = now;
                Logger.Warn($"ReadProcessMemory failed at 0x{address:X8} (count={count})");
            }
            return new byte[count];
        }
        return buffer;
    }

    public uint ReadUInt32(uint address)
    {
        var buffer = ReadBytes(address, 4);
        return BitConverter.ToUInt32(buffer, 0);
    }

    public int ReadInt32(uint address)
    {
        var buffer = ReadBytes(address, 4);
        return BitConverter.ToInt32(buffer, 0);
    }

    public ulong ReadUInt64(uint address)
    {
        var buffer = ReadBytes(address, 8);
        return BitConverter.ToUInt64(buffer, 0);
    }

    public float ReadFloat(uint address)
    {
        var buffer = ReadBytes(address, 4);
        return BitConverter.ToSingle(buffer, 0);
    }

    public string ReadString(uint address, int maxLength = 256)
    {
        var buffer = ReadBytes(address, maxLength);
        int end = Array.IndexOf(buffer, (byte)0);
        if (end < 0) end = maxLength;
        return Encoding.UTF8.GetString(buffer, 0, end);
    }

    // --- Методы записи ---

    public bool WriteBytes(uint address, byte[] data)
    {
        return WinApi.WriteProcessMemory(_processHandle, (IntPtr)address, data, data.Length, out _);
    }

    public bool WriteUInt32(uint address, uint value)
    {
        return WriteBytes(address, BitConverter.GetBytes(value));
    }

    public bool WriteInt32(uint address, int value)
    {
        return WriteBytes(address, BitConverter.GetBytes(value));
    }

    public bool WriteFloat(uint address, float value)
    {
        return WriteBytes(address, BitConverter.GetBytes(value));
    }

    public bool WriteString(uint address, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        return WriteBytes(address, bytes);
    }

    public uint AllocateMemory(uint size, uint protection = WinApi.PAGE_EXECUTE_READWRITE)
    {
        var addr = WinApi.VirtualAllocEx(
            _processHandle, IntPtr.Zero, size,
            WinApi.MEM_COMMIT | WinApi.MEM_RESERVE, protection);
        return (uint)addr;
    }

    public void FreeMemory(uint address, uint size = 0)
    {
        WinApi.VirtualFreeEx(_processHandle, (IntPtr)address, size, WinApi.MEM_RELEASE);
    }

    public IntPtr Handle => _processHandle;

    public void Dispose()
    {
        Detach();
    }
}
