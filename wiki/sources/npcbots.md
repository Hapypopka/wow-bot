---
title: "Источник: NPCBots (TrinityCore)"
updated: 2025-04-15
tags: [source, npcbots, trinitycore, reference]
---

# NPCBots — TrinityCore серверный AI

## Что это
Серверный mod для TrinityCore 3.3.5a — ботовские NPC с полным AI для всех классов.
Репозиторий клонирован в `C:\Проекты\npcbots\`

## Ключевые файлы
- `src/server/game/AI/NpcBots/bot_ai.cpp` (935KB) — базовый AI
- `bot_{class}_ai.cpp` — AI каждого класса

## Что уже портировано в WowBot
- MoveBehind — позиционирование за спину
- RangedPos — позиция для рейнж
- Spell ID подход
- Частично Break-CC (без расовых)

## Что можно портировать
- Engage Timer — DPS/хилы ждут 3-5s для танк-треда
- Predictive Healing — прогноз HP через 2s
- Auto Dispel — парсинг дебаффов группы
- Defensive CD formula — порог = 30% + attackers*4% + boss*20%
- Smart Interrupt — по UnitCastingInfo, школа, иммун
- Threat Management — Feint/Fade/Salv
- DynamicObject AoE — лужи через ObjectManager
- CC на аддов — полиморф/фир не на основную цель

## Связи
- [[combat-system]] — основной потребитель паттернов
- [[aoe-system]] — DynObject AoE detection
