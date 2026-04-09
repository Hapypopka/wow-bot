namespace WowBot.Abstractions;

/// <summary>
/// Чтение/запись памяти процесса WoW.
/// Реализация: WowBot.Core.Memory.MemoryReader
/// </summary>
public interface IMemoryReader : IDisposable
{
    bool IsAttached { get; }

    // Чтение
    byte[] ReadBytes(uint address, int count);
    uint ReadUInt32(uint address);
    int ReadInt32(uint address);
    ulong ReadUInt64(uint address);
    float ReadFloat(uint address);
    string ReadString(uint address, int maxLength = 256);

    // Запись
    bool WriteBytes(uint address, byte[] data);
    bool WriteUInt32(uint address, uint value);
    bool WriteInt32(uint address, int value);
    bool WriteFloat(uint address, float value);
    bool WriteString(uint address, string value);

    // Управление памятью
    uint AllocateMemory(uint size, uint protection = 0x40); // PAGE_EXECUTE_READWRITE
    void FreeMemory(uint address, uint size = 0);
}
