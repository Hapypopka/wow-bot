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

## ⚡ КРИТИЧЕСКИЕ НАХОДКИ ИССЛЕДОВАНИЯ

**Unicorn эмуляция МОЖЕТ БЫТЬ НЕ НУЖНА.** Найдена альтернативная стратегия через
готовые pre-computed responses.

### vmangos/warden_modules (https://github.com/vmangos/warden_modules)

Публичная коллекция **73 Warden модулей** для TC-семейства серверов. Каждый модуль
сопровождается:
- `.bin` — модуль (зашифрованный)
- `.key` — RC4 ключ
- `.cr` — **pre-computed challenge-response база** (1000 записей × 68 байт)

**Наш модуль 79C0768D657977D697E10BAD956CCED1 ЕСТЬ в коллекции.** Размер совпадает,
RC4 ключ совпадает байт-в-байт.

### Формат .cr файла

```
Header (17 bytes):
  [0..3]  uint32 memoryRead
  [4..7]  uint32 pageScanCheck
  [8..16] uint8[9] check opcodes (MEM, MODULE, PAGE_A, PAGE_B, MPQ, LUA, PROC, DRIVER, TIMING)

Entries (1000 × 68 bytes):
  seed[16]      — challenge
  reply[20]     — pre-computed SHA1 reply
  clientKey[16] — post-handshake CMSG RC4
  serverKey[16] — post-handshake SMSG RC4
```

### Стратегия (revised)

1. **HASH_REQUEST handling** — lookup seed в .cr → возврат reply + переключение RC4
2. **CHEAT_CHECKS_REQUEST handling** — реализация 9 check handlers (порт из WoWee)
3. **Никакой эмуляции модуля не требуется** — все вычисления статические

### Будущее: что осталось делать

**Phase 4.2 (несколько часов):** ✅ СДЕЛАНО
- ✅ Парсер .cr на C# (`HeadlessPoc/WardenCrFile.cs`)
- ✅ Обработчик MODULE_USE → MODULE_OK
- ✅ Обработчик HASH_REQUEST с lookup (готов отвечать)
- ✅ ReplaceKeys для switch к pre-computed RC4 keys

### ⚠ ТЕКУЩИЙ БЛОКЕР Phase 4.2

При тесте на WoWCircle x100 headless:
1. SRP6 OK ✓
2. CMSG_AUTH_SESSION → AUTH_RESPONSE result=0x0C (AUTH_OK) ✓
3. Получили SMSG_WARDEN MODULE_USE (37 байт) ✓
4. Расшифровали body, code=0x00 (MODULE_USE) ✓
5. Отправили CMSG_WARDEN MODULE_OK (1 байт, encrypted) ✓
6. **Сервер НЕ шлёт HASH_REQUEST** ✗
7. Через ~47 секунд: «Your anticheat seems to be inactive»
8. Кик через ~114 секунд

Странность: в MITM с реальным клиентом MODULE_OK работал и сервер шёл дальше.
В headless с тем же K, тем же WardenCrypt кодом — не работает.

**Гипотезы:**
- WoWCircle TC может быть кастомизирован — ожидает что-то ещё в MODULE_OK теле
- Тонкий timing/state issue который в MITM не проявлялся
- Ошибка в encryption stream sync, не очевидная из анализа

**Что попробовать в следующей сессии:**
1. Запустить MITM параллельно с headless и побайтово сравнить CMSG_WARDEN bytes
2. Попробовать MODULE_MISSING вместо MODULE_OK — если сервер начнёт стримить модуль,
   значит наша крипта ОК, проблема в логике reaction на MODULE_OK
3. Проверить вторую SMSG_WARDEN packet — может HASH_REQUEST приходит, но мы его
   неправильно декодируем (не как 0x02E6)
4. Попробовать с локальным TC сервером (где у нас полный доступ к логам)

**Phase 4.3 (несколько дней):**
- Порт WoWee `warden_handler.cpp` check handlers
- Скормить fake WoW.exe для MEM/PAGE сканов

**Phase 4.4 (полдня):**
- Интеграция в HeadlessPoc

Размер задачи: **дни**, не недели. Unicorn integration отложен — может пригодиться
если seed не найден в CR (~ 0% случаев если CR покрывает все seeds сервера).

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
