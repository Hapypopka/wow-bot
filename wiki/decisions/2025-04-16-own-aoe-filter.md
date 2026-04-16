---
title: "Решение: фильтр своих AoE в Avoidance"
updated: 2025-04-16
tags: [decision, aoe, avoidance, bug]
---

# Фильтр своих AoE в AoE Avoidance

## Проблема
Сова (Balance Druid) с включённым AoE Avoidance кастила Hurricane и тут же убегала из неё — канал прерывался, урон терялся.

## Причина
Hurricane = channel spell. Пока канал активен:
- На земле стоит DynObject (AoE зона Грозы) с SpellId=16914
- На кастере висит aura channel Hurricane с тем же SpellId

Код проверял "есть ли DynObject чей SpellId совпадает с моей аурой" → совпадало всегда с собственной Грозой → убегали.

## Решение
В `CombatHelper.TryAoEAvoidance()` добавлена проверка перед основным фильтром:
```csharp
if (dyn.Caster == player.Guid) continue;
```

**Подводный камень (WoWCircle):** оказалось что `WowDynObject.Caster` читался с неправильного оффсета. По AmeisenBotX был +0x00, но на WoWCircle там лежит GUID самого DynObject (0xF1...), а настоящий Caster — по **+0x18**. Диагностический лог с dump дескриптора показал правильный оффсет.

Фикс дескриптора: `WowBot.Core/Game/Entities/WowDynObject.cs:23`.

## Эффект
Одна строчка чинит для всех кто юзает собственные AoE на земле:
- Druid Balance — Hurricane
- Hunter — Volley
- Paladin Prot — Consecration
- DK — Death and Decay
- Warlock — Rain of Fire
- Shaman — Magma Totem / Fire Nova

## Файлы
- `WowBot.Core/Game/CombatHelper.cs:195` — одна строчка проверки

## Связи
- [[aoe-system]]
