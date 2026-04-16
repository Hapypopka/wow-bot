---
title: Combat System
updated: 2025-04-15
tags: [architecture, combat]
---

# Combat System

## Принцип
Один путь для solo и slave. Весь бой через CombatExecutor — BotEngine только собирает параметры и вызывает.

## CombatExecutor (`WowBot.Core/Game/CombatExecutor.cs`)

Порядок выполнения `ExecuteCombatTick()`:

1. **CombatPositioning** — MoveBehind (мили) / RangedPos (рейнж)
2. **Ground AoE** — Hurricane, Volley (через CastTerrainClick)
3. **Smart Taunt** — для танков, переключение на мобов бьющих хилов
4. **Approach** — slave подход к таргету (CTM)
5. **FaceInstant** — поворот к таргету
6. **Rotation** — Lua скрипт (основная ротация)

## CombatOptions (record)

Параметры собираются в BotEngine через `MakeCombatOptions()`:
- `RotationScript` — Lua строка ротации
- `EnemyCountLua` — `WB_NE`, `WB_NCE`, `WB_NCET`, `WB_AEMIN`
- `SpellFlagsLua` — `WB_S.Key=true/false` для чекбоксов UI
- Флаги: IsMeleeSpec, IsTankSpec, IsHealer, MoveBehindEnabled, AoeEnabled

## Lua переменные (передаются каждый тик)

| Переменная | Что | Откуда |
|-----------|-----|--------|
| `WB_NE` | Все живые мобы в 30yd от игрока | `CountNearbyEnemies()` |
| `WB_NCE` | Мобы в бою в 10yd от игрока, бьющие группу | `CountNearbyCombatEnemies()` |
| `WB_NCET` | Мобы в бою в 10yd от таргета | `CountEnemiesNearTarget()` |
| `WB_AEMIN` | Порог AoE из слайдера UI (default 3) | `AoeMinEnemies` |
| `WB_S.*` | Spell flags (чекбоксы UI) | `SpellFlagsLua` |

## Связи
- [[aoe-system]] — подсчёт врагов, ground AoE, avoidance
- [[buff-system]] — баффы вызываются отдельно из BotEngine.BuffTick()
- [[hivemind]] — slave режим использует тот же CombatExecutor
