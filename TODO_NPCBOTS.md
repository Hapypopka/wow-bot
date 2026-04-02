# План: фичи из NPCBots

Источник: `/c/Проекты/npcbots/src/server/game/AI/NpcBots/`

## Фаза 1 — Позиционирование (HIGH)
- [ ] **MoveBehind** — мили ДПС заходит за спину таргета (rogue/cat чаще, воин реже)
- [ ] **CalculateAttackPos** — ранж ДПС встаёт на 8м сбоку/сзади (не в лоб врагу)
- [ ] **AdjustTankPosition** — танк разворачивает мобов лицом от группы

## Фаза 2 — Break-CC (HIGH)
- [ ] **Авто-антистан** — Берсерк Рейдж (воин), Every Man for Himself (тринкет), Воля нежити
- [ ] **Авто-антирут** — снятие корней по классу
- [ ] **Авто-антифир** — снятие страха (Берсерк Рейдж, Страж благочестия)

## Фаза 3 — Умный хил (HIGH)
- [ ] **Predicted HP** — предсказание HP через 2-3с (`current + dps_taken * 2.5`)
- [ ] **Weighted target selection** — танк приоритетнее ДД, низкий HP приоритетнее
- [ ] **Проактивные HoT** — Renew/Rejuv на танка заранее

## Фаза 4 — Танк-логика (HIGH)
- [ ] **Distant Taunt** — таунт мобов которые бьют хилера/ДД
- [ ] **AoE Threat** — Thunder Clap / Consecration если 3+ мобов
- [ ] **Defensive CD chaining** — Shield Wall → Last Stand → Shield Block по очереди

## Фаза 5 — Прерывание кастов врагов (MEDIUM)
- [ ] **Kick/Counterspell/Mind Freeze** — автоматический interrupt кастующего врага
- [ ] **Fear на кастера** — если нет interrupt → CC
- [ ] **Приоритет interrupt** — опасные касты > обычные

## Фаза 6 — Dispel система (MEDIUM)
- [ ] **Friendly dispel** — снятие дебаффов с союзников (Cleanse, Abolish Disease)
- [ ] **Hostile dispel** — снятие баффов с врагов (Purge, Spellsteal)

## Фаза 7 — Consumables (LOW)
- [ ] **Авто-поты** — HP potion < 40%, Mana potion < 30%
- [ ] **Авто-еда** — вне боя если HP < 70%

## Фаза 8 — Босс-механики (LOW)
- [ ] **ICC Marrowgar** — свитч на Bone Spikes
- [ ] **ICC Sindragosa** — свитч на Ice Tombs, стоп на 50%
- [ ] **ICC Lich King** — свитч на Valkyrs
- [ ] **Общий паттерн** — выход из войдзон (AoE detection)
