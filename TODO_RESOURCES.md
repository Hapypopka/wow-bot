# Скачанные ресурсы и план использования

## Репозитории

| Репо | Путь | Назначение |
|------|------|-----------|
| **AmeisenBotX** | `/c/Проекты/AmeisenBotX/` | C# бот — FSM, combat, dungeon profiles, AoE avoidance |
| **AmeisenNavigation** | `/c/Проекты/AmeisenNavigation/` | TCP навигация Recast/Detour для данжей |
| **SpellWork** | `/c/Проекты/SpellWork/` | Просмотрщик spell ID, эффектов, аур 3.3.5a |
| **DBM-Warmane** | `/c/Проекты/DBM-Warmane/` | Босс-механики ICC (Lua файлы с таймерами) |
| **BloogBot** | `/c/Проекты/BloogBot/` | Обучающий бот, туториал 20+ глав |
| **SimCraft WotLK** | `/c/Проекты/SimCraft_WotLK/` | Симуляция DPS для проверки ротаций |
| **WoW MMaps Data** | `/c/Проекты/WoW-MMaps-Data/` | Навмеш данные для навигации (2.1 GB, 3780 файлов) |
| **NPCBots** | `/c/Проекты/npcbots/` | Серверный AI — ротации, хил, танк, позиционирование |

---

## Исследованные детали

### AmeisenBotX — ключевые файлы

**Stuck Recovery** (MovementEngine.cs:80-450):
- `UnstuckStage` enum: None → Jump → Strafe → Reverse → PathReset
- Каждые 500мс проверяется `CurrentSpeed < 0.1f` при наличии PathQueue
- `_stuckCounter` >= 3 → эскалация: прыжок → стрейф рандом → назад → полный ресет пути
- Стрейф валидируется через навмеш (`MoveAlongSurface`)
- Файл: `AmeisenBotX.Core/Engines/Movement/MovementEngine.cs`

**AoE Avoidance через DynObject** (MovementEngine.cs:295-328):
- `AvoidAoeStuff()` — берёт DynObjects из ObjectManager
- `Bot.GetAoeSpells(position, extends)` — фильтрует hostile DynObjects
- Считает mean позицию всех AoE → вычисляет вектор отталкивания (repell)
- `newPosition = meanAoePos + (repellDirection * distanceToMove)`
- У нас уже есть AoE Avoidance через дебаф-детекцию — можно усилить DynObject подходом

**Dungeon Profiles** (Engines/Dungeon/):
- `IDungeonProfile` — интерфейс: Name, MapId, Nodes, DungeonExit, WorldEntry, PriorityUnits
- `DungeonNode(Vector3 position, DungeonNodeType type)` — Normal/Boss/Use/Collect/Door/Jump
- Профили — захардкожены как List<DungeonNode> с координатами
- 16 профилей (Classic + TBC + WotLK данжи: FoS, PoS, UK, HoL, AN...)
- `DefaultDungeonEngine` — BehaviorTree: идёт по нодам, ждёт группу, следует за лидером

**Movement Jittering / Humanization** (BasicVehicle.cs + MovementSettings.cs):
- `_targetRandomizer` — рандом смещение ±0.5 при dist>15
- Рандом прыжки при движении (1% шанс, ~30с интервал)
- Turn speed variation ±2.0, arrival distance variation +0.15
- Настройки: `EnableRandomJumps`, `RandomJumpChance`, `EnableMovementVariation`

**Smart Loot** (Logic/Services/LootService.cs):
- Очередь `UnitsToLoot` — сканирует dead+lootable юнитов
- Проверка сумок (`FreeBagSlots`), авто-удаление мусора (`TrashItemsRoutine`)
- Таймаут на лут (3 попытки), скиннинг
- Loot window detection через memory offset

**Combat AI** (Engines/Movement/AI/):
- `CombatLearner` — нейросеть для предсказания исхода боя (!)
- `CombatSnapshot` — снимок состояния боя
- KNN + Multi-Head Neural Network — предсказание Win Probability
- Это overkill для нас, но интересная идея

### AmeisenNavigation — архитектура

**TCP сервер** (C++, порт 47110):
- Протокол: `AnTCP` — бинарный, message type byte + struct data
- Сервер: `AmeisenNavigation.Server/src/Main.cpp`
- Навигация: `AmeisenNavigation/src/AmeisenNavigation.hpp` — Detour queries

**C# клиент** (`AmeisenNavigation.Client/AmeisenNavClient.cs`):
- `GetPath(mapId, start, end, flags)` → Vector3[]
- `GetRandomPath(mapId, start, end)` → рандомизированные вейпоинты (human-like)
- `MoveAlongSurface(mapId, start, end)` → clamped позиция на навмеше
- `CastRay(mapId, start, end)` → bool (проверка видимости)
- `GetRandomPointAround(mapId, pos, radius)` → случайная точка
- `GetHeight(mapId, pos)` → высота навмеша
- Auto-reconnect, area costs, faction-based filtering

**Wire Format** (`WireFormat.cs`):
- `PathRequestData` { MapId, Flags, Start(Vector3), End(Vector3) }
- `MoveRequestData` { MapId, Start(Vector3), End(Vector3) }
- `RandomPointAroundData` { MapId, Start(Vector3), Radius }

**MMap формат** (MmapNavSource.hpp):
- TC335A: `{mapId:03}.mmap` + `{mapId:03}{x:02}{y:02}.mmtile`
- Автодетект формата, lazy loading тайлов
- Path smoothing: Bezier, CatmullRom, Chaikin curves

