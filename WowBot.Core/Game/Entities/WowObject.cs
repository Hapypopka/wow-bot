using WowBot.Core.Memory;

namespace WowBot.Core.Game.Entities;

public class WowObject
{
    protected readonly MemoryReader Memory;

    public uint BaseAddress { get; }
    public ulong Guid { get; }
    public WowObjectType Type { get; }

    public WowObject(MemoryReader memory, uint baseAddress)
    {
        Memory = memory;
        BaseAddress = baseAddress;
        Guid = memory.ReadUInt64(baseAddress + Offsets.ObjectGuid);
        Type = (WowObjectType)memory.ReadInt32(baseAddress + Offsets.ObjectType);
    }

    protected uint DescriptorBase => Memory.ReadUInt32(BaseAddress + Offsets.ObjectDescriptors);

    protected int ReadDescriptorInt(uint index) =>
        Memory.ReadInt32(DescriptorBase + index * 4);

    protected uint ReadDescriptorUInt(uint index) =>
        Memory.ReadUInt32(DescriptorBase + index * 4);

    protected ulong ReadDescriptorGuid(uint index) =>
        Memory.ReadUInt64(DescriptorBase + index * 4);

    protected float ReadDescriptorFloat(uint index) =>
        Memory.ReadFloat(DescriptorBase + index * 4);
}
