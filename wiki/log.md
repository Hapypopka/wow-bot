# Log

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
