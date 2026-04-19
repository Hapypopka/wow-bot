# WowBot Wiki — Index

## Обзор
- [[overview]] — проект, стек, архитектура, известные проблемы

## Архитектура
- [[combat-system]] — CombatExecutor, CombatOptions, Lua переменные (WB_NCE/NCET)
- [[hivemind]] — координация мультибокса (master/slave)
- [[buff-system]] — BuffManager (seal/blessing/aura/totems/pet)
- [[lua-engine]] — EndSceneHook, D3D9 инжекция, Lua буфер 32KB
- [[navigation]] — CTM, MoveBehind, NavEngine (патхфайндинг), ObjectManager
- [[telegram-bot]] — @clwowbot, баг-фикс пайплайн через Claude Code CLI
- [[boss-engine]] — BossEngine, CLEU listener, MarrowgarTactic
- [[updater]] — GitHub Releases, patch.zip, бэкап/откат
- [[overlay-ui]] — WPF оверлей, настройки per-character, spell toggles

## Классы
- [[warrior]] — Arms/Fury/Prot. AoE: Prot Thunder Clap
- [[paladin]] — Ret(C#)/Prot/Holy. AoE: Prot Consecration
- [[death-knight]] — Blood/Frost/Unholy. AoE: Blood DnD, Unholy Pest+DnD
- [[hunter]] — BM/MM/Surv. AoE: MM Multi-Shot + Volley
- [[warlock]] — Affli/Demo/Destro. AoE: Demo Seed of Corruption
- [[shaman]] — Ele/Enh/Resto. AoE: Enh Chain Lightning
- [[druid]] — Balance/Cat/Bear/Resto. AoE: Cat Swipe, Bear Swipe, Balance Hurricane
- [[mage]] — Arcane/Fire/Frost. AoE: нет
- [[rogue]] — Assa/Combat/Sub. AoE: нет
- [[priest]] — Shadow/Disc/Holy. AoE: Shadow MultiDot, Holy Circle of Healing

## Концепции
- [[aoe-system]] — подсчёт врагов (WB_NCE/WB_NCET), ground AoE, avoidance (grid safe spot + native stop)
- [[in-frame-mode]] — кнопка "Во фрейм": слейвы встают сзади таргета мастера (Marrowgar-style)
- [[mark-logs]] — пометка интервалов в логе (кнопка start/stop), `wowbot_<Char>_mark.log`
- [[spell-ids]] — система Spell ID, WB_S.* флаги
- [[lua-helpers]] — LuaHelpers (Cast/IR/HB/HD/SN), wrappers (WrapDPS/WrapHealer)
- [[wowcircle-quirks]] — специфика WoWCircle (ё→е, GetLocalizedText крашит, оффсеты)

## Решения
- [[2025-04-15-wb-ncet]] — WB_NCET: подсчёт врагов рядом с таргетом для рейнж DPS
- [[2025-04-16-own-aoe-filter]] — Фильтр своих AoE в Avoidance (Hurricane/Volley/DnD/Consecration)

## Источники
- [[npcbots]] — TrinityCore NPCBots AI, что портировано, что можно портировать
