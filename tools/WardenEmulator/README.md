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
- ✅ C# bridge стабилен (PythonEmulatorBridge)
- ✅ Phase 4.1: парсер `parseExecutableFormat` + `applyRelocations` (`module_loader.py`)
  - Из 6 форматов WoWee (3 layout × u16/u32) ни один **не подошёл** к WoWCircle модулю
  - Сработал raw fallback: image[i] = file[i+4], получили 45056-байтовый образ
- ⏳ **Блокер Phase 4.1:** WoWCircle использует кастомный формат модуля, отличающийся от стандартного TC.
  Граница code↔relocs неизвестна. Релоки видны в районе 0x76C0..0x7745 файла, но точно где начинаются — надо реверсить.
- ⏳ Phase 4.2: API stubs (GetTickCount, IsDebuggerPresent, ...)
- ⏳ Phase 4.3: PE map (fake WoW.exe sections)
- ⏳ Phase 4.4: state machine для WARDEN_HASH_REQUEST → emulate → reply
- ⏳ Phase 4.5: интеграция в HeadlessPoc

## Что нужно для следующей сессии

1. **Реверс формата WoWCircle модуля** — определить где кончается code, где начинаются relocs.
   Подходы:
   - Дисассемблировать `warden_module_image.bin` в IDA Free, найти последнюю валидную инструкцию
   - Перебирать кандидаты границы и тестировать в Unicorn (если эмуляция работает = граница верная)
   - Сравнить с известными TC модулями (нужен другой захваченный с TC сервера)

2. **После того как формат разобран** — Phase 4.2-4.5 по плану выше.

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
