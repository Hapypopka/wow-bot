---
title: InFrame Mode (Во фрейм)
updated: 2026-04-19
tags: [concept, positioning, hivemind]
---

# InFrame Mode — "Во фрейм"

## Идея

Кнопка **◎** на мастер-панели. При нажатии слейвы встают в фиксированную точку прямо сзади таргета мастера, на краю его хитбокса (8y от центра или `BR+4` для крупных боссов). Ротация продолжает работать — просто позиционирование к конкретной точке.

## Поведение

- **Trigger-кнопка** (не toggle): каждый клик = новая фиксация координат. UI визуально не залипает.
- Вычисление destination **один раз** в момент команды: `dest = target.Pos + ringRadius * (cos(Facing+π), sin(Facing+π))`
- Слейв сохраняет абсолютные координаты в `BotEngine.InFrameLockedPos`
- Босс двигается/поворачивается/сменился таргет → слейв стоит на зафиксированной точке
- **Авто-сброс:** при любой другой команде от мастера (Авто/Follow/Attack/Stop) — InFrame-lock очищается

## Технические детали

### Поток команды

1. Мастер: клик BtnInFrame → `OnCommand("inframe:trigger")`
2. MainWindow: `hive.CmdInFrame(true)` → SendAddonMessage через канал WBHIVE
3. Слейв в `Hivemind.ExecuteSlaveCommand`: вычисляет destination от **текущего** таргета, пишет в `_botEngine.InFrameLockedPos`
4. Approach в `ExecuteCombatTick` или хил path использует эти координаты через `CombatOptions.InFrameLockedPos`

### Где считается destination (НЕ в MakeCombatOptions!)

Важный урок: раньше `InFrameAngle` считался в `MakeCombatOptions`. Race при смене таргета: Ретпал таргетит Marrowgar → считается angle. Тикой позже AssistUnit переключил на Bone Spike → target=Spike, но InFrameAngle ещё Marrowgar'ский → `dest = Spike.X + 8*cos(Marrowgar.Facing)` → destination где-то далеко от Spike.

Фикс: считать в момент команды на слейве, абсолютные координаты в память. Dest от предыдущего target игнорируется — lock → стоит где залочено.

### Хил отдельным путём

Хил (BotEngine:1376) идёт своим маршрутом с собственным approach. Туда добавлена ветка `if (InFrameEnabled && InFrameLockedPos.HasValue)` — идёт в ту же точку что и ДПС через CombatExecutor.

### Особенности коммуникации

- InFrame НЕ требует ACK (в списке исключений, как SetBuff/Wipe/Interact)
- НЕ перезаписывает `SlaveInfo.ActiveCommand` в UI мастера — Авто/Атака-статусы слейвов остаются видимыми
- НЕ вызывает `SlaveCtrl.InitMasterGuid` (arg="on" ломал имя мастера)

## Связи

- [[hivemind]] — Command.InFrame, CmdInFrame, ExecuteSlaveCommand
- [[combat-system]] — CombatOptions.InFrameMode, InFrameLockedPos
- [[boss-engine]] — идеально для milee-клинча на боссах (Marrowgar)
