---
title: "Решение: Headless архитектура — Ports & Adapters"
updated: 2026-05-09
tags: [decision, architecture, headless]
---

# Headless архитектура — Ports & Adapters

## Контекст

Существующий wow-bot работает через **WoW.exe + injected DLL + Lua API** (`UnitHealth`, `CastSpellByName`, etc.). Это требует запущенный клиент с GUI, ~0.5–1 GB RAM на бот, привязка к Windows-машине.

В Mae 2026 Phase 4.3 был сделан headless POC ([[headless-overview]]) — бот без WoW.exe через сетевой протокол. Прорыв: SRP6 + Warden handshake (через MITM-захват pinned response) + UpdateObject + heartbeat работают, бот заходит в мир WoWCircle x100 и держит коннект 15+ минут.

**Цель проекта:** «10-й игрок» в 5-man подземельях — бот-помощник для группы когда не хватает согильдейца. Запускается одной командой, играет полноценно (танк/хил/dps по ситуации).

## Проблема

Текущий код в `WowBot.Core` привязан к `IGameHook.ExecuteLua(...)` и memory-режиму:
- Ротации возвращают **Lua-строки** (`ICombatRotation.GetFullScript()`)
- Все действия идут через Lua (CastSpellByName, UseSoulshard, etc.)
- `IObjectManager` имплементирован только через `MemoryReader`

Чтобы headless-бот мог играть как полноценный — нужен **новый транспорт без ломки текущего бота**.

## Решение

**Hexagonal Architecture (Ports & Adapters)** с двумя реализациями интерфейсов:

```
Domain Core (Combat, BuffManager, BotEngine, Rotations)
        │
        ▼ (использует только ports)
Ports: IObjectManager, IGameActions, INavigation, ICombatRotation
        ▲
        │ (две реализации)
   ┌────┴─────┐
Memory+Lua    Headless
adapter        adapter
   ▲              ▲
   │              │
EndSceneHook   TCP+RC4+UpdateObject
+ Lua + ASM    (HeadlessPoc)
```

Ядро не знает откуда приходят данные — работает только через интерфейсы. Адаптеры реализуют их по-разному.

## Ключевые архитектурные решения

### 1. ICombatRotation эволюция: Lua → Action-based

**Было:**
```csharp
public interface ICombatRotation {
    string GetFullScript();  // Lua-строка
}
```

**Будет:**
```csharp
public interface ICombatRotation {
    BotAction NextAction(IGameState state);  // CastSpell/Move/UseItem/Wait
}
```

Адаптер сам решает как выполнить `BotAction`:
- Memory+Lua adapter → транслирует в `Cast(spell)` Lua-вызов
- Headless adapter → отправляет `CMSG_CAST_SPELL`

**Почему:** убираем привязку к Lua-строкам. Один и тот же ротейшн работает в обоих режимах.

### 2. Новый интерфейс IGameActions

```csharp
public interface IGameActions {
    Task CastSpell(int spellId, ulong? target = null);
    Task UseItem(int itemId);
    Task SetTarget(ulong guid);
    Task Interact(ulong guid);
    Task Loot(ulong corpseGuid);
    Task SendChat(ChatType type, string msg);
    Task MoveTo(float x, float y, float z);
}
```

**Почему:** сейчас все действия через `IGameHook.ExecuteLua(...)`. Это удобно для Lua-режима, но не работает для headless. Явный интерфейс действий нужен в обоих случаях.

### 3. Behavior Tree для боссов и dungeon-нав, scripted pipeline для линейных ротаций

