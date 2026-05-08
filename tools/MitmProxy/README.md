# MitmProxy — WoW 3.3.5a active double-MITM

Перехватываем трафик между реальным WoW клиентом и реальным сервером WoWCircle.

## Зачем

В headless POC мы попытались сделать клиент с нуля, но упёрлись в Warden anti-cheat — нужен реверс зашифрованного x86 модуля. Через MITM проблема обходится: реальный клиент честно гоняет Warden, а мы получаем read+inject для CMSG/SMSG потока.

## Архитектура

```
┌──────────┐   logon (3724)        ┌──────────┐   logon (3724)        ┌──────────────┐
│ Wow.exe  │ ────SRP6 (с нами)──── │ MitmProxy│ ────SRP6 (с ним)───── │ wowcircle.com│
│          │                       │          │                       │              │
│          │   world (8085)        │ K_client │   world (real port)   │              │
│          │ ────RC4(K_client)──── │ K_server │ ────RC4(K_server)──── │              │
└──────────┘                       └──────────┘                       └──────────────┘
```

Ключи K_client и K_server **разные** (приватные экспоненты a/b случайные у каждой стороны). Прокси держит две пары `WowCrypt` и переводит пакеты между шифрами.

## Этап 1 (текущий) — fake logon

Только logon-leg. Клиент проходит SRP6 с нами, видит наш фейк-realm, тыкает в него — попадает на наш ещё не реализованный world-прокси. Этого этапа достаточно чтобы проверить что **наша SRP6 server-side корректная**.

## Запуск (этап 1)

1. Поменять realmlist клиента на 127.0.0.1:
   ```cmd
   D:\Games\Вов\switch_to_local.bat
   ```
   (или вручную вписать в `Data\ruRU\realmlist.wtf`: `set realmlist 127.0.0.1`)

2. Запустить прокси:
   ```cmd
   cd c:\Проекты\wow-bot\tools\MitmProxy
   dotnet run -- <ACCOUNT> <PASSWORD>
   ```
   Пример: `dotnet run -- FAIZCASSA1313 ,FLVTYNJY3131`

3. Запустить `D:\Games\Вов\Wow.exe` обычным способом.

4. В логине прокси видим:
   ```
   [logon] client connected: 127.0.0.1:NNNN
   [logon] client wants account 'FAIZCASSA1313'
   [logon] -> challenge sent
   [logon] <- proof received
   [logon] ++ SRP6 OK. K_client=...
   [logon] -> realm list sent
   ```

5. В клиенте: после логина появится realm "MITM-Test". Тыкаем на него → клиент попытается законнектиться к world-серверу (которого пока нет) → таймаут. Это нормально для этапа 1.

## Что дальше

- **Этап 2:** добавить вторую SRP6-легу (как клиент к wowcircle) → получить K_server + реальный realm address
- **Этап 3:** world-прокси на 8085 — bridge encrypted потоков с re-digest CMSG_AUTH_SESSION
- **Этап 4:** API инъекций — отправлять CMSG (chat, movement, casts) в живую сессию

## Откат

```cmd
D:\Games\Вов\switch_to_wowcircle.bat
```
вернёт realmlist на оригинальный wowcircle.
