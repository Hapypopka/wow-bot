---
title: Paladin
updated: 2025-04-15
tags: [class, paladin]
---

# Paladin

Файлы: `RetPaladinRotation.cs` (C#, Ret), `PaladinRotation.cs` (Prot/Holy → AllRotations.cs)
Lua: `AllRotations.cs` (строки ~378-450 Prot, ~450-550 Holy)

## Спеки

### Retribution (C#)
- `RetPaladinRotation.cs` — полностью на C#
- Consecration кастуется если таргет стоит на месте (не AoE по кол-ву врагов)
- **AoE: нет** (Consecration не считает мобов)

### Protection (танк)
- Taunt: Hand of Reckoning (62124)
- DefCD: Divine Shield (642) → Lay on Hands (633) → Holy Shield (48951)
- Hand of Protection (1022) на умирающих союзников
- **AoE: Consecration (26573) при `WB_NCE>=2`** (танк)
- Divine Plea (54428), Holy Shield — поддерживать баффы
- Hammer of the Righteous (53595), Shield of Righteousness (61411)
- Judgement (20271), Holy Wrath (48817), Avenger's Shield (48827)

### Holy (хилер)
- ~80% реализован
- Beacon of Light на танка, Sacred Shield
- Holy Shock → Flash of Light → Holy Light
- **AoE: нет** (хилер)

## Связи
- [[aoe-system]] — Prot: Consecration через WB_NCE
- [[combat-system]]
