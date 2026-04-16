---
title: Buff System
updated: 2025-04-15
tags: [architecture, buffs]
---

# BuffManager

Вызывается из `BotEngine.BuffTick()` каждый тик. Единый для solo и slave.

## Что делает
- Seal/Blessing/Aura (Paladin)
- Shout/Stance (Warrior)
- Totems (Shaman) — через SetMultiCastSpell (3 набора)
- Pet management (Warlock, Hunter, DK)
- Presence (DK)
- Form (Druid)

## Принцип
- Баффы проверяются ВНЕ боевой ротации
- Не мешают ротации (отдельный тик)
- Некоторые баффы кастуются только вне боя

## Связи
- [[combat-system]] — CombatExecutor НЕ занимается баффами
