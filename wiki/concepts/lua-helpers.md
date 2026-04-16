---
title: Lua Helpers
updated: 2025-04-15
tags: [concept, lua, rotations]
---

# LuaHelpers

Файл: `WowBot.Core/Game/Rotations/LuaHelpers.cs`
Инжектируется как prefix к каждому скрипту ротации.

## Кеш спеллов
```lua
local _SN = {}
function SN(id)  -- GetSpellInfo(id), кешировано
```

## Каст
| Функция | Назначение |
|---------|-----------|
| `Cast(id)` | Кастовать по ID → SN(id) → CastSpellByName |
| `CastOn(u, id)` | Кастовать на юнита: `/cast [@unit] SpellName` |

## Проверки готовности
| Функция | Назначение |
|---------|-----------|
| `IR(id)` | IsReady — спелл не на КД и не на GCD |
| `IU(id)` | IsUsable — хватает маны/энергии |
| `CDLeft(name)` | Оставшийся КД в секундах |

## Баффы / Дебаффы
| Функция | Назначение |
|---------|-----------|
| `HB(id)` | HasBuff — есть ли бафф на игроке (по spell ID) |
| `HasBuff(name)` | То же, но по имени |
| `HasBuffById(id)` | Дубликат HB (legacy) |
| `HD(u, id)` | HasDebuff — есть ли дебафф на юните |
| `BS(id)` | BuffStacks — количество стаков баффа |
| `NR(u, id)` | NeedsRefresh — дебафф осталось < cast time (для DoT) |

## Ресурсы
| Функция | Возвращает |
|---------|-----------|
| `PHP()` | Player HP% (0-1) |
| `THP()` | Target HP% (0-1) |
| `MP()` | Player Mana% (0-1) |
| `CP()` | Combo Points |

## Утилиты
| Функция | Назначение |
|---------|-----------|
| `IsBoss()` | `UnitClassification('target')=='worldboss'` |
| `IsReady(name)` | Проверка КД по имени спелла |

## Wrappers (обёртки ротаций)
| Wrapper | Для кого | PreChecks |
|---------|----------|-----------|
| `WrapDPS(name, body)` | Мили/рейнж DPS | Mounted, dead, casting, target exists + in combat, StartAttack |
| `WrapHealer(name, body)` | Хилеры | Mounted, dead, casting (без проверки таргета) |
| `WrapFull(name, body)` | Полный (DPS + доп хелперы) | Включает TryTaunt, TryDefCD |
| `Wrap(name, body)` | Минимальный | Только базовые хелперы |

## Связи
- [[spell-ids]] — SN() = основа всей системы spell ID
- [[combat-system]] — каждый тик = prefix LuaHelpers + rotation script
- [[lua-engine]] — скрипты выполняются через EndSceneHook.ExecuteLua
