---
title: Spell ID System
updated: 2025-04-15
tags: [concept, spells, lua]
---

# Spell ID System

## Принцип
Все ротации используют числовые Spell ID, не русские названия. Русские названия ненадёжны (регистр, ё, опечатки).

## Lua хелперы (`LuaHelpers.cs`)

| Функция | Назначение | Пример |
|---------|-----------|--------|
| `Cast(id)` | Кастовать спелл по ID | `Cast(12294)` — Mortal Strike |
| `IR(id)` | IsReady — проверка КД + GCD | `if IR(12294) then Cast(12294) end` |
| `HB(id)` | HasBuff — есть ли бафф | `if HB(46916) then ...` — Slam! proc |
| `HD(unit,id)` | HasDebuff — есть ли дебафф | `if not HD('target',772) then ...` — Rend |
| `SN(id)` | SpellName — GetSpellInfo(id) | `local n=SN(6572)` |
| `NR(unit,id)` | NeedsRefresh — дебафф < 3s или нет | для DoT'ов |
| `BS(id)` | BuffStacks — кол-во стаков | `if BS(53817)>=5` — Maelstrom |
| `THP()` | Target HP% (0-1) | `if THP()<0.2` — Execute range |
| `PHP()` | Player HP% (0-1) | `if PHP()<0.5` — defensive |
| `MP()` | Player Mana% (0-1) | `if MP()<0.15` — Life Tap |

## Spell Flags (`WB_S.*`)

Чекбоксы в UI → передаются как `WB_S.Key=true/false`:
```lua
if WB_S.MS~=false and IR(12294) then Cast(12294) return end
```
`~=false` означает: кастить по умолчанию, если юзер не выключил.
`==true` означает: не кастить по умолчанию, юзер должен включить.

## Где искать ID спеллов
- `SpellDatabase.json` — локальная база (50k+ спеллов)
- wow.66wan.net — онлайн база с русскими названиями
- Локальный SPP MySQL — `spell_dbc` таблица

## Связи
- [[wowcircle-quirks]] — ё→е, регистр
- [[combat-system]] — как ротации выполняются