**WoW-MMaps-Data**: 2.1 GB, 3780 файлов, формат TC335A (000.mmap, 0002239.mmtile...)

### DBM-Warmane — ICC боссы

**Структура**: `/DBM-Icecrown/` по крыльям:
- **TheLowerSpire/**: LordMarrowgar, Deathwhisper, GunshipBattle, Deathbringer
- **ThePlagueworks/**: Festergut, Rotface, Putricide
- **TheCrimsonHall/**: BPCouncil, Lanathel
- **FrostwingHalls/**: Sindragosa, Valithria
- **TheFrozenThrone/**: LichKing (+LichKingFrame.lua/xml)

**Формат данных** (пример Lord Marrowgar):
- `CreatureID = 36612`, `EncounterID = 845`
- **Spell IDs**: 69076 (Bone Storm), 69057/70826/72088/72089 (Bone Spike), 69146/70823-70825 (Coldflame), 69065 (Impaled), 72669/72670 (Summon spikes)
- **Таймеры**: BoneSpike каждые 15-20с, Bone Storm CD = 90с, первый Storm = 45с, Storm длится 20-30с
- **Фазы**: normal → Bone Storm → normal (по SPELL_AURA_APPLIED/REMOVED)
- **Warnings**: Coldflame = GTFO, Bone Storm = Run away
- **Бесноватость**: 600с (360с на Lordaeron/Frostmourne)

---

## План — что забрать

### 1. Stuck Recovery (HIGH, ~2ч)
**Источник**: AmeisenBotX MovementEngine.cs
**Адаптация**: Добавить в BotEngine.cs после follow-логики
- Проверять скорость перемещения (позиция не меняется > 1.5с)
- Эскалация: Jump (Lua) → Strafe (CTM влево/вправо) → Reverse (CTM назад) → PathReset
- У нас нет навмеша → стрейф просто рандом ±4yd от текущей позиции
- [ ] Реализовать StuckRecovery класс в WowBot.Core/Game/

### 2. AoE Avoidance улучшение (MEDIUM, ~3ч)
**Источник**: AmeisenBotX AvoidAoeStuff() 
**Адаптация**: У нас уже есть дебаф-детекция — добавить DynObject как второй источник
- Сейчас: детектим по дебафам на игроке + DynObject по спискам
- Добавить: считать mean AoE позицию → вектор отталкивания (как у Ameisen)
- [ ] Улучшить AoE Avoidance вектором отталкивания от центра масс AoE

### 3. Навигация Recast/Detour (HIGH, ~8-16ч)
**Источник**: AmeisenNavigation (C++ сервер) + WoW-MMaps-Data
**Адаптация**: Масштабная задача!
- Собрать C++ сервер (нужен CMake + Visual Studio C++)
- Написать C# клиент (скопировать AmeisenNavClient.cs, адаптировать)
- Подключить к BotEngine: follow через навмеш, Stuck Recovery через `MoveAlongSurface`
- Данжон профили станут возможны только с навигацией
- [ ] Собрать AmeisenNavigation сервер
- [ ] Написать NavClient для WowBot
- [ ] Интегрировать в BotEngine (follow path, stuck recovery)

### 4. Dungeon Profiles (MEDIUM, после навигации)
**Источник**: AmeisenBotX Engines/Dungeon/
**Адаптация**: Формат простой — список координат + тип (Normal/Boss/Jump)
- Нужна навигация для перемещения между нодами!
- Можно начать с записи координат (macro-recorder в overlay)
- [ ] Формат профиля: JSON список {x,y,z,type}
- [ ] Запись профиля из оверлея (кнопка Record)
- [ ] Движок проигрывания профиля

### 5. ICC Boss Tactics для AutoPve (MEDIUM, ~4-6ч)
**Источник**: DBM-Warmane/DBM-Icecrown/
**Адаптация**: Парсить Lua файлы → извлечь spell IDs, таймеры, фазы
- 12 боссов ICC полностью покрыты в DBM
- Данные включают: точные spell ID, таймеры с вариацией, фазовые переходы
- Формат нашего BossTactics уже есть — нужно заполнить данными
- [ ] Парсить Marrowgar → BossTactics (pilot)
- [ ] Festergut, Rotface, Putricide
- [ ] Sindragosa, Lich King
- [ ] Остальные ICC боссы

### 6. Smart Loot (LOW, ~2ч)
**Источник**: AmeisenBotX LootService.cs
**Адаптация**: Через Lua (`InteractUnit`, `LootSlot`)
- Сканировать dead+lootable юнитов в ObjectManager
- Подойти → InteractUnit → LootSlot(1..n) → CloseLoot
- Проверка сумок через Lua `GetContainerNumFreeSlots`
- [ ] Реализовать авто-лут

### 7. Movement Humanization (LOW, ~1ч)
**Источник**: AmeisenBotX BasicVehicle.cs + MovementSettings.cs
**Адаптация**: Рандом прыжки + small position offsets
- Рандом прыжки каждые 30-60с при движении (не в бою)
- Небольшое смещение target позиции (±0.5yd)
- [ ] Добавить рандом прыжки в follow
- [ ] Добавить позиционный jitter

### ОТЛОЖЕНО
- **Vendor/Repair** — нужна навигация до NPC
- **DPS/DTPS Tracking** — у Ameisen нейросеть, overkill для нас
- **SimCraft** — проверка ротаций через симуляцию (потом)
- **SpellWork** — уже используем spell ID, но полезен для верификации
