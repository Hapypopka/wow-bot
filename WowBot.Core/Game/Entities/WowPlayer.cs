using WowBot.Core.Memory;

namespace WowBot.Core.Game.Entities;

public class WowPlayer : WowUnit
{
    public WowPlayer(MemoryReader memory, uint baseAddress) : base(memory, baseAddress) { }
}
