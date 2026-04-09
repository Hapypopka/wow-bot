using WowBot.Abstractions.Entities;
using WowBot.Core.Memory;

namespace WowBot.Core.Game.Entities;

public class WowPlayer : WowUnit, IWowPlayer
{
    public WowPlayer(MemoryReader memory, uint baseAddress) : base(memory, baseAddress) { }
}
