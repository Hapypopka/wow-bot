---
title: Navigation
updated: 2026-04-19
tags: [architecture, navigation, movement]
---

# Navigation

## Файлы
- `WowBot.Core/Game/Navigation.cs` — углы, повороты, движение
- `WowBot.Core/Game/ClickToMove.cs` — CTM через память
- `WowBot.Core/Game/CombatPositioning.cs` — MoveBehind / RangedPos
- `WowBot.Core/Navigation/NavEngine.cs` — патхфайндинг
- `WowBot.Core/Navigation/AmeisenNavClient.cs` — TCP клиент

## Click-to-Move (CTM)

Память по адресу `0x00CA11D8`:
```
+0x1C = Action (0x4=MoveTo, 0xD=Stop)
+0x8C = X, +0x90 = Y, +0x94 = Z
+0x0C = Precision
```

**Приоритет:** EndSceneHook.CallClickToMove() → fallback на прямую запись в память.

### Native функции клиента (через EndSceneHook)

| Адрес | Функция | Flag | Использование |
|-------|---------|------|---------------|
| `0x00727400` | `CGPlayer_C__ClickToMove(clickType, guid, pos, precision)` | 6 | `_ctm.MoveTo` + `Navigation.FaceInstant` (clickType=1) |
| `0x0072B3A0` | `PlayerClickToMoveStop(playerBase)` | 7 | `_ctm.NativeStop()` — шлёт MSG_MOVE_STOP серверу с актуальной позицией |

**NativeStop важен** потому что прямая запись `CTM_Action=0xD` локальна — сервер не знает. Aura на сервере продолжает тикать по старой позиции. Native вызов отправляет пакет → server sync мгновенный. Используется в `TryAoEAvoidance` при выходе из зоны.

### Потенциально полезные native функции (не используем)

Из AmeisenBot offsets:
- `0x7A3B70` — **Traceline** (raycast, LoS checks) — TODO: не пуллить через стены
- `0x71B7F0` — IsOutdoors (для маунта/хартстоуна)
- `0x731260` — UnitOnRightClick (native Interact, стабильнее Lua)
- `0x711140` — GameobjectOnRightClick
- `0x524BF0` — SetTarget (быстрее Lua TargetUnit)
- `0x72EA50` — UnitSetFacing (альтернатива clickType=1)

## Navigation.cs — ключевые методы

| Метод | Что делает |
|-------|-----------|
| `FaceInstant(player, target)` | Мгновенный поворот через CTM (clickType=1) |
| `IsPlayerStanding()` | 3 тика без движения (>0.3 units) = стоит |
| `StartMoveForward()` | Lua `MoveForwardStart()` |
| `WriteFacing(unit, angle)` | Прямая запись в память `BaseAddress + UnitRotation` |

## CombatPositioning — MoveBehind

Логика `TryMoveBehind()`:
1. Только мили DPS (не танк, не хил)
2. Дистанция <= melee range + 3yd
3. Интервал: Rogue/Feral = 450ms, остальные = 750ms
4. Если уже за спиной — пауза 1-1.5s
5. Если нет — вычисляем точку 180° от facing таргета, CTM туда

**`IsMovingBehind = true`** → CombatExecutor пропускает ground AoE и прочее, только ротация.

## NavEngine — патхфайндинг

TCP сервер AmeisenNavigation на порту 47110 (localhost).
- `GetPath()` → массив Vector3 waypoints
- `Tick()` → каждые 150ms двигаемся к следующему waypoint (< 5yd = next)
- Lookahead: пропускаем 1 waypoint вперёд для сглаживания
- PathFlags: SmoothCatmullRom + ValidateMoveAlongSurface

## ObjectManager

Файл: `WowBot.Core/Game/ObjectManager.cs` (160 строк)

Связный список объектов из памяти WoW:
```
ClientConnection (0x00C79CE0) → +0x2ED0 → ObjectManager
  → +0xAC = FirstObject → +0x3C = NextObject (linked list)
  → +0x14 = Type (3=Unit, 4=Player, 6=DynObject)
```

**Коллекции:** Units, Players, DynObjects, Objects. Max 5000 объектов.

**Entity классы:**
- `WowUnit` — HP, Mana, Target, Position, CastingSpell, Auras, CombatReach
- `WowPlayer` extends WowUnit
- `WowDynObject` — X/Y/Z, SpellId, Radius (для AoE Avoidance)

## Связи
- [[lua-engine]] — CTM и Lua через EndSceneHook
- [[combat-system]] — CombatPositioning используется в CombatExecutor
- [[aoe-system]] — DynObjects для AoE Avoidance
