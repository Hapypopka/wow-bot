---
title: Death Knight
updated: 2025-04-15
tags: [class, death-knight]
---

# Death Knight

Файл ротации: `WowBot.Core/Game/Rotations/DeathKnightRotation.cs`
Fallback Lua: `AllRotations.cs` (строки ~800-870)

## Общее
- Управление гулем: PetDefensive в бою, PetPassive вне боя, PetAttack на таргет
- Автокаст 4й способности пета

## Спеки

### Blood (танк)
- Taunt (56222), DefCD: IBF (48792) → Vampiric Blood (55233) → Bone Shield (49222)
- **AoE: Death and Decay (43265)** при `WB_NCE>=2` (танк, считает от себя)
- IT (45477) → PS (45462) → Pestilence (50842) → DS (49998) → HS (55050) → BS (45902) → RS (56815)

### Frost
- IT → PS → Pestilence (при дотах < 3s) → Unbreakable Armor (51271)
- HB proc (59052→49184) → Obliterate (49020) → BS → Frost Strike (49143) → Blood Tap (45529)
- ERW (47568), HoW (57330)
- **AoE: нет**

### Unholy
- IT → PS → **Pestilence (50842) при `WB_NCET>=AEMIN`** (DPS, считает от таргета)
- Death Coil (47541) при RP>80, Gargoyle (49206), Unholy Blight (49194)
- SS (55090) → BT (45529) → BS (45902) → **DnD (43265) при `WB_NCET>=AEMIN`**
- ERW (47568)

## Связи
- [[aoe-system]] — Blood=WB_NCE, Unholy=WB_NCET
- [[combat-system]]
