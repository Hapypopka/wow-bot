# WowBot — WoW 3.3.5a Bot для WoWCircle

## Что это
Бот для PvE (автоматические ротации, follow, face target, хилбот) для WoW WoTLK 3.3.5a на приватном сервере WoWCircle. Русский клиент.

## Персонажи
- **Lovelion** — Balance Druid (Сова), 80 уровень
- **Virtuella** — Holy/Ret Paladin, 80 уровень

## Архитектура

### Solution: WowBot.sln
- **WowBot.Core** — библиотека (net8.0-windows, x86)
  - `Memory/WinApi.cs` — P/Invoke (OpenProcess, Read/WriteProcessMemory, VirtualAllocEx, D3D9 COM)
  - `Memory/MemoryReader.cs` — обёртка чтения/записи памяти WoW
  - `Game/Offsets.cs` — все оффсеты (КАСТОМНЫЕ для WoWCircle, см. ниже)
  - `Game/ObjectManager.cs` — обход объектов WoW, LocalPlayer, GetTarget(), GetUnitsInRange()
  - `Game/Entities/` — WowObject → WowUnit → WowPlayer
  - `Game/EndSceneHook.cs` — хук EndScene (D3D9), codecave (512+16384+128 байт), Lua_DoString, верификация
  - `Game/D3D9Helper.cs` — фейковое D3D9 устройство для поиска EndScene (fallback при оверлеях)
  - `Game/Navigation.cs` — FaceTarget (запись float в +0x7A8), FollowUnit
  - `Game/ClickToMove.cs` — CTM через запись координат в память
  - `Game/LuaReader.cs` — двухпроходный скан макроса для чтения Lua→C#
  - `Game/BotEngine.cs` — главный координатор: таймер 150мс, Follow/Rotation/Buffs/AoE
  - `Game/Rotations/AllRotations.cs` — ВСЕ ротации (5 спеков) в одном Lua-скрипте
  - `Logger.cs` — wowbot.log рядом с exe

- **WowBot.Injector** — WPF приложение (net8.0-windows, x86)
  - `MainWindow.xaml.cs` — Attach/Detach, Lua Console, Dump, update loop 200мс
  - `OverlayWindow.xaml.cs` — прозрачный оверлей: секции Rotation/AoE/Buffs/Follow/Target
  - `Icons/` — иконки спеллов (jpg, подхватываются wildcard из .csproj)
  - app.manifest — requireAdministrator

### Как это работает
1. **ReadProcessMemory** — читаем данные из WoW (HP, мана, позиция, объекты)
2. **EndScene Hook** — codecave: x86 asm в память WoW, патчим EndScene (D3D9)
3. **D3D9Helper** — если стандартный поиск не нашёл EndScene → фейковое D3D9 устройство
4. **Lua_DoString** — вызываем WoW Lua из главного потока через хук
5. **BotEngine** — каждые 150мс: face target + execute Lua rotation/buffs
6. **SpellFlagsLua** — тоглы из UI передаются как `WB_S={VT=true,...}` перед скриптом

## Реализованные ротации (5 спеков)
1. **Balance Druid** — Eclipse tracking, DoTs, Starfall, Treants
2. **Shadow Priest** — VT/DP/SWP (2-sec double-cast guard), MB, MF, Dispersion
3. **Demo Warlock** — Meta, Life Tap, DoTs, Curse radio (CoA/CoD/CoE), Decimation/Molten Core procs
4. **Ret Paladin** — FCFS: Judge→DS→CS→Cons→Exo (AoW proc)→HoW (<20%)
5. **Holy Paladin** — хилер: поиск дохлого в группе/рейде, Beacon/SS на фокус, правосудие

## Система баффов (BuildBuffScript)
Генерирует однострочный Lua (многострочный триггерит taint WeakAuras). Порядок:
1. Аура (Палладин, 6 вариантов, радио)
2. Печать (Палладин, радио: мщения/повиновения для рет, мудрости/Света для хпал)
3. Благословение (Палладин, радио: могущества/королей/мудрости + Великое если реагент)
4. Камень чар (Варлок: проверка→создание→применение)
5. Self-баффы (Прист: Стойкость/Дух; Друид: Дар и т.д.)
6. Рейд-баффы (с проверкой реагентов и fallback на одиночные)

