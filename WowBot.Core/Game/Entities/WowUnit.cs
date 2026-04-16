using WowBot.Abstractions.Entities;
using WowBot.Core.Memory;

namespace WowBot.Core.Game.Entities;

public class WowUnit : WowObject, IWowUnit
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

    /// <summary>Owner chain для петов/тотемов/гулей/элементалей</summary>
    public ulong CharmedBy => ReadDescriptorGuid(Offsets.UnitFieldCharmedBy);
    public ulong SummonedBy => ReadDescriptorGuid(Offsets.UnitFieldSummonedBy);
    public ulong CreatedBy => ReadDescriptorGuid(Offsets.UnitFieldCreatedBy);

    /// <summary>GUID хозяина юнита (пет/тотем/гуль) — 0 если нет</summary>
    public ulong OwnerGuid
    {
        get
        {
            ulong g = SummonedBy;
            if (g != 0) return g;
            g = CreatedBy;
            if (g != 0) return g;
            return CharmedBy;
        }
    }

    public bool IsAlive => Health > 0;
    public bool IsDead => Health <= 0;

    /// <summary>NPC ID (OBJECT_FIELD_ENTRY, дескриптор 0x06)</summary>
    public int NpcId => ReadDescriptorInt(0x06);
    public int UnitFlags => ReadDescriptorInt(Offsets.UnitFieldFlags);
    public bool InCombat => (UnitFlags & 0x80000) != 0; // UNIT_FLAG_IN_COMBAT = 0x80000

    // --- UnitFlags флаги атакуемости ---
    public const uint UNIT_FLAG_NON_ATTACKABLE    = 0x00000002;
    public const uint UNIT_FLAG_NOT_ATTACKABLE_1  = 0x00000080;
    public const uint UNIT_FLAG_PACIFIED          = 0x00020000;
    public const uint UNIT_FLAG_NOT_SELECTABLE    = 0x02000000;

    /// <summary>Юнит не может быть атакован (critter, NPC-квестодатель, неуязвимый)</summary>
    public bool IsNotAttackable =>
        ((uint)UnitFlags & (UNIT_FLAG_NON_ATTACKABLE | UNIT_FLAG_NOT_ATTACKABLE_1 | UNIT_FLAG_NOT_SELECTABLE)) != 0;

    // --- Хитбокс ---
    public float BoundingRadius => ReadDescriptorFloat(Offsets.UnitFieldBoundingRadius);
    public float CombatReach => ReadDescriptorFloat(Offsets.UnitFieldCombatReach);

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

    public float DistanceTo(IWowUnit other)
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

    public float DistanceTo2D(IWowUnit other)
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

        if (auraCount <= 0 || auraCount > 80) return result;

        // Батч: читаем весь блок аур за один syscall
        int totalBytes = auraCount * (int)Offsets.AuraSize;
        byte[] block = Memory.ReadBytes(auraTable, totalBytes);

        for (int i = 0; i < auraCount; i++)
        {
            int offset = i * (int)Offsets.AuraSize + (int)Offsets.AuraSpellId;
            if (offset + 4 > block.Length) break;
            int spellId = BitConverter.ToInt32(block, offset);
            if (spellId > 0)
                result.Add(spellId);
        }

        return result;
    }

    public bool HasAura(int spellId) => GetAuraSpellIds().Contains(spellId);
}
