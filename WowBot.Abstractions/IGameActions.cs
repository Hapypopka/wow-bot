using WowBot.Abstractions.Actions;

namespace WowBot.Abstractions;

/// <summary>
/// Действия в игре. Реализуется адаптером:
/// - Memory+Lua: транслирует в Lua-вызов через IGameHook.ExecuteLua
/// - Headless: формирует CMSG-пакет и шлёт на сервер
///
/// Все методы async — действие может требовать ack от сервера (CastSpell ждёт SMSG_SPELL_GO/SPELL_FAILED).
/// </summary>
public interface IGameActions
{
    /// <summary>Каст спелла. Target=null означает "на текущий таргет" или "на себя" в зависимости от типа спелла.</summary>
    Task CastSpell(int spellId, ulong? target = null, CancellationToken ct = default);

    /// <summary>Ground-targeted AoE (Hurricane, Volley, Death and Decay, Blizzard).</summary>
    Task CastSpellOnGround(int spellId, float x, float y, float z, CancellationToken ct = default);

    /// <summary>Использовать предмет (потион, эликсир, расходник).</summary>
    Task UseItem(int itemId, CancellationToken ct = default);

    /// <summary>Установить таргет.</summary>
    Task SetTarget(ulong guid, CancellationToken ct = default);

    /// <summary>Снять таргет.</summary>
    Task ClearTarget(CancellationToken ct = default);

    /// <summary>Авто-атака по таргету.</summary>
    Task AttackTarget(ulong guid, CancellationToken ct = default);

    /// <summary>Прекратить авто-атаку.</summary>
    Task StopAttack(CancellationToken ct = default);

    /// <summary>Взаимодействие с NPC/GameObject (нажать на дверь, поговорить, открыть, начать лут).</summary>
    Task Interact(ulong guid, CancellationToken ct = default);

    /// <summary>Лут трупа после открытия лут-окна.</summary>
    Task Loot(ulong corpseGuid, CancellationToken ct = default);

    /// <summary>Чат-сообщение.</summary>
    Task SendChat(ChatType type, string message, string? whisperTo = null, CancellationToken ct = default);

    /// <summary>Двигаться к точке (через NavQuery pathfinding или CTM в memory-режиме).</summary>
    Task MoveTo(float x, float y, float z, CancellationToken ct = default);

    /// <summary>Остановить движение.</summary>
    Task StopMovement(CancellationToken ct = default);

    /// <summary>Команда питу (для Hunter/Warlock/DK Unholy).</summary>
    Task SendPetCommand(PetCommand command, ulong? target = null, CancellationToken ct = default);
}
