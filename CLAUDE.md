# WowBot — WoW 3.3.5a Bot для WoWCircle

## Что это
Бот для PvE (автоматические ротации, follow, face target) для WoW WoTLK 3.3.5a на приватном сервере WoWCircle. Русский клиент.

## Персонажи
- **Lovelion** — Balance Druid (Сова), 80 уровень, основной для бота
- **Virtuella** — другой персонаж

## Архитектура

### Solution: WowBot.sln
- **WowBot.Core** — библиотека (net8.0-windows, x86)
  - `Memory/WinApi.cs` — P/Invoke (OpenProcess, ReadProcessMemory, WriteProcessMemory, VirtualAllocEx)
  - `Memory/MemoryReader.cs` — обёртка чтения/записи памяти WoW
  - `Game/Offsets.cs` — все оффсеты (КАСТОМНЫЕ для WoWCircle, см. ниже)
  - `Game/ObjectManager.cs` — обход объектов WoW, чтение HP/Mana/Position/Target
  - `Game/Entities/` — WowObject, WowUnit, WowPlayer
  - `Game/EndSceneHook.cs` — хук EndScene (D3D9), codecave в памяти WoW, Lua_DoString
  - `Game/Navigation.cs` — FaceTarget (поворот к цели), FollowUnit
  - `Game/Rotations/RotationEngine.cs` — таймер 150мс, face target + Lua ротация
  - `Game/Rotations/BalanceDruidPvE.cs` — Lua-скрипт ротации совы

- **WowBot.Injector** — WPF приложение (net8.0-windows, x86)
  - UI: Attach/Detach, Rotation Start/Stop, Lua Console, Scan Spells, Dump
  - app.manifest — requireAdministrator

### Как это работает
1. **ReadProcessMemory** — читаем данные из WoW (HP, мана, позиция, объекты)
2. **EndScene Hook** — codecave: пишем x86 asm в память WoW, патчим EndScene (D3D9)
3. **Lua_DoString** — вызываем WoW Lua из главного потока через хук
4. **Rotation Engine** — каждые 150мс: face target (память) + execute Lua rotation
5. **Follow** — FollowUnit('focus') в Lua, CheckInteractDistance для проверки дистанции

## Сборка и запуск
```bash
# Сборка (из корня проекта):
dotnet publish WowBot.Injector -c Release -r win-x86 --self-contained -o publish

# Или через build.bat (убивает старый процесс + собирает)

# Запуск:
publish\WowBot.Injector.exe
# Требует права администратора (UAC)
# .NET Runtime не нужен (self-contained)
# Всё x86 (WoW 3.3.5a — 32-битный)
```

## КРИТИЧЕСКИЕ НЮАНСЫ WoWCircle

### Оффсеты дескрипторов — ОТЛИЧАЮТСЯ от стандартного build 12340!
Стандартные оффсеты НЕ работают. Правильные (найдены через дамп):
- HEALTH = 0x18 (стандарт: 0x58)
- MANA = 0x19 (стандарт: 0x5C)
- MAX_HEALTH = 0x20 (стандарт: 0x70)
- MAX_MANA = 0x21 (стандарт: 0x74)
- LEVEL = 0x36 (стандарт: 0x88)
- TARGET = 0x12, FLAGS = 0x3B, DISPLAY_ID = 0x3E

### Глобальные указатели и позиция — стандартные, работают
- ClientConnection = 0x00C79CE0, ObjMgr = +0x2ED0
- Position X/Y/Z = +0x798/+0x79C/+0x7A0, Facing = +0x7A8

### Русский клиент — названия спеллов
- **НИКОГДА не использовать букву ё** — в клиенте все е
- "Звездный огонь" НЕ "Звёздный огонь"
- "Облик лунного совуха" НЕ "Лунный совух"
- "Сила Природы" с большой П
- Полный список спеллов друида в файле памяти wow-bot.md

### Lua API нюансы WoWCircle
- `BOOKTYPE_SPELL` не определен — использовать строку `'spell'`
- `GetLocalizedText` (0x007225E0) КРАШИТ WoW — НЕ использовать!
- Для вывода данных из Lua → `DEFAULT_CHAT_FRAME:AddMessage()` (локальное)
- `GetSpellInfo(spellId)` работает — через него сканируем названия спеллов

### EndScene Hook
- Может иметь модифицированный пролог — нужен CalculateStolenBytes
- Относительные инструкции (call/jmp rel32) нужно фиксить при копировании
- Диагностика сохраняется в endscene_diag.txt при подключении

### Сборка
- Publish блокируется если exe запущен — taskkill перед сборкой
- Собирать в одну папку `publish` (не плодить publish2, publish3...)
- Каждый publish ~150МБ (self-contained)

## Ключевые механики
- **Click-to-Move (CTM)**: плавное следование, адреса CTM_Base=0x00CA11D8, X=+0x8C, Y=+0x90, Z=+0x94, Action=+0x1C, Precision=+0x0C
- **Cast detection**: playerBase+0xA60 = текущий каст ID, +0xA6C = канал ID. Если != 0 → кастуем
- **BotEngine**: единый движок, три режима (follow/rotation/both), управляется через оверлей
- **Facing**: запись float в playerBase+0x7A8. Не менять во время каста!

## Известные проблемы
- CTM: на последних метрах может слегка дёргаться
- Instants на бегу без поворота к таргету (по дизайну — "попадёт или нет, пофиг")
- EndScene hook может не работать на других ПК (разные D3D9 пролога)

## Планы (следующие этапы)
- [ ] Ротации для кастеров: SP, Demo, Affli, Destro, Fire Mage, Arcane Mage, Elem Shaman
- [ ] Выбор спека в оверлее (dropdown)
- [ ] Автодетект класса/спека
- [ ] Дубовая кожа по HP
- [ ] Хоткей вкл/выкл
- [ ] Hot-reload ротаций
