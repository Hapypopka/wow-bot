---
title: Hivemind
updated: 2025-04-15
tags: [architecture, hivemind, multibox]
---

# Hivemind — Координация мультибокса

## Режимы

| Режим | Что делает |
|-------|-----------|
| **Following** | Slave следует за мастером, кастует instant'ы в бою |
| **Attacking** | Slave атакует таргет мастера |
| **Auto** | Slave следует + автоматически атакует когда мастер в бою |

## Коммуникация
- Addon messages через hidden channel
- Master отправляет команды (Attack, Follow, Wipe)
- Slave отвечает ACK
- Register/Unregister при подключении

## Хилеры в Hivemind
- Хилер-slave **всегда** хилит группу (если не Wipe)
- В Auto/Attacking: позиционируется как DPS (HPal=мили, остальные=28yd)
- Вне боя: follow к мастеру

## Известные баги
- ACK иногда не доходит
- 2 хила могут кастить в одного (нет координации выбора цели)

## Планы
- Координация хилов — чтобы 2 хила не лечили одного
- Группировка слейвов по ролям (танки/хилы/мдд/рдд)

## Связи
- [[combat-system]] — slave использует тот же CombatExecutor
- [[overview]] — общая архитектура
