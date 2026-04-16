---
title: Rogue
updated: 2025-04-15
tags: [class, rogue]
---

# Rogue

Файл: `WowBot.Core/Game/Rotations/RogueRotation.cs`
Определение спека: talent tabs (t1=Assassination, t2=Combat, t3=Subtlety)
Combo points: `local cp=GetComboPoints('player','target')`

## Assassination
| Приоритет | Спелл | ID | Условие |
|-----------|-------|----|---------|
| 1 | Envenom | 32645 | cp>=4 + IR() |
| 2 | Rupture | 1943 | cp>=4 + нет дебаффа |
| 3 | Hunger for Blood | 51662 | нет баффа + IR() |
| 4 | Mutilate | 1329 | filler |

## Combat
| Приоритет | Спелл | ID | Условие |
|-----------|-------|----|---------|
| 1 | Slice and Dice | 5171 | cp>=1 + нет баффа |
| 2 | Rupture | 1943 | cp>=5 + нет дебаффа |
| 3 | Eviscerate | 2098 | cp>=5 |
| 4 | Killing Spree | 51690 | IR() |
| 5 | Sinister Strike | 1752 | filler |

## Subtlety
| Приоритет | Спелл | ID | Условие |
|-----------|-------|----|---------|
| 1 | Eviscerate | 2098 | cp>=5 |
| 2 | Rupture | 1943 | cp>=5 + нет дебаффа |
| 3 | Hemorrhage | 16511 | нет дебаффа |
| 4 | Backstab | 53 | filler |

## AoE: НЕТ (все 3 спека)
Нет Fan of Knives (51723).

## Связи
- [[aoe-system]] — отсутствует
- [[spell-ids]]
