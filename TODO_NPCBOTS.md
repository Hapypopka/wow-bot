# План: фичи из NPCBots

Источник: `/c/Проекты/npcbots/src/server/game/AI/NpcBots/`

## Фаза 1 — Позиционирование (HIGH) ✅
- [x] **MoveBehind** — мили ДПС заходит за спину таргета
- [x] **RangedPos** — ранж ДПС встаёт сбоку-сзади
- [x] **Чекбокс "Забегание за спину"** — per-spec дефолты
- [ ] **AdjustTankPosition** — танк разворачивает мобов лицом от группы

## Фаза 2 — Break-CC (HIGH) ~80%
- [x] **Классовые антистан** — Берсерк Рейдж, Icebound Fortitude, Blink, Cloak, Hand of Freedom
- [x] **Расовые** — Every Man, Will of Forsaken, Escape Artist (частично — UnitRace баг)
- [x] **PvP тринкет** — слоты 13/14 fallback
- [x] **50+ CC spell ID** — Fear, Stun, Root, Polymorph, Sleep, Horror + ICC боссы
- [ ] **Дебаг UnitRace** на WoWCircle (Human расовая не срабатывает)

## Фаза 3 — Умный хил (HIGH) ✅
- [x] **Predicted HP** — реальная скорость потери между тиками (WB_HPT)
- [x] **Weighted target selection** — танк вес 0.7, urgency levels 0/1/2
- [x] **Проактивные HoT** — Rejuv/Riptide на танка, раскидка Rejuv по рейду
- [x] **Mass heal** — CoH, Wild Growth, Chain Heal при 2+ раненых
- [x] **Dispel союзников** — per-class (Cleanse, Dispel Magic, Remove Curse, Cleanse Spirit)
- [x] **Resurrect вне боя** — авторес с guard 60с, тогл "Авторес"
- [x] **Innervate** — только на хилеров с маной < 20%
- [x] **Inner Focus** — перед большим хилом (прист)
- [x] **Prayer of Mending** — проактивно на танка
- [x] **Beacon/Sacred Shield** — автоматически на танка (без фокуса)
- [x] **CastOn()** — хил через RunMacroText без смены таргета
- [x] **Хил петов** — только когда все игроки на фуле
- [x] **Regrowth проверка HoT** — не дублирует
- [x] **Idle хил** — Rejuv→Regrowth(танки)→WG когда все на фуле
- [x] **Lua буфер 32KB** — хилерские скрипты ~16KB
- [ ] **Barkskin** — авто при низком HP (рдруид)
- [ ] **Tranquility** — mass emergency heal (3+ людей < 50%)
- [ ] **Overheal prevention** — не хилить если heal > потеря HP

## Фаза 4 — Танк-логика (HIGH)
- [ ] **Distant Taunt** — таунт мобов которые бьют хилера/ДД
- [ ] **AoE Threat** — Thunder Clap / Consecration если 3+ мобов
- [ ] **Defensive CD chaining** — Shield Wall → Last Stand → Shield Block по очереди

## Фаза 5 — Прерывание кастов врагов (MEDIUM)
- [ ] **Kick/Counterspell/Mind Freeze** — автоматический interrupt кастующего врага
- [ ] **Fear на кастера** — если нет interrupt → CC
- [ ] **Приоритет interrupt** — опасные касты > обычные

## Фаза 6 — Hostile Dispel (MEDIUM)
- [ ] **Hostile dispel** — снятие баффов с врагов (Purge, Spellsteal)

## Фаза 7 — Consumables (LOW)
- [ ] **Авто-поты** — HP potion < 40%, Mana potion < 30%
- [ ] **Авто-еда** — вне боя если HP < 70%

## Фаза 8 — Босс-механики (LOW)
- [ ] **ICC Marrowgar** — свитч на Bone Spikes
- [ ] **ICC Sindragosa** — свитч на Ice Tombs, стоп на 50%
- [ ] **ICC Lich King** — свитч на Valkyrs
- [ ] **Общий паттерн** — выход из войдзон (AoE detection)

## Текущие баги/TODO
- [ ] Break-CC: UnitRace Human не работает на WoWCircle
- [ ] CTM холодный старт: PostMessage warmup иногда чуть убегает
- [ ] Tree of Life: CastSpellByName не кастует shapeshift формы через хук
- [ ] Lua консоль: ExecuteLuaWithResult иногда null (PauseTick помогает частично)
