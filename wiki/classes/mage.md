---
title: Mage
updated: 2025-04-15
tags: [class, mage]
---

# Mage

Файл: `WowBot.Core/Game/Rotations/MageRotation.cs`
Определение спека: talent tabs (t1=Arcane, t2=Fire, t3=Frost)

## Arcane
| Приоритет | Спелл | ID | Условие |
|-----------|-------|----|---------|
| 1 | Arcane Power | 12042 | IR() |
| 2 | Mirror Image | 55342 | IR() |
| 3 | Arcane Barrage | 44425 | HB(44401) proc + IR() |
| 4 | Evocation | 12051 | mana<35% + IR() |
| 5 | Arcane Blast | 30451 | filler |

## Fire
| Приоритет | Спелл | ID | Условие |
|-----------|-------|----|---------|
| 1 | Mirror Image | 55342 | IR() |
| 2 | Combustion | 11129 | IR() |
| 3 | Living Bomb | 44457 | нет дебаффа на таргете |
| 4 | Pyroblast | 11366 | HB(48108) Hot Streak proc |
| 5 | Scorch | 2948 | нет дебаффа на таргете |
| 6 | Fireball | 133 | filler |

## Frost
| Приоритет | Спелл | ID | Условие |
|-----------|-------|----|---------|
| 1 | Mirror Image | 55342 | IR() |
| 2 | Deep Freeze | 44572 | IR() |
| 3 | Ice Lance | 30455 | HB(44544) Fingers of Frost proc |
| 4 | Frostfire Bolt | 44614 | HB(57761) Brain Freeze proc |
| 5 | Frostbolt | 116 | filler |

## AoE: НЕТ (все 3 спека)
Нет Blizzard (10), Flamestrike (2120), Arcane Explosion (1449).

## Связи
- [[aoe-system]] — отсутствует
- [[spell-ids]]
