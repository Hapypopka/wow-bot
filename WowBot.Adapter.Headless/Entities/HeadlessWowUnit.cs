using WowBot.Abstractions.Entities;
using WowBot.HeadlessPoc;

namespace WowBot.Adapter.Headless.Entities;

/// <summary>
/// Адаптер WorldEntity → IWowUnit. Lazy-обёртка: читает из WorldEntity при доступе,
/// чтобы значения отражали последний state из UpdateObject.
/// </summary>
internal class HeadlessWowUnit : IWowUnit
{
    protected readonly WorldEntity _e;
    protected readonly Func<WorldEntity?> _localPlayer;

    public HeadlessWowUnit(WorldEntity entity, Func<WorldEntity?> localPlayer)
    {
        _e = entity;
        _localPlayer = localPlayer;
    }

    public uint BaseAddress => 0;            // headless не имеет процесс памяти
    public ulong Guid => _e.Guid;

    public int Health => _e.Health;
    public int MaxHealth => _e.MaxHealth;
    public float HealthPercent => _e.MaxHealth > 0 ? (float)_e.Health * 100f / _e.MaxHealth : 0f;

    // TODO Phase C: Mana/power через UpdateFields. Сейчас не парсится.
    public int Mana => 0;
    public int MaxMana => 0;
    public float ManaPercent => 0f;

    public int Level => _e.Level;
    public ulong TargetGuid => _e.Target;
    public int NpcId => (int)_e.Entry;

    // TODO Phase C: статусы через UnitFlags + Auras
    public bool IsAlive => _e.Health > 0;
    public bool IsDead => _e.Health <= 0;
    public bool InCombat => false;             // нужен парсинг UNIT_FLAG_IN_COMBAT (0x80000)
    public bool IsCasting => false;            // нужен SMSG_SPELL_START tracking
    public int CastingSpellId => 0;
    public int ChannelingSpellId => 0;

    public float X => _e.X;
    public float Y => _e.Y;
    public float Z => _e.Z;
    public float Facing => _e.Orientation;

    public float BoundingRadius => 1.0f;       // TODO: UNIT_FIELD_BOUNDINGRADIUS
    public float CombatReach => 1.5f;          // TODO: UNIT_FIELD_COMBATREACH

    public string Name => _e.Name ?? string.Empty;

    public float DistanceTo(IWowUnit other)
    {
        var dx = X - other.X; var dy = Y - other.Y; var dz = Z - other.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public float DistanceTo2D(IWowUnit other)
    {
        var dx = X - other.X; var dy = Y - other.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
