---
title: Headless Roadmap
updated: 2026-05-09
tags: [architecture, headless, roadmap]
---

# Headless Roadmap

План реализации архитектуры [[2026-05-09-headless-architecture]] до MVP «10-й игрок в 5-man подземельях».

## Phase A — Foundation (2-3 дня)

**Цель:** интерфейсы и walking skeleton headless-адаптера.

### Задачи
- [ ] Расширить `WowBot.Abstractions`:
  - Добавить `IGameActions.cs`
  - Добавить `Actions/BotAction.cs` (records: Cast/Move/UseItem/Wait/Noop/SetTarget)
  - Добавить `Role.cs` (Tank/Heal/MeleeDPS/RangedDPS), `Specialization.cs`
- [ ] Эволюционировать `ICombatRotation`:
  - Добавить `BotAction NextAction(IGameState state)`
  - Старый `GetFullScript()` оставить с `[Obsolete]` для обратной совместимости
- [ ] Создать solution-проекты:
  - `WowBot.Adapter.Memory/` (рефакторинг существующего кода)
  - `WowBot.Adapter.Headless/` (рефакторинг HeadlessPoc)
  - `WowBot.Headless/` — новый entry-point CLI
- [ ] Реализовать `HeadlessObjectManager : IObjectManager` поверх `WorldState`
- [ ] Smoke-test: бот заходит в мир, видит себя через `IObjectManager.LocalPlayer`

### Критерий завершения
В headless `objectManager.LocalPlayer.Health == [реальное HP]`. UpdateObject заполняет Entity снапшот.

---

## Phase B — Action layer (3-4 дня)

**Цель:** базовые действия в обоих режимах через `IGameActions`.

### Задачи
- [ ] `CMSG_CAST_SPELL` (opcode 0x12E) — структура: cast count, spell id, cast flags, target packed GUID
- [ ] `CMSG_SET_SELECTION` (opcode 0x13D)
- [ ] `CMSG_USE_ITEM` (opcode 0xAB)
- [ ] `CMSG_INTERACT_OBJECT` (opcode 0xB1)
- [ ] `CMSG_LOOT` (opcode 0x15D), `CMSG_LOOT_RELEASE` (0x15F)
- [ ] `CMSG_ATTACKSWING` (0x141), `CMSG_ATTACKSTOP` (0x142)
- [ ] `MSG_MOVE_SET_FACING` для поворота
- [ ] `HeadlessGameActions : IGameActions` — реализация всех методов
- [ ] `MemoryGameActions : IGameActions` — обёртка над `IGameHook.ExecuteLua` (трансляция `BotAction` → Lua-вызов)
- [ ] Smoke-test: бот в headless кастует на себя бафф (Blessing of Wisdom self-cast)

### Критерий завершения
Один и тот же тестовый скрипт «кастуй BoW на себя, потом /say done» работает в обоих режимах через `IGameActions`.

---

## Phase C — Combat MVP (Demonology Warlock, ~неделя)

**Цель:** одна полная ротация работает в headless.

**Тестовый чар:** Узянбаева (Warlock 80, map=530 Outland). На WoWCircle x100 уже залогинена, доступна через destroyer.cool. Demonology — относительно простая ротация (Immolate + Corruption + Curse + Felguard в пете), хорошо подходит для первого порта.

### Задачи
- [ ] Расширить `UpdateObjectParser`:
  - Auras (UNIT_FIELD_AURA[56], UNIT_FIELD_AURALEVELS, UNIT_FIELD_AURAFLAGS)
  - Power types (mana — для Warlock + soul shards в инвентаре)
  - Casting (UNIT_FIELD_CHANNEL_SPELL, проверять CastId)
  - Pet info (PLAYER_FIELD_PET_NAME_TIMESTAMP, GUID пета через CURRENT_PET)
