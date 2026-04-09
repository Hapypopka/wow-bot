---
paths:
  - "**/Offsets.cs"
  - "**/AllRotations.cs"
  - "**/EndSceneHook.cs"
  - "**/Entities/**"
---

# WoWCircle 3.3.5a — кастомные нюансы

## Оффсеты дескрипторов — ОТЛИЧАЮТСЯ от стандартного build 12340!
- HEALTH = 0x18 (стандарт: 0x58), MANA = 0x19, MAX_HEALTH = 0x20, MAX_MANA = 0x21
- LEVEL = 0x36, TARGET = 0x12, FLAGS = 0x3B, DISPLAY_ID = 0x3E

## Глобальные указатели — стандартные
- ClientConnection = 0x00C79CE0, ObjMgr = +0x2ED0
- Position X/Y/Z = +0x798/+0x79C/+0x7A0, Facing = +0x7A8
- CastId = +0xA60, ChannelId = +0xA6C, CTM_Base = 0x00CA11D8
- LastHardwareAction = 0x00B499A4 (стандарт +8)

## Русский клиент — названия спеллов
- НИКОГДА не использовать букву ё — в клиенте все "е"
- Регистр важен! "Правосудие света" (маленькая с), "Печать Света" (большая С)
- Перед добавлением спелла — сверять со скриншотом спеллбука

## Lua API нюансы
- `GetLocalizedText` (0x007225E0) КРАШИТ WoW — НЕ использовать!
- `BOOKTYPE_SPELL` не определен — использовать строку `'spell'`
- `EditMacro` НЕ работает в бою — protected function
- Spell ID через GetSpellInfo(spellId) — надёжнее русских названий

## EndScene Hook
- CalculateStolenBytes разбирает модифицированный пролог
- Относительные инструкции (call/jmp rel32) нужно фиксить при копировании
- D3D9Helper: fallback через фейковое устройство
- Lua буфер: 32768 байт (скрипт хилера ~16KB)
