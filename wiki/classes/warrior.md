---
title: Warrior
updated: 2025-04-15
tags: [class, warrior]
---

# Warrior

Файл ротации: `WowBot.Core/Game/Rotations/WarriorRotation.cs`
Fallback Lua: `AllRotations.cs` (строки ~360-370)

## Спеки

### Arms
- Recklessness (1719), Execute (5308), Rend (772), Mortal Strike (12294), Overpower (7384), Slam (1464), Cleave (845)
- **AoE: нет** — нет Whirlwind, Bladestorm, Cleave по условию

### Fury
- Recklessness (1719), Execute (5308), Bloodthirst (23881), Whirlwind (1680), Slam proc (46916→1464)
- **AoE: нет** — Whirlwind кастится как часть сингл-ротации, не по кол-ву врагов

### Prot (танк)
- Taunt (355), DefCD: Shield Wall (871) → Last Stand (12975) → Shield Block (2565)
- **AoE: Thunder Clap (6343)** при `WB_NCE>=2` (танк, считает от себя)
- Heroic Strike (47449) при rage>50, Berserker Rage (2687) при rage<30
- Shield Slam (23922), Revenge (6572), Shockwave (46968), Devastate (20243)

## Связи
- [[aoe-system]] — Prot AoE через WB_NCE
- [[combat-system]] — единый путь solo/slave
