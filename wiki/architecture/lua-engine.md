---
title: Lua Engine (EndSceneHook)
updated: 2025-04-15
tags: [architecture, lua, endscene, core]
---

# Lua Engine — EndSceneHook

Файл: `WowBot.Core/Game/EndSceneHook.cs` (609 строк)
Ядро бота — инжекция Lua кода в WoW через D3D9 EndScene хук.

## Как работает

1. **Находит EndScene** — через D3D9 vtable chain (`DevicePtr1 → +0x397C → VTable → +0xA8`)
2. **Аллоцирует 33KB** в процессе WoW:
   - Codecave (512 байт) — x86 ASM код хука
   - Lua буфер (32KB) — скрипты ротаций (хилер ~16KB)
   - Флаг (4 байта) — синхронизация (0=idle, 1=exec, 2=done, 3=exec+read...)
   - Служебные данные (строки, указатели, CTM/TerrainClick структуры)
3. **Патчит EndScene** — JMP rel32 на codecave + NOP до границы stolen bytes
4. **Каждый кадр WoW** codecave проверяет флаг и выполняет команду

## Флаги (режимы работы)

| Флаг | Что делает |
|------|-----------|
| 0 | Idle — ничего |
| 1 | Lua_DoString — выполнить скрипт |
| 3 | Lua_DoString + lua_getfield — выполнить + прочитать WB_R |
| 4 | lua_getfield only — прочитать без выполнения |
| 5 | HandleTerrainClick — клик по земле (ground AoE) |
| 6 | CGPlayer_C__ClickToMove — прямой вызов CTM функции |

## Public API

| Метод | Флаг | Таймаут | Назначение |
|-------|------|---------|-----------|
| `ExecuteLua(code)` | 1 | 1000ms | Выполнить Lua |
| `ExecuteLuaWithResult(code)` | 1→4 | 2000ms | Выполнить + прочитать результат из WB_R |
| `CastTerrainClick(x,y,z)` | 5 | 500ms | Ground AoE (Hurricane, Volley) |
| `CallClickToMove(x,y,z,...)` | 6 | 500ms | Прямой CTM через функцию WoW |

## Stolen Bytes
- x86 instruction decoder (1-5 байт инструкции)
- Relative call/jmp фиксятся при релокации: `new_rel32 = target - (new_addr + offset + 5)`

## Ключевые оффсеты
- `Lua_DoString = 0x00819210`
- `LuaState = 0x00D3F78C`
- `HandleTerrainClick = 0x00527830`
- `CGPlayer_C__ClickToMove = 0x00727400`

## КРИТИЧНО
- **НЕ трогать без крайней необходимости** — одна ошибка = краш WoW
- **GetLocalizedText (0x007225E0) КРАШИТ** — не использовать
- Буфер 32KB — если скрипт больше, обрежется молча

## Связи
- [[combat-system]] — ротации выполняются через ExecuteLua
- [[navigation]] — CTM через CallClickToMove
- [[aoe-system]] — ground AoE через CastTerrainClick
- [[wowcircle-quirks]] — оффсеты и Lua нюансы
- [[spell-ids]] — LuaHelpers загружаются как prefix к каждому скрипту
