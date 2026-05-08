namespace WowBot.Abstractions;

/// <summary>Роль персонажа в группе. Определяет приоритеты и поведение в данжах/рейдах.</summary>
public enum Role
{
    Unknown = 0,
    Tank,
    Healer,
    MeleeDps,
    RangedDps,
}

/// <summary>Спек класса WoW 3.3.5a. Все 30 специализаций (10 классов × 3 спека).</summary>
public enum Specialization
{
    Unknown = 0,

    // Warrior
    WarriorArms, WarriorFury, WarriorProtection,
    // Paladin
    PaladinHoly, PaladinProtection, PaladinRetribution,
    // Hunter
    HunterBeastMastery, HunterMarksmanship, HunterSurvival,
    // Rogue
    RogueAssassination, RogueCombat, RogueSubtlety,
    // Priest
    PriestDiscipline, PriestHoly, PriestShadow,
    // Death Knight
    DeathKnightBlood, DeathKnightFrost, DeathKnightUnholy,
    // Shaman
    ShamanElemental, ShamanEnhancement, ShamanRestoration,
    // Mage
    MageArcane, MageFire, MageFrost,
    // Warlock
    WarlockAffliction, WarlockDemonology, WarlockDestruction,
    // Druid
    DruidBalance, DruidFeralCat, DruidFeralBear, DruidRestoration,
}
