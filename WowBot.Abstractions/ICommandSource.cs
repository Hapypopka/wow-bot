using System.Collections.Concurrent;

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

    /// <summary>Поставить команду в очередь</summary>
    void EnqueueCommand(BotCommand command);
}

/// <summary>Команда для бота — единая для solo и slave</summary>
public record BotCommand
{
    public BotCommandType Type { get; init; }

    /// <summary>Имя цели (для Follow, Attack — имя мастера/моба)</summary>
    public string TargetName { get; init; } = "";

    /// <summary>GUID цели</summary>
    public ulong TargetGuid { get; init; }

    /// <summary>Координаты (для MoveTo, Scatter)</summary>
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }

    /// <summary>Источник команды (для логов)</summary>
    public string Source { get; init; } = "";
}

/// <summary>Типы команд — единые для solo и slave</summary>
public enum BotCommandType
{
    // Движение
    Stop,           // Полный стоп
    Follow,         // Следовать за целью
    MoveTo,         // Бежать в точку

    // Бой
    Attack,         // Ассист + атака
    Auto,           // Авто-режим (follow + auto-assist)

    // Тоглы
    StartRotation,
    StopRotation,
    StartBuffs,
    StopBuffs,

    // Hivemind-специфика (пока)
    Scatter,        // Разбежаться
    Wipe,           // Хилеры стоп
    AutoToggleFollow,
    AutoToggleAttack,
    Interact,       // Interact с NPC
    Gossip,         // Gossip option
}

/// <summary>
/// Базовая реализация ICommandSource через ConcurrentQueue.
/// Наследуется UserCommandSource и HivemindCommandSource.
/// </summary>
public class CommandSourceBase : ICommandSource
{
    private readonly ConcurrentQueue<BotCommand> _queue = new();

    public string SourceName { get; }
    public bool HasPendingCommand => !_queue.IsEmpty;

    public CommandSourceBase(string sourceName)
    {
        SourceName = sourceName;
    }

    public BotCommand? DequeueCommand()
    {
        return _queue.TryDequeue(out var cmd) ? cmd : null;
    }

    public void EnqueueCommand(BotCommand command)
    {
        _queue.Enqueue(command);
    }

    /// <summary>Очистить очередь</summary>
    public void Clear()
    {
        while (_queue.TryDequeue(out _)) { }
    }
}
