---
title: Druid
updated: 2025-04-15
tags: [class, druid]
---

# Druid

Файл ротации: `WowBot.Core/Game/Rotations/DruidRotation.cs` (делегирует в AllRotations.cs)
Lua: `AllRotations.cs` (строки ~1124-1325)

## Спеки

### Balance (Moonkin)
- Faerie Fire (FF), Insect Swarm (IS), Moonfire (MF)
- **AoE: нет** в ротации. **Ground AoE: Hurricane (Гроза)** через `TryGroundAoE` (CastTerrainClick)
- Hurricane кастуется по русскому имени "Гроза" (не spell ID)

### Feral (Cat)
- Mangle → Savage Roar → Rip → Ferocious Bite
- **AoE: Swipe (62078) при `WB_NCET>=AEMIN`**
- Faerie Fire (Feral) бесплатно

### Feral (Bear, танк)
- DefCD: Survival Instincts (61336) → Frenzied Regen (22842) → Barkskin (22812)
- **AoE: Swipe (779) при `WB_NCE>=2`** (танк, считает от себя)
- Maul (6807), Faerie Fire bear (16857), Lacerate (33745), Mangle bear (33878)

### Resto (хилер)
- Wild Growth, Rejuvenation, Nourish, Healing Touch
- **AoE: нет** (хилер)

## Связи
- [[aoe-system]] — Cat=WB_NCET, Bear=WB_NCE, Balance=Ground AoE
- [[combat-system]]
