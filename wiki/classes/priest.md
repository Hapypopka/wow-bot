---
title: Priest
updated: 2025-04-15
tags: [class, priest]
---

# Priest

Файл: `WowBot.Core/Game/Rotations/PriestRotation.cs` (делегирует в AllRotations.cs)
Lua: `AllRotations.cs` строки 676-787
MultiDot: `WowBot.Core/Game/MultiDotHelper.cs`

## Shadow
| Приоритет | Спелл | ID | Условие |
|-----------|-------|----|---------|
| 1 | Shadowform | 15473 | обязательный бафф |
| 2 | Dispersion | 47585 | mana<15% + IR() |
| 3 | Vampiric Touch | 34914 | NR() + throttle 2s |
| 4 | Devouring Plague | 2944 | NR() + throttle 2s |
| 5 | Shadow Word: Pain | 589 | NR() + throttle 2s |
| 6 | Mind Blast | 8092 | талант row3 col8 + IR() |
| 7 | Shadow Form Drain | 34433 | mana<50% + IR() |
| 8 | Mind Flay | 15407 | filler (channel) |

### MultiDot (AoE режим)
Через `MultiDotHelper.cs` — отдельная система, не WB_NCE/WB_NCET.
- Сканирует юнитов в 30yd от игрока, 10yd от таргета
- Раскидывает VT/DP/SWP по нескольким таргетам
- Mind Sear при достаточно врагов
- **Использует русские названия спеллов через CastSpellByName** (не spell ID!)
  - "Прикосновение вампира", "Всепожирающая чума", "Слово Тьмы: Боль"
  - "Взрыв разума", "Иссушение разума", "Пытка разума"

## Discipline (хилер)
| Приоритет | Спелл | ID | Условие |
|-----------|-------|----|---------|
| 1 | Resurrect | 2006 | вне боя, TryRes() |
| 2 | Pain Suppression | 33206 | urgency>=2 + IR() |
| 3 | Power Word: Shield | 17 | IR() (instant) |
| 4 | Prayer of Mending | 33076 | IR(), проактивно на танка |
| 5 | Penance | 47540 | bestHP<95% + IR() |
| 6 | Inner Focus | 14751 | mana<70% + IR() |
| 7 | Pain Suppression | 33206 | bestHP<40% + IR() |
| 8 | Flash Heal | 2061 | urgency>=1 |
| 9 | Renew | 139 | needHoT |
| 10 | Flash Heal | 2061 | bestHP<95% |
| — | Dispel Magic | 527 | ddt=='Magic' |
| — | Remove Disease | 552/528 | ddt=='Disease' |

## Holy (хилер)
| Приоритет | Спелл | ID | Условие |
|-----------|-------|----|---------|
| 1 | Resurrect | 2006 | вне боя, TryRes() |
| 2 | Guardian Spirit | 47788 | urgency>=2 + IR() |
| 3 | Circle of Healing | 34861 | lowCount>=2 + IR() (AoE хил) |
| 4 | Guardian Spirit | 47788 | bestHP<20% + IR() |
| 5 | Prayer of Mending | 33076 | IR(), проактивно на танка |
| 6 | Renew | 139 | needHoT / bestHP<95% |
| 7 | Flash Heal | 2061 | urgency>=1 |
| 8 | Inner Focus | 14751 | mana<70% + IR() |
| 9 | Greater Heal | 2060 | bestHP<60% |
| 10 | Flash Heal | 2061 | bestHP<95% |
| 11 | Binding Heal | 32546 | bestHP<95% |
| — | Dispel Magic | 527 | ddt=='Magic' |
| — | Remove Disease | 552/528 | ddt=='Disease' |

## Связи
- [[aoe-system]] — Shadow через MultiDotHelper (отдельная система)
- [[spell-ids]] — MultiDot использует русские имена (!) вместо ID