При смене выбора (печати/благо/ауры) — проверяет КОНКРЕТНЫЙ баф, мгновенно перебафает.

## UI — OverlayWindow
- **Радио-выборы**: Печать, Благословение, Аура, Правосудие (хпал), Проклятие (варлок)
- **Тоглы спеллов**: иконки 34x34, per-spec
- **Слайдеры**: мана-пороги, дистанция follow, дальность таргета
- **Persist**: settings.json рядом с exe (позиция окна, все тоглы, слайдеры, радио-выборы)

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

### Глобальные указатели — стандартные
- ClientConnection = 0x00C79CE0, ObjMgr = +0x2ED0
- Position X/Y/Z = +0x798/+0x79C/+0x7A0, Facing = +0x7A8
- CastId = +0xA60, ChannelId = +0xA6C
- CTM_Base = 0x00CA11D8

### Русский клиент — названия спеллов
- **НИКОГДА не использовать букву ё** — в клиенте все "е"
- **Регистр важен!** "Правосудие света" (маленькая с), "Печать Света" (большая С)
- Перед добавлением спелла — сверять со скриншотом спеллбука
- "Звездный огонь" НЕ "Звёздный огонь"
- "Облик лунного совуха" НЕ "Лунный совух"

### Lua API нюансы WoWCircle
- `BOOKTYPE_SPELL` не определен — использовать строку `'spell'`
- `GetLocalizedText` (0x007225E0) КРАШИТ WoW — НЕ использовать!
- `EditMacro` НЕ работает в бою — protected function
- `GetSpellInfo(spellId)` работает — через него сканируем названия спеллов

### EndScene Hook
- Может иметь модифицированный пролог — CalculateStolenBytes разбирает инструкции
- Относительные инструкции (call/jmp rel32) нужно фиксить при копировании
- D3D9Helper: fallback через фейковое устройство (для ПК с оверлеями)
- Верификация: после установки хука тест `local x=1` (500мс)
- Автоскан оффсетов 0x2000–0x5000 если стандартный не подошёл
- Lua буфер: 16384 байт (скрипт ~8KB не влезал в 8192)

### Сборка
- Publish блокируется если exe запущен — taskkill перед сборкой
- Собирать в одну папку `publish` (не плодить publish2, publish3...)
- Каждый publish ~150МБ (self-contained)

## ПРАВИЛА ДЛЯ АВТОФИКСА (Telegram бот)

Когда тебя вызывают через Telegram бот (@clwowbot) для фикса бага:

1. **МИНИМАЛЬНЫЕ ИЗМЕНЕНИЯ** — фикси ТОЛЬКО баг. Не рефактори, не реструктуризируй, не выделяй в новые файлы.
2. **НЕ создавай новые файлы** — правь существующие. Архитектура уже устоялась.
3. **Проверяй компиляцию** — после фикса проверь: `dotnet build WowBot.sln` (если ошибки — исправь).
4. **Spell ID** — все ротации на spell ID через `Cast(id)`, `IR(id)`, `HB(id)`, `HD(u,id)`. НЕ использовать русские названия в новом коде.
5. **Названия спеллов** — если нужно имя, `GetSpellInfo(spellId)` через хелпер `SN(id)`.
6. **Класс-изоляция** — палладинские фичи (seal/blessing/aura) ТОЛЬКО для PALADIN, курсы ТОЛЬКО для WARLOCK.
7. **Тестировщики — не разработчики** — отвечай просто и понятно, без технических деталей.
8. **Логи** — если тестировщик скинул wowbot.log, ОБЯЗАТЕЛЬНО прочитай его (Read tool) перед диагностикой.
9. **Авто-ревью** — после больших изменений (3+ файлов или новая система) ОБЯЗАТЕЛЬНО запусти `/code-review` перед коммитом. Это ловит баги типа undefined functions, неправильные spell ID, забытые проверки.

### Структура кода (НЕ МЕНЯТЬ):
- `AllRotations.cs` — ВСЕ ротации в одном файле, per-class методы, spell ID хелперы (SN/Cast/IR/HB/HD/NR)
- `BotEngine.cs` — координатор: таймер, follow, rotation, buffs — ОДИН файл
- `CombatPositioning.cs` — позиционирование в бою (MoveBehind, RangedPos)
- `OverlayWindow.xaml.cs` — ВСЕ UI в одном файле
- `EndSceneHook.cs` — хук D3D9, НЕ трогать без крайней необходимости

