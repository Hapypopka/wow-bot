---
title: AoE System
updated: 2025-04-15
tags: [concept, aoe, combat]
---

# AoE System

## Три компонента

### 1. Подсчёт врагов (C# → Lua)

В `BotEngine.cs` каждый тик считаются и передаются в Lua:

| Переменная | Метод | Радиус | От кого | Для кого |
|-----------|-------|--------|---------|----------|
| `WB_NCE` | `CountNearbyCombatEnemies(player)` | 10yd | Игрок | **Танки** (стоят в мобах) |
| `WB_NCET` | `CountEnemiesNearTarget(target)` | 10yd | Таргет | **DPS** (стоят далеко) |
| `WB_AEMIN` | UI слайдер | — | — | Порог (default 3) |

**Баг (исправлен 2025-04-15):** раньше был только WB_NCE. Рейнж DPS стоят в 25-30yd от мобов → WB_NCE=0 → AoE никогда не срабатывал. Добавлен WB_NCET.

### 2. Inline AoE в ротациях (Lua)

Каждая ротация сама решает когда кастить AoE:

| Класс | Спек | Спелл | Условие | Переменная |
|-------|------|-------|---------|-----------|
| Warrior | Prot | Thunder Clap (6343) | >=2 | WB_NCE |
| Paladin | Prot | Consecration (26573) | >=2 | WB_NCE |
| DK | Blood | DnD (43265) | >=2 | WB_NCE |
| Druid | Bear | Swipe (779) | >=2 | WB_NCE |
| DK | Unholy | Pestilence (50842) + DnD (43265) | >=AEMIN | WB_NCET |
| Shaman | Enh | Chain Lightning (421) | >=AEMIN | WB_NCET |
| Druid | Cat | Swipe (62078) | >=AEMIN | WB_NCET |
| Hunter | MM | Multi-Shot (2643) | >=AEMIN | WB_NCET |
| Warlock | Demo | Seed of Corruption (27243) | >=AEMIN | WB_NCET |

### 3. Ground AoE (C# через CastTerrainClick)

В `CombatHelper.TryGroundAoE()` — для спеллов требующих клик по земле:
- **Druid Balance**: Hurricane (Гроза) — по русскому имени
- **Hunter**: Volley (1510) — через GetSpellInfo

Использует `CountEnemiesNearTarget()` (правильный подсчёт).

## AoE Avoidance (убегание из луж)

`CombatHelper.TryAoEAvoidance()`:
- Сканирует DynObjects из ObjectManager
- **Пропускает свои заклинания** (dyn.Caster == player.Guid) — чтобы не убегать из своих Hurricane/Volley/DnD/Consecration
- Проверяет есть ли у игрока дебафф совпадающий со SpellId лужи
- Фильтрует безопасные (Desecration — только слоу)
- Считает среднюю позицию луж → вектор побега
- Flee на 2 секунды, приоритет над всем

## Что НЕ работает
- Лужи без DynObject не детектятся (визуальные эффекты)
- Нет AoE для: Mage (все), Rogue (все), Warrior Arms/Fury, Priest Shadow, Hunter BM/Surv, Warlock Affli/Destro, Shaman Ele, Druid Balance, Paladin Ret, DK Frost

## Связи
- [[combat-system]] — CombatExecutor вызывает TryGroundAoE
- Классы: [[warrior]], [[paladin]], [[death-knight]], [[druid]], [[shaman]], [[hunter]], [[warlock]]
