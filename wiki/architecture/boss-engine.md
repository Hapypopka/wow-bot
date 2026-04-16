---
title: BossEngine
updated: 2025-04-15
tags: [architecture, boss, pve]
---

# BossEngine — Боссовые тактики

Файлы:
- `WowBot.Core/Game/BossEngine.cs` (274 строк)
- `WowBot.Core/Game/MarrowgarTactic.cs` (230 строк)

## Архитектура (v2, event-driven, как DBM)

1. **CLEU Listener** — Lua фрейм `COMBAT_LOG_EVENT_UNFILTERED`
   - Собирает spell ID из зарегистрированных тактик
   - Пишет в `WB_BOSS_EVT`: `"EVENT|spellId|srcName|dstName|isMe"`
2. **DetectBoss** — каждые ~1s ищет босса в ObjectManager по NPC ID
3. **ReadAndDispatchEvent** — каждые ~300ms читает WB_BOSS_EVT, передаёт в тактику
4. **TacticAction** — тактика возвращает действие: None, MoveTo, Flee, TargetSwitch, RotateAndAttack

## Зарегистрированные тактики

| Босс | NPC ID | Файл |
|------|--------|------|
| Lord Marrowgar (ICC) | 36612 | MarrowgarTactic.cs |

## MarrowgarTactic

**Фазы:**
- **Normal** — мили стекаются за спиной (танк 1yd, DPS 4yd). Перепозиция каждые ~1.5s
- **Bone Storm** — разбежаться 15+ ярдов от босса
- **Bone Spike** — не-танки переключаются на шип (NPC: 36619, 38712, 38711), убивают, возвращаются

**Спеллы:**
- Bone Storm: 69076 (SPELL_AURA_APPLIED/REMOVED)
- Bone Spike: 69057, 70826, 72088, 72089 (4 варианта сложности)
- Coldflame: 69146, 70823, 70824, 70825

## AutoPve
- Кнопка в MasterPanel: `BtnAutoPve`
- Включает BossEngine.Tick() в бою
- **Ограничение:** работает только с боссами из реестра. Без тактики = обычная ротация

## Как добавить нового босса
1. Создать `{Boss}Tactic.cs` реализуя `IBossTactic`
2. Зарегистрировать в `BossEngine._tacticFactory` с NPC ID
3. Добавить spell ID для CLEU listener

## Связи
- [[combat-system]] — BossEngine перехватывает ротацию когда тактика активна
- [[overview]] — AutoPve в планах на расширение
