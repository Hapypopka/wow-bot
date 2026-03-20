using WowBot.Core.Memory;

namespace WowBot.Core.Game.Entities;

public class WowUnit : WowObject
{
    public WowUnit(MemoryReader memory, uint baseAddress) : base(memory, baseAddress) { }

    // --- Основные характеристики ---
    public int Health => ReadDescriptorInt(Offsets.UnitFieldHealth);
    public int MaxHealth => ReadDescriptorInt(Offsets.UnitFieldMaxHealth);
    public float HealthPercent => MaxHealth > 0 ? (float)Health / MaxHealth * 100f : 0f;

    public int Mana => ReadDescriptorInt(Offsets.UnitFieldPower1);
    public int MaxMana => ReadDescriptorInt(Offsets.UnitFieldMaxPower1);
    public float ManaPercent => MaxMana > 0 ? (float)Mana / MaxMana * 100f : 0f;

    public int Rage => ReadDescriptorInt(Offsets.UnitFieldPower2) / 10;
    public int Energy => ReadDescriptorInt(Offsets.UnitFieldPower4);
    public int RunicPower => ReadDescriptorInt(Offsets.UnitFieldPower5) / 10;

    public int Level => ReadDescriptorInt(Offsets.UnitFieldLevel);
    public int FactionTemplate => ReadDescriptorInt(Offsets.UnitFieldFactionTemplate);

    public ulong TargetGuid => ReadDescriptorGuid(Offsets.UnitFieldTarget);

    public bool IsAlive => Health > 0;
    public bool IsDead => Health <= 0;

    // --- Каст ---
    public int CastingSpellId => Memory.ReadInt32(BaseAddress + Offsets.CurrentCastId);
    public int ChannelingSpellId => Memory.ReadInt32(BaseAddress + Offsets.CurrentChannelId);
    public bool IsCasting => CastingSpellId != 0 || ChannelingSpellId != 0;

    // --- Имя (NPC/мобы) ---
    public string Name
    {
        get
        {
            try
            {
                uint ptr1 = Memory.ReadUInt32(BaseAddress + Offsets.UnitNamePtr1);
                if (ptr1 == 0 || ptr1 > 0x7FFFFFFF) return "";
                uint ptr2 = Memory.ReadUInt32(ptr1 + Offsets.UnitNamePtr2);
                if (ptr2 == 0 || ptr2 > 0x7FFFFFFF) return "";
                return Memory.ReadString(ptr2, 64);
            }
            catch { return ""; }
        }
    }

    // --- Позиция ---
    public float X => Memory.ReadFloat(BaseAddress + Offsets.UnitPositionX);
    public float Y => Memory.ReadFloat(BaseAddress + Offsets.UnitPositionY);
    public float Z => Memory.ReadFloat(BaseAddress + Offsets.UnitPositionZ);
    public float Facing => Memory.ReadFloat(BaseAddress + Offsets.UnitRotation);

    public float DistanceTo(WowUnit other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        float dz = Z - other.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>2D дистанция (без учёта высоты) — WoW использует для проверки дальности спеллов</summary>
    public float DistanceTo2D(WowUnit other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    // --- Ауры (баффы/дебаффы) ---
    public List<int> GetAuraSpellIds()
    {
        var result = new List<int>();
        int auraCount = Memory.ReadInt32(BaseAddress + Offsets.AuraCount);
        uint auraTable = BaseAddress + Offsets.AuraTableBase;

        if (auraCount == -1)
        {
            auraCount = Memory.ReadInt32(BaseAddress + Offsets.AuraTableCountAlt);
            auraTable = Memory.ReadUInt32(BaseAddress + Offsets.AuraTableAlt);
        }

        for (int i = 0; i < auraCount && i < 80; i++)
        {
            int spellId = Memory.ReadInt32(auraTable + (uint)(i * Offsets.AuraSize) + Offsets.AuraSpellId);
            if (spellId > 0)
                result.Add(spellId);
        }

        return result;
    }

    public bool HasAura(int spellId) => GetAuraSpellIds().Contains(spellId);
}
