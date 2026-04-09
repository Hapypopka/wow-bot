---
paths:
  - "**/Rotations/**"
  - "**/CombatExecutor.cs"
  - "**/CombatHelper.cs"
  - "**/BuffManager.cs"
---

# Правила ротаций и боевой логики

## Spell ID — ВСЕГДА
- Все ротации на spell ID через `Cast(id)`, `IR(id)`, `HB(id)`, `HD(u,id)`
- НЕ использовать русские названия в новом коде
- Если нужно имя — `GetSpellInfo(spellId)` через хелпер `SN(id)`

## Структура ротаций
- Каждый класс — отдельный файл {Class}Rotation.cs, реализует ICombatRotation
- RotationRegistry — поиск по классу/спеку, C# приоритет → Lua fallback
- LuaHelpers.cs — единый набор Lua хелперов (IR, HB, HD, Cast, SN, TryTaunt...)
- AllRotations.cs — хранилище Lua-скриптов для хилеров/сложных спеков

## Куда добавлять код
- Боевые фичи (dodge, interrupt, CD) → CombatExecutor.ExecuteCombatTick()
- Баффы → BuffManager
- Подсчёт врагов, AoE детекция → CombatHelper
- Новый спек → отдельный файл, зарегистрировать в RotationRegistry

## ПЕРЕД изменением — убедиться что код ВЫПОЛНЯЕТСЯ
- Проверить что RotationRegistry загружает нужную ротацию (не AllRotations fallback)
- Проверить логи: "Rotation: Xxx (C#)" или "Rotation: Xxx (Lua fallback)"
