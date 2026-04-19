# Log

## [2026-04-19] feat | Mark logs — кнопка пометки интервалов
- Кнопка-кружок (12x12) в шапке оверлея и мастер-панели. Серый → клик → красный (mark активен) → клик → серый.
- Logger.StartMark/StopMark открывают/закрывают `wowbot_<Char>_mark.log`. Пока активен — каждая запись в основной лог дублируется в mark-файл.
- Hivemind: `Command.MarkStart/MarkStop` (service-команды без ACK). Кнопка в мастер-панели → broadcast всем слейвам через `CmdMarkStart/CmdMarkStop`. У каждого перса свой mark-файл в одном временном окне.
- Фикс: `ParseSlaveResponse` не имел случаев для новых команд — слейв не парсил. Добавлены `"MarkStart" => Command.MarkStart, "MarkStop" => Command.MarkStop`.
- Зачем: бот может работать сутками, tail основного лога даёт картину НЕ того момента когда юзер видит баг. Mark-файл = только помеченный отрезок.
Затронуты: [[mark-logs]], [[hivemind]], [[overlay-ui]]

## [2026-04-19] refactor | AoeAvoid — UI vs Tactic override
- `BotEngine.AoeAvoidEnabled` теперь computed property: `AoeAvoidTacticOverride ?? AoeAvoidUiEnabled`.
- UI пишет в `AoeAvoidUiEnabled`. Тактика (например MarrowgarTactic) — `ctx.SetAoeAvoid(true)` ставит override, `ctx.ClearAoeAvoid()` снимает.
- Зачем: `MainWindow.xaml.cs` каждый тик синхронизировал `AoeAvoidEnabled = _overlay.AoeAvoidEnabled` → перетирал значения тактики каждый тик. Во время Bone Storm Маровгара слейвы не уклонялись от Coldflame: `BossTactic: AoE avoid → ON` логировалось каждый тик (мой код пытался включить, UI сбрасывал).
- `_savedAoeAvoid` в MarrowgarTactic снесён — override сам "помнит" состояние.
- StormGraceSec 15→2.5: после исчезновения ауры Bone Storm раньше ждали 15 сек (страховка от мигания) — слишком долго, не успевали переключиться до OUT_OF_COMBAT.
Затронуты: [[aoe-system]], [[boss-engine]], [[overlay-ui]]

## [2026-04-19] feat | Native CTM Stop + адаптивный ring radius для больших луж
- EndSceneHook flag=7: thiscall PlayerClickToMoveStop (0x0072B3A0) — native функция клиента, шлёт MSG_MOVE_STOP серверу с актуальной позицией. Лечит movement desync когда aura на сервере продолжала тикать после визуального выхода из лужи (старый Stop писал ActionStop=0xD в память, сервер не знал).
- ClickToMove.NativeStop() → `TryAoEAvoidance` при выходе из flee вызывает её → server sync мгновенный → тики урона прекращаются сразу.
- AoEGridRingRadius адаптивный: `max(8, largestDangerRadius + 3)`. При центровом касте DnD 10y все 8y кандидаты были внутри зоны → бот не видел "чистой земли". Теперь кольцо всегда больше лужи.
- AoESafetyCap 6→2.5y (бот выбегает тесно, без лишнего хода).
Затронуты: [[aoe-system]], [[navigation]]

## [2026-04-19] feat | Режим "Во фрейм" (InFrame) + фиксы slave attack
- Кнопка ◎ в MasterPanel (trigger, не toggle). Слейвы встают на краю хитбокса таргета мастера (`max(BR+4, 8)`), прямо сзади (`target.Facing + π`).
- Координаты фиксируются в момент команды (InFrameLockedPos), не пересчитываются. Босс двигается → слейв стоит. Авто-сброс при любой другой команде от мастера.
- Фикс: SlaveAttackTick убрал верхний гейт `!InCombat` → слейвы бьют Bone Spike / feign death цели. Добавил `NoCombatCheck` флаг в CombatOptions (ExecuteCombatTick проверяет InCombat только если NoCombatCheck=false).
- Фикс: `hasAutoTarget = ... && (!IsNotAttackable || InCombat)` — цели под feign death aura продолжают атаковаться если уже в бою (убрало переключение follow↔attack каждую секунду).
- Фикс: race condition в `ObjectManager.Update()` (UI крашился на Units.ToList() во время Clear+Add). Теперь списки пересобираются атомарно — UI всегда видит консистентный snapshot.
Затронуты: [[in-frame-mode]], [[hivemind]], [[combat-system]]

