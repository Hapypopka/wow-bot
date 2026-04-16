---
title: Hunter
updated: 2025-04-15
tags: [class, hunter]
---

# Hunter

Файл ротации: `WowBot.Core/Game/Rotations/HunterRotation.cs`
Fallback Lua: `AllRotations.cs` (строки ~600-660)

## Общее
- Pet management: Defensive в бою, Passive+Follow вне боя и при Feign Death
- Tracking по типу существа (автоматически)
- Mend Pet (136) при HP пета < 75%
- Aspect switching: Viper (34074) при mana<20%, Dragonhawk (61847) при mana>50%
- Feign Death (5384) при threat>=3
- Misdirection (34477) на танка (находит по баффу стойки)

## Спеки

### BM
- Bestial Wrath (19574), Kill Command (34471), Serpent Sting (1978), Aimed (19434), Arcane (3044), Steady (56641)
- **AoE: нет**

### MM
- Dragonhawk (61847), **Multi-Shot (2643) при `WB_NCET>=AEMIN` + стоим + не кастим**
- Rapid Fire (3045), Kill Command (34026), pet abilities (Раж, Неистовый вой, Зов дикой природы)
- Tranquilizing Shot (19801) на Enrage баффы
- Serpent → Chimera (53209) → Silencing Shot (34490) → Aimed → Trap (13813) → Readiness (23989) → Steady
- **Ground AoE: Volley (1510)** через `TryGroundAoE` (CastTerrainClick)

### Survival
- Explosive Shot (53301), Black Arrow (3674), Serpent Sting, Aimed, Arcane, Steady
- **AoE: нет**

## Связи
- [[aoe-system]] — MM: Multi-Shot через WB_NCET, Volley через Ground AoE
- [[combat-system]]
