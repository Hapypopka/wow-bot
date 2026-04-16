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
- Нет AoE для: Priest Disc/Holy (хилеры), Druid Resto (хилер), Shaman Resto (хилер), Paladin Holy (хилер)
- Paladin Ret — Divine Storm уже есть в ротации (не через WB_NCET)

## Полная таблица AoE по классам (после апдейта 2025-04-17)

| Класс | Спек | Механизм | Спеллы |
|-------|------|----------|--------|
| Warrior | Arms | Inline Lua | Bladestorm (46924), Sweeping Strikes (12328), Cleave (845) |
| Warrior | Fury | Inline Lua | Whirlwind (1680), Cleave (845) |
| Warrior | Prot | Inline Lua | Thunder Clap (6343), Cleave (845) |
| Paladin | Ret | C# ротация | Divine Storm (53385) |
| Paladin | Prot | Inline Lua | Consecration (26573), Holy Wrath |
| Hunter | BM | Inline Lua | Multi-Shot (2643) |
| Hunter | MM | Inline Lua + Ground AoE | Multi-Shot (2643), Volley (58434) ground |
| Hunter | Surv | Inline Lua | Multi-Shot + Lock and Load proc |
| Rogue | Assa | Inline Lua | Fan of Knives (51723) |
| Rogue | Combat | Inline Lua | Fan of Knives + Blade Flurry (13877) |
| Rogue | Sub | Inline Lua | Fan of Knives (51723) |
| Priest | Shadow | MultiDotHelper | Mind Sear + multi-DoT |
| DK | Blood | Inline Lua | Death and Decay (43265) |
| DK | Frost | Inline Lua | Howling Blast (49184), Blood Boil (48721), DnD |
| DK | Unholy | Inline Lua | Pestilence, DnD |
| Shaman | Ele | Inline Lua | Magma Totem (58734), Fire Nova (61657), Chain Lightning (421) |
| Shaman | Enh | Inline Lua | Chain Lightning при Maelstrom 5, Magma Totem |
| Mage | Arcane | Inline + Ground | Arcane Explosion (42921), Blizzard (42940) ground |
| Mage | Fire | Inline + Ground | Dragon's Breath (42950), Blast Wave (42945), Flamestrike (42926) ground |
| Mage | Frost | Inline + Ground | Cone of Cold (42931), Blizzard (42940) ground |
| Warlock | Affli | Inline Lua | Seed of Corruption (27243) |
| Warlock | Demo | Inline Lua | Seed of Corruption (27243) |
| Warlock | Destro | Inline Lua | Seed of Corruption (27243) |
| Druid | Balance | Ground AoE | Hurricane (48467) |
| Druid | Cat | Inline Lua | Swipe Cat (62078) |
| Druid | Bear | Inline Lua | Swipe Bear (779) |

## Связи
- [[combat-system]] — CombatExecutor вызывает TryGroundAoE
- Классы: [[warrior]], [[paladin]], [[death-knight]], [[druid]], [[shaman]], [[hunter]], [[warlock]]
