---
title: Warlock
updated: 2025-04-15
tags: [class, warlock]
---

# Warlock

Файл ротации: `WowBot.Core/Game/Rotations/WarlockRotation.cs`
Fallback Lua: `AllRotations.cs` (строки ~1060-1100)

## Спеки

### Affliction
- Life Tap (1454) при mana<15%, Haunt (48181), UA (30108), Corruption (172)
- CoA (980, opt-in), CoE (1490, opt-in), Immolate (348), Death's Embrace (30283)
- LT Glyph proc (63321) → Life Tap
- Shadow Bolt (686) filler
- **AoE: нет**

### Demonology
- Survival: Death Coil (6789) при HP<35%, Healthstone при HP<60%, Shadow Ward (6229) при HP<70%
- Dark Pact (18220) при mana<20% и пет mana>300
- Soulshatter (29858) при threat>=3
- Metamorphosis (47241), Demonic Empowerment (47193), Immolation Aura (50589) в Meta
- **AoE: Seed of Corruption (27243) при `WB_NCET>=AEMIN`**
- Curses: CoA/CoD/CoE, Corruption (с защитой от двойного каста), Immolate
- Soul Fire proc (63167→6353), Incinerate proc (71165→29722)
- **Известный баг:** Seed не проверяет NR() и IR() — спамит каждый тик, игнорируя остальную ротацию

### Destruction
- Life Tap при mana<15%, Immolate (348), Chaos Bolt (50796), Conflagrate (17962)
- Corruption (172), CoD/CoE, Incinerate (29722) filler
- **AoE: нет**

## Связи
- [[aoe-system]] — Demo: Seed через WB_NCET
- [[combat-system]]
