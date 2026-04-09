namespace WowBot.Abstractions.Entities;

/// <summary>Юнит WoW (NPC, моб, петы)</summary>
public interface IWowUnit : IWowObject
{
    // Характеристики
    int Health { get; }
    int MaxHealth { get; }
    float HealthPercent { get; }
    int Mana { get; }
    int MaxMana { get; }
    float ManaPercent { get; }
    int Level { get; }
    ulong TargetGuid { get; }
    int NpcId { get; }

    // Состояние
    bool IsAlive { get; }
    bool IsDead { get; }
    bool InCombat { get; }
    bool IsCasting { get; }
    int CastingSpellId { get; }
    int ChannelingSpellId { get; }

    // Позиция
    float X { get; }
    float Y { get; }
    float Z { get; }
    float Facing { get; }

    // Хитбокс
    float BoundingRadius { get; }
    float CombatReach { get; }

    // Имя
    string Name { get; }

    // Дистанция
    float DistanceTo(IWowUnit other);
    float DistanceTo2D(IWowUnit other);
}
