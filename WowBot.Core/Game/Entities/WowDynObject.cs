using System.Linq;
using WowBot.Core.Memory;

namespace WowBot.Core.Game.Entities;

/// <summary>
/// DynamicObject — AoE зона на земле (Consecration, Death and Decay, Blizzard и т.д.)
/// Используется для AoE Avoidance — бежать из лужи.
/// Оффсеты из AmeisenBotX WowDynobject335a.
/// </summary>
public class WowDynObject : WowObject
{
    public WowDynObject(MemoryReader memory, uint baseAddress) : base(memory, baseAddress)
    {
        // Позиция: BaseAddress + 0xE8 (X, Y, Z как float)
        X = memory.ReadFloat(baseAddress + 0xE8);
        Y = memory.ReadFloat(baseAddress + 0xEC);
        Z = memory.ReadFloat(baseAddress + 0xF0);

        // Descriptor: Caster(8) + Bytes(4) + SpellId(4) + Radius(4)
        uint desc = DescriptorBase;
        Caster = memory.ReadUInt64(desc);
        SpellId = memory.ReadInt32(desc + 0x0C);
        float rawRadius = memory.ReadFloat(desc + 0x10);
        // WoWCircle: Radius в дескрипторе = 1.0 (неверно). Используем фикс по SpellId
        Radius = rawRadius > 1.5f ? rawRadius : GetDefaultRadius(SpellId);

        // Raw дамп для дебага оффсетов
        try
        {
            byte[] raw = memory.ReadBytes(desc, 32);
            RawDump = string.Join(" ", raw.Select(b => $"{b:X2}"));
        }
        catch { RawDump = ""; }
    }

    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public ulong Caster { get; }
    public int SpellId { get; }
    public float Radius { get; }
    public string RawDump { get; }

    /// <summary>Дефолтный радиус по spell ID (WoWCircle не записывает Radius в дескриптор)</summary>
    private static float GetDefaultRadius(int spellId) => spellId switch
    {
        43265 or 49936 or 49937 or 49938 or 52212 => 10f, // Death and Decay
        26573 or 48818 or 48819 => 8f,                     // Consecration
        2121 or 8422 or 8423 or 10215 or 10216 or 27086 or 42925 or 42926 => 8f, // Flamestrike
        10 or 42208 or 42209 or 42210 or 42211 or 42212 or 42213 or 42198 => 8f, // Blizzard
        5740 or 42218 or 42223 or 42224 or 42225 or 42226 => 8f, // Rain of Fire
        16914 or 48295 => 10f,  // Hurricane
        _ => 8f                 // дефолт 8 ярдов для неизвестных
    };
}
