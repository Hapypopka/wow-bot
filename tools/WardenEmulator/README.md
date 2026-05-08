# WardenEmulator (WIP)

Цель: эмулировать Warden модуль WoWCircle через Unicorn Engine, чтобы сделать **истинно headless** WoW бот без `WoW.exe`.

## Что сделано

- `WardenEmulator.csproj` — .NET 9 библиотека с зависимостью `UnicornEngine.Unicorn 2.1.3`
- `WardenConstants.cs` — порт `WoWee/include/game/warden_constants.hpp` (sub-opcodes, PE-секции, размеры)
- `UnicornSmokeTest.cs` — минимальная проверка что Unicorn эмулирует x86
- `SmokeTestRunner/` — консольный runner с диагностикой step-by-step
- `native/unicorn.dll` — Windows x64 DLL (взято из `pip install unicorn`, 2.1.4)

## Текущий блокер

`Unicorn.EmuStart()` валит процесс с exit code 9 (native crash). 
Прошлые шаги работают:
- ✅ `new Unicorn(UC_ARCH_X86, UC_MODE_32)` — создание инстанса
- ✅ `MemMap` — выделение памяти
- ✅ `MemWrite` — запись кода
- ❌ `EmuStart` — нативный краш

**Гипотеза:** ABI mismatch между .NET binding `UnicornEngine.Unicorn 2.1.3` (компилировано под 2.1.0/2.1.1) и DLL `unicorn 2.1.4`. Возможно поменялась calling convention или порядок аргументов.

## Что попробовать в следующей сессии

1. **Synced version:** найти `unicorn.dll` ровно от версии 2.1.3 (которой собирался binding) — на github releases или archive
2. **Direct P/Invoke:** написать свой `[DllImport("unicorn")]` в обход F# binding — будет ровно та сигнатура которая в native
3. **Альтернатива:** взять другой x86 emulator (Triton, IcedFlame), но Unicorn — стандарт
4. **Пересобрать binding:** склонировать binding из github, перекомпилировать против native 2.1.4

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
