---
title: "План: Raycast LoS через AmeisenNav"
updated: 2025-04-18
tags: [future, plan, los, navigation]
---

# Raycast LoS — план на будущее

## Проблема
Сейчас у бота нет проверки Line of Sight перед кастом. Бывают случаи:
- Сова под целью, цель на возвышении — не может попасть заклинанием (WoW отказывает на сервере)
- Ranged DPS за препятствием — не видит цель, не может кастовать

## Когда делать
Не приоритет. Делать когда столкнёмся с частыми проблемами LoS в рейдах/инстах.

## Инфраструктура готова
- `WowBot.Core/Navigation/AmeisenNavClient.cs:161` — `CastRay(mapId, start, end, out hitPoint)` уже написан
- AmeisenNav TCP сервер на порту 47110 с mmaps из SPP
- Возвращает true если есть препятствие

## Как реализовать

В `CombatExecutor.ExecuteCombatTick()` перед ротацией для ranged DPS:
```csharp
if (!options.IsMeleeSpec && !options.IsHealer)
{
    bool blocked = _navEngine.CastRay(
        new Vector3(player.X, player.Y, player.Z + 2f), // +2m центр модели
        new Vector3(target.X, target.Y, target.Z + 1f)
    );
    if (blocked)
    {
        _ctm.MoveTo(target.X, target.Y, target.Z, 5.0f);
        return false;
    }
}
```

## Оптимизации
- Кешировать результат, пересчитывать раз в 1-2 сек (не каждый тик)
- Проверять только если dist > 10м
- Fallback на Z-diff check если AmeisenNav не отвечает

## Тонкости
- Высота +2m/+1m от ступней — в WoW LoS считается от центра модели
- На летающих юнитах (вертолёты, драконы) mmap может не знать про них — false negatives
- 1-5ms TCP round-trip на каждый raycast

## Связи
- [[navigation]] — AmeisenNav клиент
- [[combat-system]] — точка интеграции
