# Mark logs — пометка интервалов в логах

## Зачем
Бот может работать сутками — основной лог `wowbot_<Char>.log` содержит десятки тысяч строк. Когда юзер видит баг и хочет показать его Claude'у, `tail` бесполезен: с момента события могло пройти 5+ минут, контекст ушёл.

Mark logs позволяют выделить **только интересный отрезок**.

## Как пользоваться
1. Юзер видит проблему (или собирается её воспроизвести)
2. Нажимает **серый кружок** в оверлее (справа от заголовка "WowBot") или в мастер-панели (слева от `▬`/`⚙`) — кружок становится **красным**
3. Происходит проблема
4. Снова клик — кружок снова серый, mark остановлен
5. В `publish/` появляется `wowbot_<Char>_mark.log` — там только то что между Start и Stop

При следующем StartMark файл **перезаписывается**. История не накапливается.

## Master vs slave
Кнопка в **мастер-панели** автоматически рассылает команду всем слейвам через Hivemind (`Command.MarkStart` / `Command.MarkStop`). У каждого перса создаётся свой `wowbot_<Char>_mark.log` с одним и тем же временным окном.

Кнопка в обычном **оверлее** — только локально для этого перса.

## Реализация
- [Logger.cs](../../WowBot.Core/Logger.cs) `StartMark()` / `StopMark()` — открывают/закрывают `_markWriter`. Метод `Log()` дублирует каждую строку в mark-файл если активен.
- [Hivemind.cs](../../WowBot.Core/Game/Hivemind.cs) — `Command.MarkStart` / `Command.MarkStop` (service-команды без ACK), методы `CmdMarkStart()` / `CmdMarkStop()` для master broadcast. Слейв обрабатывает в switch case → вызывает `Logger.StartMark/StopMark`. **Важно:** строковое имя команды должно быть в `ParseSlaveResponse` — иначе слейв не распарсит.
- UI: компактный круглый Border 12x12 в шапке. Серый `#3a3d45` неактивно, красный `#c43a3a` активно. Toggle при клике.
  - [OverlayWindow.xaml](../../WowBot.Injector/OverlayWindow.xaml) + `BtnMark_Click` в `.xaml.cs` → событие `OnMarkToggle(bool)`
  - [MasterPanel.xaml](../../WowBot.Injector/MasterPanel.xaml) + `BtnMark_Click` в `.xaml.cs` → событие `OnMarkToggle(bool)`
- [MainWindow.xaml.cs](../../WowBot.Injector/MainWindow.xaml.cs) подписан на оба события: локально `Logger.StartMark/StopMark` всегда, broadcast через Hivemind если роль = `Master`.