| Логика | Подход | Источник |
|---|---|---|
| Линейная ротация (priority list) | scripted (как сейчас, но в C#) | как у нас сейчас в Lua |
| Босс-механики, dungeon nav | Behavior Tree | порт `AmeisenBotX.BehaviorTree` (MIT) |
| Реактивные триггеры (interrupt, dispel) | TriggerEngine + reactor pattern | playerbots/cmangos style |

**Почему гибрид:** BT идеален для древовидных решений (если танк жив, иди следующий пак, иначе ждать рез), но избыточен для приоритет-листа из 8 спеллов.

**Про чужой код:** берём `AmeisenBotX.BehaviorTree` (MIT) как стартовую базу. Понимаем что потом возможно потребуется адаптация под наши нужды (свои узлы, свой `OngoingNode` поведение, дебаг). Сейчас экономим время старта — лучше работающий бот через 5 недель чем «правильная» BT-библиотека через 7.

### 4. Один процесс, N инстансов ботов

Для headless **все боты живут в одном процессе** — N TCP-коннектов, общий координатор:

```
WowBot.Headless.exe (один процесс)
  ├─ Bot 1 (Tank)    ← TCP к WoWCircle
  ├─ Bot 2 (Healer)  ← TCP
  ├─ Bot 3 (DPS)     ← TCP
  └─ Coordinator (общий state, role assignment)
```

Это **проще чем memory-режим** (где Hivemind через IPC между процессами). Иногда не хватает 1 человека — иногда 3-5. Архитектура должна с самого начала поддерживать N ботов, не один. Memory-Hivemind остаётся для текущих 21+ персонажей через WoW.exe.

### 5. Профили данжей как код, не данные

Профиль данжа = C# класс с `DungeonNode[]` + `BossTactic`. Это позволяет сложные тактики (за-фейзы, респавны, escape sequences) которые не описать декларативным JSON.

### 6. Адаптеры — отдельные сборки

```
WowBot.Adapter.Memory/      (рефакторинг текущего кода)
WowBot.Adapter.Headless/    (новый, на основе HeadlessPoc)
```

`WowBot.Core` ссылается ТОЛЬКО на `WowBot.Abstractions`. Адаптеры подключаются в entry-point проектах:
- `WowBot.Injector` (memory mode) → `Adapter.Memory`
- `WowBot.Headless` (headless mode, новый) → `Adapter.Headless`

## Что переиспользуется (~70%)

| Компонент | Изменения |
|---|---|
| `WowBot.Abstractions` | расширить (`IGameActions`, action types) |
| `BotEngine`, `CombatExecutor`, `BuffManager`, `CombatHelper` | работают через `IGameActions`, не `ExecuteLua` |
| Ротации классов | эволюция: возвращают `BotAction` |
| `RotationRegistry` | без изменений |
| `Hivemind`, `SlaveController` | без изменений (IPC, не зависит от транспорта) |
| `Navigation`/`NavQuery` | уже headless-friendly |
| `BossEngine`, `BossTactics` | без изменений |
| `WowBot.HeadlessPoc` | становится `WowBot.Adapter.Headless` |

## Альтернативы рассмотренные

### Альтернатива 1: Полностью отдельный headless-бот
Завести `WowBot.Headless` как независимое решение, не трогая текущий `WowBot.Core`.

**Плюсы:** не ломаем работающее. Быстрее старт.
**Минусы:** дублирование ротаций, баг-фиксы в двух местах, расходимся по фичам. Через год получим два разных бота.

**Отвергнуто** — техдолг превысит экономию.

### Альтернатива 2: Server-side бот (NPCBots/playerbots)
Бот живёт на сервере как `Creature`, не клиент.

**Плюсы:** не зависит от Warden, всегда в синхроне. **Минусы:** нужен доступ к серверу. Мы играем на чужом WoWCircle — **неприменимо**.

**Отвергнуто** — нет доступа к серверу.

### Альтернатива 3: BehaviorTree для всего
Все ротации через BT.

**Плюсы:** единая модель. **Минусы:** избыточно для линейных ротаций (Ret Pala = priority list из 8 спеллов, не дерево).

**Отвергнуто** — гибрид лучше.

## Стоимость и риски

### Стоимость (см. [[headless-roadmap]])
- Phase A (Foundation): 2-3 дня
- Phase B (Action layer): 3-4 дня
- Phase C (Combat MVP, Ret Pala): неделя
- Phase D (Dungeon engine): неделя+
- Phase E (Boss tactics, MVP «10-й игрок»): 2 недели
- **Итого до MVP:** ~5-6 недель работы

### Риски
1. **Warden обновление** — pinned response сломается, надо заново MITM-захватывать. Митигация: автоматизировать процесс захвата, документировать.
2. **WoWCircle кастомные опкоды** — может быть несовместимость со стандартом TC. Уже видели в Phase 4.3. Митигация: быстрое логирование незнакомых опкодов, инкрементальная адаптация.
3. **Server-side restrictions** — login-from-new-IP flag блокирует часть действий. Не блокирует chat/movement/casting/loot — для PvE-бота некритично. Митигация: логиниться через VPN привязанный к одному IP.
4. **CHEAT_CHECKS warning** — сервер ругается, но не кикает за 15+ минут. Долгосрочно может ужесточить. Митигация: эмуляция модуля как Phase F+ (отложенный).

## Реализация: см.

- [[headless-overview]] — общая архитектура headless
- [[headless-roadmap]] — план по фазам
- [[ports-and-adapters]] — детали интерфейсов
- [[project_headless_mitm_warden]] (memory) — Warden handshake обход

## Файлы затронутые (на момент решения)

Решение ещё не реализовано в коде. Существующее:
- `WowBot.Abstractions/` — все интерфейсы (расширить)
- `WowBot.Core/Game/` — domain logic (работают через интерфейсы)
- `tools/HeadlessPoc/` — будет рефакториться в `WowBot.Adapter.Headless`

## Связи

- [[headless-overview]]
- [[headless-roadmap]]
- [[ports-and-adapters]]
- [[combat-system]]
- [[hivemind]]
- [[wowcircle-quirks]]
