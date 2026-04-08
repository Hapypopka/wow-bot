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
| **WoW MMaps Data** | `/c/Проекты/WoW-MMaps-Data/` | Навмеш данные для навигации |
| **NPCBots** | `/c/Проекты/npcbots/` | Серверный AI — ротации, хил, танк, позиционирование |

## План — что спиздить

### Из AmeisenBotX (HIGH)
- [ ] **Stuck Recovery** — прыжок→стрейф→назад→ресет при застревании
- [ ] **AoE Avoidance через DynObject** — читать DynObject из ObjectManager, не белый список
- [ ] **Dungeon Profiles** — ноды: куда бежать, где драться, где лутать
- [ ] **DPS/DTPS Tracking** — скользящее окно 5с для реального predicted HP
- [ ] **Movement Jittering** — рандом позиции для хилов/ранжей (22±12yd)
- [ ] **Smart Loot** — авто-лут с приоритетом (деньги > квест > зелёное+)
- [ ] **Vendor/Repair** — авто-продажа/ремонт у NPC

### Из AmeisenNavigation (HIGH)
- [ ] **TCP навигация** — поднять сервер, C# клиент шлёт запросы пути
- [ ] **Интеграция с MMaps** — готовые навмеш данные из WoW-MMaps-Data

### Из DBM-Warmane (MEDIUM)
- [ ] **Парсинг босс-механик** — таймеры, фазы, spell ID для AutoPve
- [ ] **ICC профили** — Marrowgar, Sindragosa, Lich King и т.д.

### Из SpellWork (MEDIUM)
- [ ] **Быстрый поиск spell ID** — вместо гадания по именам
- [ ] **Эффекты и ауры** — проверить правильность наших проверок

### Из BloogBot (LOW)
- [ ] **Туториал навигации** — Recast/Detour интеграция
- [ ] **EndScene подход** — сравнить с нашим

### Из SimCraft (LOW)
- [ ] **Проверка ротаций** — симулировать DPS наших ротаций