## Известные проблемы
- Break-CC: расовые не срабатывают — UnitRace на WoWCircle возвращает неизвестное значение (TODO)
- Instants на бегу без поворота к таргету (по дизайну)
- У брата другой ПК — EndScene автоскан нашёл ложные кандидаты
- AutoPve (босс-тактики) — код есть, но не проверен в рейде (нужны логи)
- AoE: лужи без DynObject (некоторые боссы/треш) не детектятся — нужен fallback

## NPCBots Reference
Репа: `/c/Проекты/npcbots/` — TrinityCore 3.3.5a с NPCBots (C++)
План: `TODO_NPCBOTS.md` — 8 фаз (позиционирование✓, break-cc~, хил, танк, interrupt, dispel, consumables, боссы)

## WoWCircle кастомные оффсеты (не только дескрипторы!)
- LastHardwareAction = **0x00B499A4** (стандарт: 0x00B4999C, сдвиг +8)

## Реализовано
- [x] **20+ ротаций** — все классы, все спеки PvE
- [x] **Система баффов** — per-class (seal/blessing/aura, проклятия, стойки, оружие шамана)
- [x] **UI оверлей** — тоглы спеллов, иконки, радио-выбор, слайдеры
- [x] **Persist настроек** — settings.json по нику персонажа
- [x] **D3D9Helper** — фейковое D3D9 устройство
- [x] **LuaReader** — авто-создание макроса, двухпроходный скан
- [x] **Логирование** — wowbot.log
- [x] **Telegram бот** — @clwowbot, баг-репорты + Claude Code фикс
- [x] **update.exe** — обновлялка с VPS
- [x] **Глобальный exception handler** — не крашит приложение
- [x] **Мультибоксинг (Hivemind)** — MasterPanel, per-slave управление, GUID, бафы
- [x] **Hivemind команды** — Attack/Follow/Stop/Auto + sub-toggles, Scatter/Stack, Interact/Gossip
- [x] **AntiAFK** — запись TickCount в 0x00B499A4 + Lua снятие (невидимый, без движения)
- [x] **Навигация** — Ctrl+ПКМ отправка слейвов в точку, дропдаун выбора
- [x] **AutoPve** — BossTactics (Лорд Ребрад, код написан)
- [x] **Горячие клавиши** — RegisterHotKey в MasterPanel
- [x] **Spell ID система** — все ротации на GetSpellInfo(id), хелперы SN/Cast/IR/HB/HD/NR
- [x] **Ground AoE** — Гроза (друид), Семя порчи (варлок) через TerrainClick/спам
- [x] **Бурсты** — отдельная секция в UI, per-spec, по дефолту OFF
- [x] **IsMounted()** — не кастует на коне
- [x] **Нативный CTM** — CGPlayer_C__ClickToMove (0x727400) через EndScene хук, без cold start
- [x] **Нативный FaceTarget** — clickType=1 с GUID, сервер синхронизирован (убран TurnLeft хак)
- [x] **AoE Avoidance** — дебаф-детекция + DynObject, SafeAoeDebuffs (Осквернение ДК)
- [x] **ACK система** — seq numbers, retry 500мс, гарантированная доставка команд
- [x] **CommandSource/FollowTarget** — разделение: мастер командует, follow target отдельно
- [x] **Позиционирование** — MoveBehind (мили за спину), RangedPos (ранж сбоку)
- [x] **Break-CC** — автоснятие контроля (расовые + классовые + тринкет), 50+ CC spell ID
- [x] **Пет варлока** — радио-выбор, авто-призыв, dismiss+resummon при смене
- [x] **Precast refresh** — NR() хелпер, перекаст дота до окончания
- [x] **Демо лок NPCBots** — survival/мана/угроза/проки из bot_warlock_ai.cpp
- [x] **Перчатки use** — авто-детект + тогл в бурстах
- [x] **pcall обёртка** — ловит Lua ошибки в WB_ERR

## Планы
- [ ] Дубовая кожа по HP
- [ ] AutoPve — дебаг + остальные боссы
- [ ] Ротация элема, кота
- [ ] Группировка слейвов по ролям (танки/хилы/мдд/рдд)
