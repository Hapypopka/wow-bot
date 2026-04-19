---
title: AoE System
updated: 2026-04-19
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

### Алгоритм grid safe spot (v2 — с 2026-04-19)

`CombatHelper.TryAoEAvoidance()`:

**1. Сбор опасных зон (DangerZones):**
- Проход по DynObjects из ObjectManager
- Своя зона (`Caster == self`) → игнор
- Безопасный дебафф (Desecration — только слоу) → игнор
- **Дружественный caster (партия):** бежим только если реально получаем дебафф от зоны (дуэль/misfired)
- **Враждебный caster:** всегда опасно (превентивно)

**2. Предикт движения лужи:**
- Coldflame-подобные ползущие зоны — вектор `caster→dyn`, скорость 6y/s
- При расчёте учитываем текущую позицию И через 0.5с
- Статичные (Consecration, DnD) — velocity=0

**3. Score позиции:**
- `score = min(dist до края каждой зоны сейчас ИЛИ через 0.5с)`
- Потолок **`AoESafetyCap = 2.5y`** — не различаем "в 10y от лужи" и "в 2.5y", иначе грид бы бегал к самой далёкой точке
- Score отрицательный = в луже
- Score ≥ safetyCap = безопасно

**4. Решение:**
- `score < 1y` → в опасности, бежим
- `score ≥ 2.5y` → безопасно, останов (`_fleeDestination=null`, `NativeStop()`)
- Между 1 и 2.5 — гистерезис

**5. Выбор destination (FindSafeSpot):**
- 24 кандидата на кольце вокруг игрока
- **Адаптивный `ringRadius = max(8, largestDangerRadius + 3)`** — для DnD 10y радиус 13y, для Void Zone 15y — 18y. Иначе при касте по центру крупной лужи все 8y кандидаты внутри неё, бот не видит "чистой земли".
- Score каждого кандидата → выбираем max
- **Гистерезис 2y:** если старый destination почти так же хорош — не меняем, чтоб не дёргаться

**6. Выход:**
- Native `_ctm.NativeStop()` через PlayerClickToMoveStop (0x72B3A0) — шлёт серверу MSG_MOVE_STOP с актуальной позицией → aura на сервере сразу видит что игрок вне зоны, тики прекращаются мгновенно. Без этого был movement desync: клиент уже выбежал, сервер ещё видит старую позицию.

### Proactive AoE Avoidance (слой 2)

`CombatHelper.TryProactiveAvoidance()` — уклонение по SPELL_CAST_START ДО создания DynObject. Смотрит `EnemyCastObserver`, сверяет с `DangerousSpellTable` (3865+ спеллов). По `AoETargetMode` выбирает стратегию: `AroundCaster` (flee радиально), `GroundTargeted` (strafe перпендикулярно), `Cone/Frontal` (strafe вбок).

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
