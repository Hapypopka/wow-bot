---
title: WowBot Overview
updated: 2025-04-15
tags: [overview, architecture]
---

# WowBot

PvE бот для WoW 3.3.5a (WoTLK), приватный сервер WoWCircle. Русский клиент, x86.

## Что умеет
- Ротации для всех 10 классов (C# + Lua fallback)
- Follow + автоатака (мультибоксинг)
- Хилбот (Holy Paladin, Resto Druid, Resto Shaman, Disc/Holy Priest)
- Hivemind — координация master/slave (follow, attack, auto mode)
- MoveBehind — позиционирование за спину таргета
- AoE Avoidance — убегание из луж (DynObject)
- Telegram бот @clwowbot — удалённое управление
- Авто-обновление через GitHub Releases

## Стек
- C# (.NET 8, win-x86) — инжектор, память, хук
- Lua — ротации выполняются в EndScene хуке WoW
- WPF — оверлей поверх WoW
- AmeisenNavigation — патхфайндинг через TCP сервер

## Архитектура v2
- [[combat-system]] — CombatExecutor (единый бой solo/slave)
- [[buff-system]] — BuffManager (seal/blessing/aura/totems)
- [[aoe-system]] — CombatHelper (подсчёт врагов, ground AoE, avoidance)
- [[hivemind]] — координация мультибокса
- [[lua-engine]] — EndSceneHook, Lua буфер 32KB
- [[navigation]] — NavEngine + CTM fallback

## Известные проблемы
- Break-CC: расовые не работают (UnitRace на WoWCircle)
- AutoPve: не оттестировано (нужны логи из рейда)
- AoE лужи без DynObject не детектятся
- FaceTarget: криповый поворот при большом угле
