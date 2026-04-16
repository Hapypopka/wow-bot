# Log

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
