---
title: Headless архитектура — Overview
updated: 2026-05-09
tags: [architecture, headless]
---

# Headless архитектура — Overview

## Что это

Параллельная ветка wow-bot которая работает **без WoW.exe**. Подключается к серверу напрямую как самостоятельный клиент — TCP, RC4, протокол 3.3.5a, UpdateObject. Цель: «10-й игрок» в 5-man подземельях, запуск одной командой.

См. [[2026-05-09-headless-architecture]] для обоснования.

## Слои

```
┌─────────────────────────────────────────────────────────┐
│  CONTROL                                                │
│    WowBot.Headless (new, CLI)  │ WowBot.Injector (UI)   │
├─────────────────────────────────────────────────────────┤
│  ENGINES (что делает бот)                               │
│    DungeonEngine │ CombatEngine │ GroupEngine           │
│    MovementEngine │ TacticEngine │ TriggerEngine        │
├─────────────────────────────────────────────────────────┤
│  DOMAIN CORE (не зависит от транспорта)                 │
│    BotEngine │ CombatExecutor │ BuffManager             │
│    Rotations (ICombatRotation per spec)                 │
│    BehaviorTree │ Strategy/Action/Trigger               │
├─────────────────────────────────────────────────────────┤
│  PORTS (WowBot.Abstractions)                            │
│    IObjectManager │ IGameActions │ INavigation          │
│    IWowUnit/Player │ ICombatRotation                    │
├─────────────────────────────────────────────────────────┤
│  ADAPTERS                                               │
│    WowBot.Adapter.Memory    │ WowBot.Adapter.Headless   │
│    (existing, рефакторинг)  │ (new, на HeadlessPoc)     │
├─────────────────────────────────────────────────────────┤
│  TRANSPORT                                              │
│    EndSceneHook+Lua+ASM     │ TCP+RC4+opcodes+UpdateObj │
└─────────────────────────────────────────────────────────┘
```

## Текущее состояние (Phase 4.3)

Работает как POC:
- SRP6 авторизация через WoWCircle logon
- World server connection (RC4, opcodes)
- Warden handshake пройден через MITM-захваченную pinned response
- UpdateObject парсер (базовые поля: HP, MP, level, position)
- LOGIN_VERIFY_WORLD, NavQuery подключён
- Heartbeat 200мс, держит коннект 15+ минут
- Чат: SAY/YELL/WHISPER (CMSG)
- Движение: MoveForward/MoveTo через CTM-стиль пакеты
- /who, /friend list

См. [[project_headless_mitm_warden]] (memory) для деталей Warden обхода.

## Ports (интерфейсы)

### IObjectManager (расширение)
Уже есть в `WowBot.Abstractions/IObjectManager.cs`. Headless реализует поверх `WorldState` который наполняется из `UpdateObjectParser`.

### IGameActions (новый)
```csharp
public interface IGameActions {
    Task CastSpell(int spellId, ulong? target = null);
    Task CastSpellOnGround(int spellId, float x, float y, float z);
    Task UseItem(int itemId);
    Task SetTarget(ulong guid);
    Task Interact(ulong guid);
    Task Loot(ulong corpseGuid);
    Task SendChat(ChatType type, string msg, string? whisperTo = null);
    Task MoveTo(float x, float y, float z);
    Task StopMovement();
    Task PetAction(PetCommand cmd, ulong? target = null);
    Task AttackTarget(ulong guid);   // CMSG_ATTACKSWING
    Task StopAttack();                // CMSG_ATTACKSTOP
}
```

### ICombatRotation (эволюция)
```csharp
public interface ICombatRotation {
    string Name { get; }
    string WowClass { get; }
    Specialization Spec { get; }
    Role Role { get; }                        // Tank/Heal/MeleeDPS/RangedDPS
    bool HandlesMovement { get; }              // ротация сама управляет позицией
    bool HandlesFacing { get; }                // ротация сама поворачивает

    bool IsMatch(string playerClass, string? specName);
    BotAction NextAction(IGameState state);    // главное
    BotAction NextOutOfCombatAction(IGameState state);
}
```

### BotAction (новый тип)
```csharp
public abstract record BotAction;
public record CastSpellAction(int SpellId, ulong? Target = null) : BotAction;
public record CastGroundAction(int SpellId, float X, float Y, float Z) : BotAction;
public record UseItemAction(int ItemId) : BotAction;
public record MoveToAction(float X, float Y, float Z) : BotAction;
public record SetTargetAction(ulong Guid) : BotAction;
public record WaitAction(TimeSpan Duration) : BotAction;
public record NoopAction : BotAction;
```

## Adapters

### WowBot.Adapter.Memory (рефакторинг текущего)
Реализует `IObjectManager` через `MemoryReader`, `IGameActions` через `IGameHook.ExecuteLua` (трансляция `BotAction` → Lua-вызов).

### WowBot.Adapter.Headless (новый)
Реализует `IObjectManager` через `WorldState` (парсинг UpdateObject), `IGameActions` через CMSG-пакеты.

```csharp
class HeadlessGameActions : IGameActions {
    public Task CastSpell(int spellId, ulong? target) {
        var pkt = BuildCastSpellPacket(spellId, target);
        return _world.SendCmsg(0x12E, pkt);  // CMSG_CAST_SPELL
    }
    // ...
}
```

## Engines

### CombatEngine
Тикается каждые 150мс, выбирает таргет, опрашивает `ICombatRotation.NextAction()`, выполняет через `IGameActions`.

### MovementEngine
Pathfinding через `INavigation` (NavQuery TCP). Преобразует waypoints в последовательность `MoveToAction`.

### DungeonEngine (Phase D)
Загружает `IDungeonProfile`, ведёт бота по `DungeonNode[]`. Делегирует бой `CombatEngine`, навигацию `MovementEngine`.

```csharp
public interface IDungeonProfile {
    uint MapId { get; }
    string Name { get; }
    int GroupSize { get; }                    // 5
    IReadOnlyList<DungeonNode> Nodes { get; }
    IReadOnlyList<BossTactic> Bosses { get; }
}

public record DungeonNode(NodeType Type, float X, float Y, float Z);
public enum NodeType { Move, Pull, UseDoor, WaitForGroup, Boss, Loot }
```

### GroupEngine
Определяет роль бота (Tank/Heal/DPS), watcher лидера, follow-логика (если бот не лидер).

### TriggerEngine
Реактивные правила: «если враг кастит Polymorph → interrupt», «если у танка 3+ Sunder Stack → ждать дамаг ↓». Опрашивается каждый тик параллельно с ротацией.

## Behavior Tree (для боссов и dungeon-нав)

Берём `AmeisenBotX.BehaviorTree` (MIT, ~500 строк) или пишем минимальный:

```csharp
var lichKingPhase1 = new Selector(
    new Sequence(
        new Condition(s => s.HasDebuff("Necrotic Plague")),
        new Action(s => RunFromGroup(s))
    ),
    new Sequence(
        new Condition(s => IsAdding()),
        new Action(s => SwitchToAdds(s))
    ),
    new Action(s => DpsBoss(s))
);
```

См. [[ports-and-adapters]] для детальных интерфейсов.

## Roadmap

См. [[headless-roadmap]].

## Связи

- [[2026-05-09-headless-architecture]] — обоснование
- [[headless-roadmap]] — план по фазам
- [[ports-and-adapters]] — детали интерфейсов
- [[combat-system]] — текущий CombatExecutor
- [[hivemind]] — memory-mode multibox
