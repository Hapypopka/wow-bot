using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

/// <summary>
/// Реестр ротаций. Находит нужную ротацию по классу/спеку.
/// Все ротации регистрируются здесь.
/// </summary>
public static class RotationRegistry
{
    private static readonly List<ICombatRotation> _rotations = new();

    static RotationRegistry()
    {
        // Регистрация всех ротаций
        _rotations.Add(new WarriorRotation());
        _rotations.Add(new PaladinRotation());
        _rotations.Add(new HunterRotation());
        _rotations.Add(new RogueRotation());
        _rotations.Add(new PriestRotation());
        _rotations.Add(new DeathKnightRotation());
        _rotations.Add(new ShamanRotation());
        _rotations.Add(new MageRotation());
        _rotations.Add(new WarlockRotation());
        _rotations.Add(new DruidRotation());
    }

    /// <summary>Найти ротацию по классу и спеку</summary>
    public static ICombatRotation? Find(string playerClass, string? specName = null)
    {
        return _rotations.FirstOrDefault(r => r.IsMatch(playerClass, specName));
    }

    /// <summary>Все зарегистрированные ротации</summary>
    public static IReadOnlyList<ICombatRotation> All => _rotations;
}
