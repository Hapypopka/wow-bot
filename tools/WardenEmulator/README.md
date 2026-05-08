# WardenEmulator (WIP)

Цель: эмулировать Warden модуль WoWCircle через Unicorn Engine, чтобы сделать **истинно headless** WoW бот без `WoW.exe`.

## Что сделано

- `WardenEmulator.csproj` — .NET 9 библиотека с зависимостью `UnicornEngine.Unicorn 2.1.3`
- `WardenConstants.cs` — порт `WoWee/include/game/warden_constants.hpp` (sub-opcodes, PE-секции, размеры)
- `UnicornSmokeTest.cs` — минимальная проверка что Unicorn эмулирует x86
- `SmokeTestRunner/` — консольный runner с диагностикой step-by-step
- `native/unicorn.dll` — Windows x64 DLL (взято из `pip install unicorn`, 2.1.4)

## Решено: subprocess Python

`Unicorn.EmuStart()` валит процесс из .NET (на Windows стабильно exit code 9 без stack trace).
Проверены: F# binding 2.1.3, direct P/Invoke, separate thread, native DLL 2.0.1, 2.1.3 (MSVC), 2.1.4 (pip).
Python с тем же DLL работает идеально → проблема в .NET ↔ Unicorn JIT интеграции на Windows.

**Решение:** делегируем эмуляцию **subprocess Python**. C# спавнит python.exe с
`py_helper/warden_emu_helper.py`, общается JSON-сообщениями через stdin/stdout.

```
HeadlessPoc (C#) ──JSON over pipes── python.exe (warden_emu_helper.py)
                                              └── unicorn (x86 emulator)
```

`PythonEmulatorBridge.cs` — высокоуровневый wrapper: Ping/Open/Map/Write/Emu/RegRead/Close.

## Текущее состояние

- ✅ Захват модуля работает (см. `MitmProxy --capture-module`)
- ✅ Python helper эмулирует x86 — smoke + multi-register сценарий
- ✅ C# bridge стабилен
- ⏳ Порт логики WoWee `warden_module.cpp` (PE loader) на Python
- ⏳ Порт `warden_emulator.cpp` (API stubs) на Python
- ⏳ Порт `warden_handler.cpp` (state machine) на C#
- ⏳ Интеграция в HeadlessPoc

## Reference: WoWee

Полная C++ реализация которую мы портируем — `c:\Проекты\wow-bot\tools\WoWee-reference\`.

Ключевые файлы:
- `include/game/warden_constants.hpp` ✅ ПОРТИРОВАН
- `include/game/warden_crypto.hpp` + `src/game/warden_crypto.cpp` (236 lines) — RC4 + SHA1Randx
- `include/game/warden_emulator.hpp` + `src/game/warden_emulator.cpp` (822 lines) — Unicorn + API stubs
- `include/game/warden_handler.hpp` + `src/game/warden_handler.cpp` (1486 lines) — state machine, packet handling
- `include/game/warden_memory.hpp` + `src/game/warden_memory.cpp` (1071 lines) — PE loader
- `include/game/warden_module.hpp` + `src/game/warden_module.cpp` (1552 lines) — download/decrypt pipeline

Итого ~5200 строк C++ для порта на C#.

## Captured module

Реальный Warden модуль с WoWCircle x100:
- `../MitmProxy/warden_module.bin` — 30533 b распакованного x86
- `../MitmProxy/warden_module_info.txt` — hash, key, size

Готов к скармливанию эмулятору как только тот заработает.
