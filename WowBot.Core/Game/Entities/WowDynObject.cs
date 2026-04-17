using System.Linq;
using WowBot.Abstractions.Entities;
using WowBot.Core.Game.Generated;
using WowBot.Core.Memory;

namespace WowBot.Core.Game.Entities;

/// <summary>
/// DynamicObject — AoE зона на земле (Consecration, Death and Decay, Blizzard и т.д.)
/// Используется для AoE Avoidance — бежать из лужи.
/// Оффсеты из AmeisenBotX WowDynobject335a.
/// </summary>
public class WowDynObject : WowObject, IWowDynObject
{
    public WowDynObject(MemoryReader memory, uint baseAddress) : base(memory, baseAddress)
    {
        // Позиция: BaseAddress + 0xE8 (X, Y, Z как float)
        X = memory.ReadFloat(baseAddress + 0xE8);
        Y = memory.ReadFloat(baseAddress + 0xEC);
        Z = memory.ReadFloat(baseAddress + 0xF0);

        // Descriptor (WoWCircle 3.3.5a, проверено по dump):
        // +0x00 (8b): DynObject's own GUID (not Caster!)
        // +0x08 (4b): Bytes
        // +0x0C (4b): SpellId
        // +0x10 (4b): Radius
        // +0x14 (4b): ???
        // +0x18 (8b): Caster GUID — real owner of the AoE zone
        uint desc = DescriptorBase;
        Caster = memory.ReadUInt64(desc + 0x18);
        SpellId = memory.ReadInt32(desc + 0x0C);
        float rawRadius = memory.ReadFloat(desc + 0x10);
        // WoWCircle: Radius в дескрипторе = 1.0 (неверно). Используем фикс по SpellId
        Radius = rawRadius > 1.5f ? rawRadius : GetDefaultRadius(SpellId);

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

    /// <summary>
    /// Дефолтный радиус по spell ID.
    /// WoWCircle не записывает Radius в дескриптор (всегда 1.0), поэтому берём из сгенерированной
    /// таблицы SpellRadiusTable (построена из Spell.dbc + SpellRadius.dbc).
    /// Fallback 8y для спеллов вне таблицы (редкие кастомные боссовские AoE).
    /// </summary>
    private static float GetDefaultRadius(int spellId)
    {
        var r = SpellRadiusTable.Get(spellId);
        return r > 0 ? r : 8f;
    }
}
