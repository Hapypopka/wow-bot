using WowBot.Abstractions;

namespace WowBot.Core.Game;

/// <summary>
/// Источник команд от UI (кнопки, горячие клавиши).
/// UI вызывает EnqueueCommand(), BotEngine обрабатывает в Tick().
/// </summary>
public class UserCommandSource : CommandSourceBase
{
    public UserCommandSource() : base("UI") { }

    // Хелперы для удобства вызова из UI
    public void Follow(string targetName) =>
        EnqueueCommand(new BotCommand { Type = BotCommandType.Follow, TargetName = targetName, Source = "UI" });

    public void Attack(string targetName) =>
        EnqueueCommand(new BotCommand { Type = BotCommandType.Attack, TargetName = targetName, Source = "UI" });

    public void Stop() =>
        EnqueueCommand(new BotCommand { Type = BotCommandType.Stop, Source = "UI" });

    public void StartRotation() =>
        EnqueueCommand(new BotCommand { Type = BotCommandType.StartRotation, Source = "UI" });

    public void StopRotation() =>
        EnqueueCommand(new BotCommand { Type = BotCommandType.StopRotation, Source = "UI" });

    public void StartBuffs() =>
        EnqueueCommand(new BotCommand { Type = BotCommandType.StartBuffs, Source = "UI" });

    public void StopBuffs() =>
        EnqueueCommand(new BotCommand { Type = BotCommandType.StopBuffs, Source = "UI" });
}

/// <summary>
/// Источник команд от Hivemind (мастер через addon messages).
/// Hivemind.ExecuteSlaveCommand() теперь может ставить BotCommand в очередь.
/// </summary>
public class HivemindCommandSource : CommandSourceBase
{
    public HivemindCommandSource() : base("Hivemind") { }
}
