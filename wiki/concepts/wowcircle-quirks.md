---
title: WoWCircle Quirks
updated: 2025-04-15
tags: [concept, wowcircle, gotchas]
---

# WoWCircle 3.3.5a — Специфика

Приватный сервер. Русский клиент x86. Оффсеты стандартные (TrinityCore), но есть нюансы.

## Lua API

| Что | Статус | Детали |
|-----|--------|--------|
| `GetLocalizedText()` | КРАШИТ WoW | Не использовать никогда |
| `BOOKTYPE_SPELL` | Не определён | Использовать строку `'spell'` |
| `EditMacro` | Protected | Не работает в бою |
| `GetSpellInfo(id)` | Работает | Основной способ получения имени спелла |

## Русский клиент

- Буква **ё** не существует — всегда **е** ("Благословение" не "Благословениё")
- **Регистр важен**: "Правосудие света" (маленькая с), "Печать Света" (большая С)
- Перед добавлением спелла — сверять со скриншотом спеллбука

## Оффсеты (стандартные TrinityCore)

- OBJECT_END=0x06, TARGET=0x12, HEALTH=0x18, MANA=0x19
- MAX_HEALTH=0x20, MAX_MANA=0x21, LEVEL=0x36, FLAGS=0x3B
- ClientConnection=0x00C79CE0, ObjMgr=+0x2ED0
- Position X/Y/Z=+0x798/+0x79C/+0x7A0, Facing=+0x7A8
- CastId=+0xA60, ChannelId=+0xA6C, CTM_Base=0x00CA11D8

## DynamicObject дескриптор (нестандарт!)

Проверено по dump из реального боя. Отличается от "канонического" TrinityCore:

| Offset | Size | Что |
|--------|------|-----|
| +0x00 | 8 | **GUID самого DynObject** (не Caster! high byte 0xF1) |
| +0x08 | 4 | Bytes |
| +0x0C | 4 | **SpellId** |
| +0x10 | 4 | Radius (на WoWCircle всегда 1.0 — использовать fallback по SpellId) |
| +0x14 | 4 | ??? |
| +0x18 | 8 | **Caster GUID** — реальный владелец AoE зоны |

Если читать Caster из +0x00 (как в AmeisenBotX) — получишь GUID DynObject'а, а не кастера. Это сломает фильтр "своё AoE" → игрок убегает из собственной Грозы/DnD/Consecration.

## EndScene Hook

- Lua буфер: 32768 байт (скрипт хилера ~16KB — половина)
- CalculateStolenBytes разбирает модифицированный пролог
- Относительные инструкции (call/jmp rel32) фиксятся при копировании
- D3D9Helper: fallback через фейковое устройство

## Связи
- [[lua-engine]] — как работает Lua инжекция
- [[combat-system]] — ротации используют GetSpellInfo(id)