## [2026-04-19] fix | NpcId читался с неправильного индекса
Baseline `ReadDescriptorInt(0x06)` возвращал 0 для всех NPC → BossEngine не мог матчить фабрику тактик. Правильный индекс для OBJECT_FIELD_ENTRY = 3 (GUID[0-1], TYPE[2], ENTRY[3], SCALE[4], PADDING[5], OBJECT_END=6). После фикса MarrowgarTactic активируется корректно.
Затронуты: [[boss-engine]]

## [2025-04-17] feat | AoE ротации для всех классов + процы
Большая актуализация: добавлены AoE-ветки для ВСЕХ классов где их не было + оптимизация процов.
- Rogue (все 3 спека): Fan of Knives (51723) + Blade Flurry для Combat
- Mage Fire: Flamestrike (ground AoE) + Blast Wave + Dragon's Breath
- Mage Frost: Blizzard (ground AoE) + Cone of Cold
- Mage Arcane: Arcane Explosion + Blizzard (ground AoE)
- Warrior Arms: Bladestorm + Sweeping Strikes + Cleave (>=2)
- Warrior Fury: Whirlwind + Cleave (>=2)
- Warlock Affli: Seed of Corruption + Drain Soul execute
- Warlock Destro: Seed of Corruption
- Hunter BM: Multi-Shot (через WB_NCET)
- Hunter Survival: Lock and Load (56453) proc
- DK Frost: Howling Blast + Blood Boil + DnD (AoE приоритет)
- DK Unholy: Sudden Doom (49530) free Death Coil
- Shaman Elemental: Magma Totem + Fire Nova + Chain Lightning (>=2)
- Mage Frost: Deep Freeze требует Fingers of Frost (44544) proc

Ground AoE расширен: Flamestrike (42926), Blizzard (42940) — по аналогии с Hurricane/Volley.
Затронуты: [[rogue]], [[mage]], [[warrior]], [[warlock]], [[hunter]], [[death-knight]], [[shaman]], [[aoe-system]]

## [2025-04-17] fix | WowDynObject.Caster — правильный оффсет +0x18
Предыдущий фикс (пропуск своих AoE) не работал: Caster читался из +0x00, где на WoWCircle лежит GUID самого DynObject, а не кастера. Настоящий Caster — по +0x18. Найдено через диагностический dump дескриптора.
Затронуты: [[wowcircle-quirks]], [[2025-04-16-own-aoe-filter]]. Протестировано на сове — Гроза каста́ется полный канал.

## [2025-04-16] fix | AoE Avoidance игнорирует свои заклинания
Баг: сова кастит Hurricane → DynObject + aura channel с одним SpellId → Avoidance убегала из своей же Грозы, канал прерывался.
Фикс: `TryAoEAvoidance` пропускает DynObject где `Caster == player.Guid`. Одна строка фиксит для всех (Hurricane, Volley, DnD, Consecration, RoF).
Затронуты: [[aoe-system]], [[2025-04-16-own-aoe-filter]]

## [2025-04-15] ingest | Инициализация вики
Создана структура вики по паттерну LLM Wiki (Karpathy). Seed-страницы из существующих знаний проекта.
Затронуты: все начальные страницы

## [2025-04-15] fix | WB_NCET — подсчёт мобов рядом с таргетом
Баг: WB_NCE считал мобов в 10yd от игрока, а не от таргета. Рейнж DPS (Hunter, Warlock) стоят в 25-30yd от мобов — WB_NCE всегда 0, AoE не срабатывает.
Фикс: добавлена переменная WB_NCET (enemies near target). DPS ротации переведены на WB_NCET, танки остались на WB_NCE.
Затронуты: [[aoe-system]], [[hunter]], [[warlock]], [[death-knight]], [[shaman]], [[druid]]
