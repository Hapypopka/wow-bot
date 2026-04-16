---
title: Overlay UI
updated: 2025-04-15
tags: [architecture, ui, settings]
---

# Overlay UI (OverlayWindow)

Файл: `WowBot.Injector/OverlayWindow.xaml.cs` (2244 строк)
WPF оверлей поверх окна WoW.

## Настройки
- Путь: `settings.json` (глобальный) или `settings_{charName}.json` (per-character)
- Формат: JSON dictionary
- Автосохранение при каждом изменении

## Основные контролы

| Тип | Что | Хранение |
|-----|-----|----------|
| AoE toggle | Вкл/выкл AoE | `BtnAoe.IsChecked` |
| Buff toggle | Вкл/выкл баффов | `BtnBuffs.IsChecked` |
| AoE Min slider | Порог врагов для AoE (default 3) | `slider_aoeMin` |
| Follow Dist slider | Дистанция follow (default 8) | `slider_dist` |
| Max Range slider | Макс рейндж таргета (default 30) | `slider_maxRange` |
| AutoFace checkbox | Авто-поворот к таргету | `chk_autoFace` |
| AutoTarget checkbox | Авто-таргет | `chk_autoTarget` |
| MoveBehind checkbox | Заходить за спину | `chk_moveBehind` |
| AoE Avoid checkbox | Убегать из луж | `chk_aoeAvoid` |

## Класс-специфичные контролы

| Класс | Контролы |
|-------|----------|
| **Paladin** | Seal (SoV/SoC/SoW/SoL), Blessing (BoM/BoK/BoW/BoS), Aura (7 вариантов), Judgement |
| **Warrior** | Stance (Battle/Def/Berserker), Shout (Battle/Commanding) |
| **DK** | Presence (Blood/Frost/Unholy) |
| **Druid** | Feral Form (Cat/Bear) |
| **Shaman** | Totems 4x (Earth/Fire/Water/Air), Weapons MH/OH (FT/EL/WF) |
| **Warlock** | Pet (5 вариантов), Curse (CoA/CoD/CoE) |
| **Hunter** | Standard/Trapper mode |
| **Priest** | MultiDot checkbox, MindSear checkbox, Dispersion/SF mana thresholds |

## Spell Toggles
- Каждый спелл ротации можно вкл/выкл отдельно
- Хранится как `spell_{key}=true/false`
- Передаётся в Lua как `WB_S.Key=true/false`
- `~=false` = включён по умолчанию, `==true` = выключен по умолчанию

## Связи
- [[combat-system]] — WB_S.* spell flags
- [[buff-system]] — seal/blessing/aura/stance/presence настройки
- [[aoe-system]] — AoE toggle + min slider
