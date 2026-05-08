namespace WowBot.Abstractions.Actions;

/// <summary>
/// Команда боту от ротации/AI. Адаптер сам решает как выполнить —
/// memory-режим транслирует в Lua-вызов, headless — в CMSG-пакет.
/// </summary>
public abstract record BotAction
{
    /// <summary>Опциональное человеко-читаемое описание для логов.</summary>
    public string? Comment { get; init; }
}

/// <summary>Ничего не делать в этом тике. Возвращается ротацией если все условия не выполнены.</summary>
public sealed record NoopAction(string? Reason = null) : BotAction;

/// <summary>Подождать указанное время прежде чем делать следующую попытку (например, ждём GCD).</summary>
public sealed record WaitAction(TimeSpan Duration) : BotAction;

/// <summary>Каст спелла. Если Target=null — на текущий target/себя в зависимости от спелла.</summary>
public sealed record CastSpellAction(int SpellId, ulong? Target = null) : BotAction;

/// <summary>Каст AoE по точке на земле (Hurricane, Volley, Death and Decay, Blizzard, etc).</summary>
public sealed record CastGroundAction(int SpellId, float X, float Y, float Z) : BotAction;

/// <summary>Использовать предмет из инвентаря (потионы, зелья, эликсиры).</summary>
public sealed record UseItemAction(int ItemId) : BotAction;

/// <summary>Установить таргет.</summary>
public sealed record SetTargetAction(ulong Guid) : BotAction;

/// <summary>Снять таргет.</summary>
public sealed record ClearTargetAction() : BotAction;

/// <summary>Двигаться к точке (через NavQuery pathfinding).</summary>
public sealed record MoveToAction(float X, float Y, float Z) : BotAction;

/// <summary>Остановить движение.</summary>
public sealed record StopMovementAction() : BotAction;

/// <summary>Авто-атака по таргету (CMSG_ATTACKSWING).</summary>
public sealed record AttackTargetAction(ulong TargetGuid) : BotAction;

/// <summary>Прекратить авто-атаку.</summary>
public sealed record StopAttackAction() : BotAction;

/// <summary>Взаимодействие с объектом (NPC, GameObject — лут, разговор, дверь).</summary>
public sealed record InteractAction(ulong Guid) : BotAction;

/// <summary>Лут трупа (после Interact на трупе).</summary>
public sealed record LootAction(ulong CorpseGuid) : BotAction;

/// <summary>Команда питу (для Hunter/Warlock/DK ghoul).</summary>
public sealed record PetAction(PetCommand Command, ulong? Target = null) : BotAction;

public enum PetCommand
{
    Attack,
    Follow,
    Stay,
    Passive,
    Defensive,
    Aggressive,
    SpecialAbility,   // Felguard Cleave, Felhunter Spell Lock, etc.
    Dismiss,
    Summon,
}

/// <summary>Отправить чат-сообщение.</summary>
public sealed record ChatAction(ChatType Type, string Message, string? WhisperTo = null) : BotAction;

public enum ChatType
{
    Say,
    Yell,
    Party,
    Guild,
    Officer,
    Whisper,
    Channel,
}
