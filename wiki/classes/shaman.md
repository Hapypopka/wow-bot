---
title: Shaman
updated: 2025-04-15
tags: [class, shaman]
---

# Shaman

Файл ротации: `WowBot.Core/Game/Rotations/ShamanRotation.cs` (делегирует в AllRotations.cs)
Lua: `AllRotations.cs` (строки ~870-1010)

## Спеки

### Elemental
- Сингл-таргет ротация
- **AoE: нет**

### Enhancement
- Lightning Shield (324) поддерживать
- **Maelstrom 5 стаков (53817): Chain Lightning (421) при `WB_NCET>=AEMIN`**, иначе Lightning Bolt (403)
- Stormstrike (17364), Lava Lash (60103), Earth Shock (8042), Magma Totem (8190)
- Fire Nova (1535) при Magma Totem active
- **AoE: Chain Lightning** через WB_NCET (только при 5 стаках Maelstrom)

### Resto (хилер)
- Riptide → Chain Heal → Lesser Healing Wave → Healing Wave
- Earth Shield на танка
- **AoE: нет** (хилер)

## Тотемы
- SetMultiCastSpell для 3 наборов (огонь/земля/вода/воздух)
- Баффы (Strength of Earth, Windfury, etc.) через BuffManager

## Связи
- [[aoe-system]] — Enh: Chain Lightning через WB_NCET
- [[buff-system]] — тотемы
- [[combat-system]]
