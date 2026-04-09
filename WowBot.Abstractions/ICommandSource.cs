namespace WowBot.Abstractions;

/// <summary>
/// Источник команд для бота.
/// Solo: команды от UI / горячих клавиш.
/// Slave: команды от мастера через addon message.
/// Auto: команды от AI (AutoPvE, boss tactics).
///
/// Ключевая идея v2: BotEngine не знает КТО отдал команду.
/// Одна команда → один путь выполнения → работает везде.
/// </summary>
public interface ICommandSource
{
    /// <summary>Название источника (для логов)</summary>
    string SourceName { get; }

    /// <summary>Есть ли ожидающая команда</summary>
    bool HasPendingCommand { get; }

    /// <summary>Забрать следующую команду (null если нет)</summary>
    BotCommand? DequeueCommand();
}

/// <summary>Команда для бота</summary>
public record BotCommand
{
    public BotCommandType Type { get; init; }

    // Для Follow/MoveTo
    public ulong TargetGuid { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }

    // Для Attack
    public ulong AttackTargetGuid { get; init; }
}

/// <summary>Типы команд — единые для solo и slave</summary>
public enum BotCommandType
{
    Stop,
    Follow,
    Attack,
    MoveTo,
    StartRotation,
    StopRotation,
    StartBuffs,
    StopBuffs,
}