- [ ] Spell tracking:
  - `SMSG_SPELL_START` (0x131), `SMSG_SPELL_GO` (0x132), `SMSG_SPELL_FAILED` (0x133)
  - `SMSG_SPELL_COOLDOWN` (0x134) → локальный CooldownTracker
  - GCD tracker (1.5с минус хейст)
- [ ] Портировать `WarlockRotation` (Demo) на `BotAction`-based:
  - Текущий код в `WowBot.Core/Game/Rotations/WarlockRotation.cs` (Lua-генератор → priority list в C#)
  - Spell IDs из `SpellDatabase.json`
  - Pet management (Felguard summon, follow, attack, special)
- [ ] `CombatEngine`: tick 150мс, target selection, выполнение `NextAction()`
- [ ] Smoke-test: Узянбаева в open-world Шаттрата бьёт моба ротацией Demo Lock

### Критерий завершения
Бот забивает уровневого моба за разумное время, использует все основные спеллы Demo (Immolate, Corruption, Incinerate, Curse of Agony, Demonic Empowerment, Felguard auto-attack + Cleave/Anguish).

### Почему Demo Lock первым
- Тестовый аккаунт уже есть (destroyer.cool/Узянбаева) и не критичен — можно сломать
- Простая линейная ротация без сложных приоритетов
- Pet management покроем сразу — переиспользуется для Hunter позже
- DoT-based тестирует UpdateFields auras (важная часть парсера)

---

## Phase D — Dungeon Engine (неделя+)

**Цель:** бот ходит за лидером по данжу.

### Задачи
- [ ] `IDungeonProfile`, `DungeonNode`, `BossTactic` интерфейсы
- [ ] Один профиль (любой 5-man WotLK с линейной структурой)
- [ ] `DungeonEngine`: нав по нодам через `IMovementEngine`
- [ ] `GroupEngine`:
  - Опрос party listа (`SMSG_GROUP_LIST` 0x7D)
  - Detection лидера, role assignment
  - Follow-leader логика когда бот не лидер
  - Coordinator для нескольких ботов в одном процессе (распределение ролей: tank/heal/dps между N ботами)
- [ ] BehaviorTree библиотека (порт `AmeisenBotX.BehaviorTree` или ~300-строчный свой)

### Критерий завершения
Бот в группе, ходит за лидером, бьёт моба которого пометили (focus or skull). Не отстаёт, не теряется.

---

## Phase E — Boss Tactics + MVP (~2 недели)

**Цель:** «10-й игрок» работает в одном данже.

### Задачи
- [ ] `BossTactic` API: pre-pull, phase 1, phase 2, post-fight
- [ ] BossTactic для каждого босса выбранного данжа
- [ ] `TriggerEngine` для реактивных механик (interrupt, dispel)
- [ ] Smoke-test: лидер-человек заводит бота в данж, проходит до конца с ботом

### Критерий завершения
Бот успешно проходит выбранный 5-man данж в составе живой группы (4 человека + бот).

---

## Phase F+ — Расширение (по желанию)

- Больше классов (одного DPS-каста, одного DPS-мили, хилера, танка → 5 ролей покрыты)
- Больше данжей (приоритет — heroic ICC: Forge of Souls, Pit of Saron, Halls of Reflection)
- Эмуляция Warden модуля (если restrictions начнут мешать)
- Multibox (5 ботов = вся группа без людей) — далёкая цель

---

## Принципы исполнения

- **Не ломать memory-режим.** Существующий wow-bot работает через `WowBot.Injector` — оставляем рабочим. Headless — параллельная ветка.
- **Phase-by-phase, не all-at-once.** Каждая фаза имеет smoke-test и критерий завершения.
- **Документировать решения.** Каждое архитектурное решение → отдельный ADR в `wiki/decisions/`.
- **Маленькие коммиты.** Один коммит = одна задача из roadmap. Атомарно.

## Связи

- [[2026-05-09-headless-architecture]] — обоснование
- [[headless-overview]] — слои и ports
- [[ports-and-adapters]] — детальные интерфейсы (TBD)
