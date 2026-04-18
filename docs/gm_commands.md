# SPP WotLK — все GM команды (342)

**Источник:**  (локальная MySQL)

## Уровни доступа
- **0 Игрок** — доступно всем
- **1 Модер** — модератор
- **2 GM** — гейммастер
- **3 Админ** — админ (твой TEST)
- **4 Root** — высший (только консоль)

## Как использовать

В игре: напиши команду с  в чат, например , .
В консоли сервера: без .

---

## .account (10 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.account` | 0 Игрок | Syntax: .account\n\nDisplay the access level of your account. |
| `.account characters` | 3 Админ | Syntax: .account characters [#accountId\|$accountName]\n\nShow list all characters for account selected by provided #accountId or $accountName, or for selected player in game. |
| `.account create` | 4 Root | Syntax: .account create $account $password [$expansion]\n\nCreate account and set password to it. Optionally, you may also set another expansion for this account than the defined default value. |
| `.account delete` | 4 Root | Syntax: .account delete $account\n\nDelete account with all characters. |
| `.account lock` | 0 Игрок | Syntax: .account lock [on\|off]\n\nAllow login from account only from current used IP or remove this requirement. |
| `.account onlinelist` | 4 Root | Syntax: .account onlinelist\n\nShow list of online accounts. |
| `.account password` | 0 Игрок | Syntax: .account password $old_password $new_password $new_password\n\nChange your account password. |
| `.account set addon` | 3 Админ | Syntax: .account set addon [#accountId\|$accountName] #addon\n\nSet user (possible targeted) expansion addon level allowed. Addon values: 0 - normal, 1 - tbc, 2 - wotlk. |
| `.account set gmlevel` | 4 Root | Syntax: .account set gmlevel [#accountId\|$accountName] #level\n\nSet the security level for targeted player (can't be used at self) or for #accountId or $accountName to a level of #level.\n\n#level m... |
| `.account set password` | 4 Root | Syntax: .account set password (#accountId\|$accountName) $password $password\n\nSet password for account. |

## .achievement (5 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.achievement` | 3 Админ | Syntax: .achievement $playername #achivementid\n\nShow state achievment #achivmentid (can be shift link) and list of achievement criteria with progress data for selected player in game or by player na... |
| `.achievement add` | 3 Админ | Syntax: .achievement add $playername #achivementid\n\nComplete achievement and all it's criteria for selected player in game or by player name. Command can't be used for counter achievements. |
| `.achievement criteria add` | 3 Админ | Syntax: .achievement criteria add $playername #criteriaid #change\n\nIncrease progress for non-completed criteria at #change for selected player in game or by player name. If #chnage not provided then... |
| `.achievement criteria remove` | 3 Админ | Syntax: .achievement criteria remove $playername #criteriaid #change\n\necrease progress for criteria at #change for selected player in game or by player name. If #chnage not provided then criteria pr... |
| `.achievement remove` | 3 Админ | Syntax: .achievement remove $playername #achivementid\n\nRemove complete state for achievement #achivmentid and reset all achievement's criteria for selected player in game or by player name. Also com... |

## .additem (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.additem` | 3 Админ | Syntax: .additem #itemid/[#itemname]/#shift-click-item-link #itemcount\n\nAdds the specified number of items of id #itemid (or exact (!) name $itemname in brackets, or link created by shift-click at i... |

## .additemset (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.additemset` | 3 Админ | Syntax: .additemset #itemsetid\n\nAdd items from itemset of id #itemsetid to your or selected character inventory. Will add by one example each item from itemset. |

## .ahbot (4 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.ahbot item` | 3 Админ | Syntax: .ahbot item #itemid [$itemvalue [$addchance [$minamount [$maxamount]]]] [reset]\n\nShow/modify AHBot item. Setting $itemvalue to 0 bans item. Setting $addchance greater than 0 (0-100, default ... |
| `.ahbot rebuild` | 3 Админ | Syntax: .ahbot rebuild [all]\n\nExpire all auctions by ahbot except those bidded on by a player. Bidded auctions can be forced expired by using the "all" option. AHBot will re-fill auctions using curr... |
| `.ahbot reload` | 3 Админ | Syntax: .ahbot reload\n\nReload AHBot settings from configuration file. |
| `.ahbot status` | 3 Админ | Syntax: .ahbot status\n\nShow current amount of items added to the auction house by AHBot. |

## .announce (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.announce` | 1 Модер | Syntax: .announce $MessageToBroadcast\n\nSend a global message to all players online in chat log. |

## .auction (5 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.auction` | 3 Админ | Syntax: .auction\n\nShow your team auction store. |
| `.auction alliance` | 3 Админ | Syntax: .auction alliance\n\nShow alliance auction store independent from your team. |
| `.auction goblin` | 3 Админ | Syntax: .auction goblin\n\nShow goblin auction store common for all teams. |
| `.auction horde` | 3 Админ | Syntax: .auction horde\n\nShow horde auction store independent from your team. |
| `.auction item` | 3 Админ | Syntax: .auction item (alliance\|horde\|goblin) #itemid[:#itemcount] [[[#minbid] #buyout] [short\|long\|verylong]\n\nAdd new item (in many stackes if amount grater stack size) to specific auction hous... |

## .aura (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.aura` | 3 Админ | Syntax: .aura #spellid\n\nAdd the aura from spell #spellid to the selected Unit. |

## .ban (3 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.ban account` | 3 Админ | Syntax: .ban account $Name $bantime $reason\nBan account kick player.\n$bantime: negative value leads to permban, otherwise use a timestring like "4d20h3s". |
| `.ban character` | 3 Админ | Syntax: .ban character $Name $bantime $reason\nBan account and kick player.\n$bantime: negative value leads to permban, otherwise use a timestring like "4d20h3s". |
| `.ban ip` | 3 Админ | Syntax: .ban ip $Ip $bantime $reason\nBan IP.\n$bantime: negative value leads to permban, otherwise use a timestring like "4d20h3s". |

## .baninfo (3 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.baninfo account` | 3 Админ | Syntax: .baninfo account $accountid\nWatch full information about a specific ban. |
| `.baninfo character` | 3 Админ | Syntax: .baninfo character $charactername \nWatch full information about a specific ban. |
| `.baninfo ip` | 3 Админ | Syntax: .baninfo ip $ip\nWatch full information about a specific ban. |

## .bank (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.bank` | 3 Админ | Syntax: .bank\n\nShow your bank inventory. |

## .banlist (3 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.banlist account` | 3 Админ | Syntax: .banlist account [$Name]\nSearches the banlist for a account name pattern or show full list account bans. |
| `.banlist character` | 3 Админ | Syntax: .banlist character $Name\nSearches the banlist for a character name pattern. Pattern required. |
| `.banlist ip` | 3 Админ | Syntax: .banlist ip [$Ip]\nSearches the banlist for a IP pattern or show full list of IP bans. |

## .cast (5 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.cast` | 3 Админ | Syntax: .cast #spellid [triggered]\n  Cast #spellid to selected target. If no target selected cast to self. If 'trigered' or part provided then spell casted with triggered flag. |
| `.cast back` | 3 Админ | Syntax: .cast back #spellid [triggered]\n  Selected target will cast #spellid to your character. If 'trigered' or part provided then spell casted with triggered flag. |
| `.cast dist` | 3 Админ | Syntax: .cast dist #spellid [#dist [triggered]]\n  You will cast spell to pint at distance #dist. If 'trigered' or part provided then spell casted with triggered flag. Not all spells can be casted as ... |
| `.cast self` | 3 Админ | Syntax: .cast self #spellid [triggered]\nCast #spellid by target at target itself. If 'trigered' or part provided then spell casted with triggered flag. |
| `.cast target` | 3 Админ | Syntax: .cast target #spellid [triggered]\n  Selected target will cast #spellid to his victim. If 'trigered' or part provided then spell casted with triggered flag. |

## .channel (2 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.channel list` | 1 Модер | Syntax: .channel list [#max] [static]\n\nShow list of custom channels with amounts of players joined. |
| `.channel static` | 1 Модер | Syntax: .channel static $channelname on\|off\n\nEnable or disable static mode for a custom channel with name $channelname. Static custom channel upon conversion acquires a set of properties identical ... |

## .character (11 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.character achievements` | 2 GM | Syntax: .character achievements [$player_name]\n\nShow completed achievments for selected player or player find by $player_name. |
| `.character customize` | 2 GM | Syntax: .character customize [$name]\n\nMark selected in game or by $name in command character for customize at next login. |
| `.character deleted delete` | 4 Root | Syntax: .character deleted delete #guid\|$name\n\nCompletely deletes the selected characters.\nIf $name is supplied, only characters with that string in their name will be deleted, if #guid is supplie... |
| `.character deleted list` | 4 Root | Syntax: .character deleted list [#guid\|$name]\n\nShows a list with all deleted characters.\nIf $name is supplied, only characters with that string in their name will be selected, if #guid is supplied... |
| `.character deleted old` | 4 Root | Syntax: .character deleted old [#keepDays]\n\nCompletely deletes all characters with deleted time longer #keepDays. If #keepDays not provided the  used value from mangosd.conf option 'CharDelete.KeepD... |
| `.character deleted restore` | 3 Админ | Syntax: .character deleted restore #guid\|$name [$newname] [#new account]\n\nRestores deleted characters.\nIf $name is supplied, only characters with that string in their name will be restored, if $gu... |
| `.character erase` | 4 Root | Syntax: .character erase $name\n\nDelete character $name. Character finally deleted in case any deleting options. |
| `.character level` | 3 Админ | Syntax: .character level [$playername] [#level]\n\nSet the level of character with $playername (or the selected if not name provided) by #numberoflevels Or +1 if no #numberoflevels provided). If #numb... |
| `.character rename` | 2 GM | Syntax: .character rename [$name]\n\nMark selected in game or by $name in command character for rename at next login. |
| `.character reputation` | 2 GM | Syntax: .character reputation [$player_name]\n\nShow reputation information for selected player or player find by $player_name. |
| `.character titles` | 2 GM | Syntax: .character titles [$player_name]\n\nShow known titles list for selected player or player find by $player_name. |

## .combat (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.combat stop` | 2 GM | Syntax: .combatstop [$playername]\nStop combat for selected character. If selected non-player then command applied to self. If $playername provided then attempt applied to online player $playername. |

## .commands (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.commands` | 0 Игрок | Syntax: .commands\n\nDisplay a list of available commands for your account level. |

## .cooldown (3 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.cooldown clear` | 3 Админ | Syntax: .cooldown clear [spell id] Remove cooldown from selected unit. |
| `.cooldown clearclientside` | 3 Админ | Syntax: .cooldown clearclientside  Clear all cooldown client side only. |
| `.cooldown list` | 3 Админ | Syntax: .cooldown list  Active cooldown from selected unit. |

## .damage (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.damage` | 3 Админ | Syntax: .damage $damage_amount [$school [$spellid]]\n\nApply $damage to target. If not $school and $spellid provided then this flat clean melee damage without any modifiers. If $school provided then d... |

## .debug (22 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.debug anim` | 2 GM | Syntax: .debug anim #emoteid\n\nPlay emote #emoteid for your character. |
| `.debug areatriggers` | 1 Модер | Syntax: .debug areatriggers\n\nToggle debug mode for areatriggers. In debug mode GM will be notified if reaching an areatrigger. |
| `.debug arena` | 3 Админ | Syntax: .debug arena\n\nToggle debug mode for arenas. In debug mode GM can start arena with single player. |
| `.debug bg` | 3 Админ | Syntax: .debug bg\n\nToggle debug mode for battlegrounds. In debug mode GM can start battleground with single player. |
| `.debug dbscript` | 3 Админ | .debug dbscript\n\nStarts dbscript type param0 id param1 from player(source) to selected(target) |
| `.debug dbscriptguided` | 3 Админ | .debug dbscript\n\nStarts dbscript type param0 id param1 from param2 dbguid(source creature) to param3 dbguid(target creature) |
| `.debug dbscriptsourced` | 3 Админ | .debug dbscript\n\nStarts dbscript type param0 id param1 from param2 dbguid(source creature) to selected(target) |
| `.debug dbscripttargeted` | 3 Админ | .debug dbscript\n\nStarts dbscript type param0 id param1 from selected(source) to param2 dbguid(target creature) |
| `.debug getitemvalue` | 3 Админ | Syntax: .debug getitemvalue #itemguid #field [int\|hex\|bit\|float]\n\nGet the field #field of the item #itemguid in your inventroy.\n\nUse type arg for set output format: int (decimal number), hex (h... |
| `.debug getvaluebyindex` | 3 Админ | Syntax: .debug getvaluebyindex #field [int\|hex\|bit\|float]\n\nGet the field index #field (integer) of the selected target. If no target is selected, get the content of your field.\n\nUse type arg fo... |
| `.debug getvaluebyname` | 3 Админ | Syntax: .debug getvaluebyname #fieldname\n\nGet the field name #fieldname (string) of the selected target. If no target is selected, get the content of your field. |
| `.debug moditemvalue` | 3 Админ | Syntax: .debug moditemvalue #guid #field [int\|float\| &= \| \|= \| &=~ ] #value\n\nModify the field #field of the item #itemguid in your inventroy by value #value. \n\nUse type arg for set mode of mo... |
| `.debug modvalue` | 3 Админ | Syntax: .debug modvalue #field [int\|float\| &= \| \|= \| &=~ ] #value\n\nModify the field #field of the selected target by value #value. If no target is selected, set the content of your field.\n\nUs... |
| `.debug play cinematic` | 1 Модер | Syntax: .debug play cinematic #cinematicid\n\nPlay cinematic #cinematicid for you. You stay at place while your mind fly.\n |
| `.debug play movie` | 1 Модер | Syntax: .debug play movie #movieid\n\nPlay movie #movieid for you. |
| `.debug play sound` | 1 Модер | Syntax: .debug play sound #soundid\n\nPlay sound with #soundid.\nSound will be play only for you. Other players do not hear this.\nWarning: client may have more 5000 sounds... |
| `.debug setitemvalue` | 3 Админ | Syntax: .debug setitemvalue #guid #field [int\|hex\|bit\|float] #value\n\nSet the field #field of the item #itemguid in your inventroy to value #value.\n\nUse type arg for set input format: int (decim... |
| `.debug setvaluebyindex` | 3 Админ | Syntax: .debug setvaluebyindex #field [int\|hex\|bit\|float] #value\n\nSet the field index #field (integer) of the selected target to value #value. If no target is selected, set the content of your fi... |
| `.debug setvaluebyname` | 3 Админ | Syntax: .debug setvaluebyname #fieldname #values\n\nSet the field name #fieldname (string) of the selected target to value #values. If no target is selected, set the content of your field. |
| `.debug spellcoefs` | 3 Админ | Syntax: .debug spellcoefs #spellid\n\nShow default calculated and DB stored coefficients for direct/dot heal/damage. |
| `.debug spellmods` | 3 Админ | Syntax: .debug spellmods (flat\|pct) #spellMaskBitIndex #spellModOp #value\n\nSet at client side spellmod affect for spell that have bit set with index #spellMaskBitIndex in spell family mask for valu... |
| `.debug taxi` | 3 Админ | Syntax: .debug taxi\n\nToggle debug mode for taxi flights. In debug mode GM receive additional on-screen information during taxi flights. |

## .demorph (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.demorph` | 2 GM | Syntax: .demorph\n\nDemorph the selected player. |

## .die (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.die` | 3 Админ | Syntax: .die\n\nKill the selected player. If no player is selected, it will kill you. |

## .dismount (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.dismount` | 0 Игрок | Syntax: .dismount\n\nDismount you, if you are mounted. |

## .distance (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.distance` | 3 Админ | Syntax: .distance [$name/$link]\n\nDisplay the distance from your character to the selected creature/player, or player with name $name, or player/creature/gameobject pointed to shift-link with guid. |

## .event (4 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.event` | 2 GM | Syntax: .event #event_id\nShow details about event with #event_id. |
| `.event list` | 2 GM | Syntax: .event list\nShow list of currently active events.\nShow list of all events |
| `.event start` | 2 GM | Syntax: .event start #event_id\nStart event #event_id. Set start time for event to current moment (change not saved in DB). |
| `.event stop` | 2 GM | Syntax: .event stop #event_id\nStop event #event_id. Set start time for event to time in past that make current moment is event stop time (change not saved in DB). |

## .explorecheat (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.explorecheat` | 3 Админ | Syntax: .explorecheat #flag\n\nReveal  or hide all maps for the selected player. If no player is selected, hide or reveal maps to you.\n\nUse a #flag of value 1 to reveal, use a #flag value of 0 to hi... |

## .flusharenapoints (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.flusharenapoints` | 3 Админ | Syntax: .flusharenapoints\n\nUse it to distribute arena points based on arena team ratings, and start a new week. |

## .gearscore (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.gearscore` | 3 Админ | Syntax: .gearscore [#withBags] [#withBank]\n\nShow selected player's gear score. Check items in bags if #withBags != 0 and check items in Bank if #withBank != 0. Default: 1 for bags and 0 for bank |

## .gm (8 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.gm` | 3 Админ | Syntax: .gm [on/off]\n\nEnable or Disable in game GM MODE or show current state of on/off not provided. |
| `.gm chat` | 3 Админ | Syntax: .gm chat [on/off]\n\nEnable or disable chat GM MODE (show gm badge in messages) or show current state of on/off not provided. |
| `.gm fly` | 3 Админ | Syntax: .gm fly [on/off]\nEnable/disable gm fly mode. |
| `.gm ingame` | 3 Админ | Syntax: .gm ingame\n\nDisplay a list of available in game Game Masters. |
| `.gm list` | 3 Админ | Syntax: .gm list\n\nDisplay a list of all Game Masters accounts and security levels. |
| `.gm mountup` | 3 Админ | Syntax: .gm mountup [fast\|slow\|#displayid\|target]\n\nIf #displayid is provided, visually mounts your character on a provided creature likeness. If your target is a creature and corresponding arg is... |
| `.gm setview` | 3 Админ | Syntax: .gm setview\n\nSet farsight view on selected unit. Select yourself to set view back. |
| `.gm visible` | 3 Админ | Syntax: .gm visible on/off\n\nOutput current visibility state or make GM visible(on) and invisible(off) for other players. |

## .go (11 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.go` | 1 Модер | Syntax: .go  [$playername\|pointlink\|#x #y #z [#mapid]]\nTeleport your character to point with coordinates of player $playername, or coordinates of one from shift-link types: player, tele, taxinode, ... |
| `.go creature` | 1 Модер | Syntax: .go creature (#creature_guid\|$creature_name\|id #creature_id)\nTeleport your character to creature with guid #creature_guid, or teleport your character to creature with name including as part... |
| `.go graveyard` | 1 Модер | Syntax: .go graveyard #graveyardId\n Teleport to graveyard with the graveyardId specified. |
| `.go grid` | 1 Модер | Syntax: .go grid #gridX #gridY [#mapId]\n\nTeleport the gm to center of grid with provided indexes at map #mapId (or current map if it not provided). |
| `.go object` | 1 Модер | Syntax: .go object (#gameobject_guid\|$gameobject_name\|id #gameobject_id)\nTeleport your character to gameobject with guid #gameobject_guid, or teleport your character to gameobject with name includi... |
| `.go taxinode` | 1 Модер | Syntax: .go taxinode #taxinode\n\nTeleport player to taxinode coordinates. You can look up zone using .lookup taxinode $namepart |
| `.go trigger` | 1 Модер | Syntax: .go trigger (#trigger_id\|$trigger_shift-link\|$trigger_target_shift-link) [target]\n\nTeleport your character to areatrigger with id #trigger_id or trigger id associated with shift-link. If a... |
| `.go warp` | 1 Модер | Syntax: .go warp #axis #value\n\nTeleports the user by the specified value along the specified axis.\nUse a positive value to move forward the axis, and a negative value to move backward the axis.\nVa... |
| `.go xy` | 1 Модер | Syntax: .go xy #x #y [#mapid]\n\nTeleport player to point with (#x,#y) coordinates at ground(water) level at map #mapid or same map if #mapid not provided. |
| `.go xyz` | 1 Модер | Syntax: .go xyz #x #y #z [#mapid]\n\nTeleport player to point with (#x,#y,#z) coordinates at ground(water) level at map #mapid or same map if #mapid not provided. |
| `.go zonexy` | 1 Модер | Syntax: .go zonexy #x #y [#zone]\n\nTeleport player to point with (#x,#y) client coordinates at ground(water) level in zone #zoneid or current zone if #zoneid not provided. You can look up zone using ... |

## .gobject (7 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.gobject add` | 2 GM | Syntax: .gobject add #id <spawntimeSecs>\n\nAdd a game object from game object templates to the world at your current location using the #id.\nspawntimesecs sets the spawntime, it is optional.\n\nNote... |
| `.gobject delete` | 2 GM | Syntax: .gobject delete #go_guid\nDelete gameobject with guid #go_guid. |
| `.gobject move` | 2 GM | Syntax: .gobject move #goguid [#x #y #z]\n\nMove gameobject #goguid to character coordinates (or to (#x,#y,#z) coordinates if its provide). |
| `.gobject near` | 2 GM | Syntax: .gobject near  [#distance]\n\nOutput gameobjects at distance #distance from player. Output gameobject guids and coordinates sorted by distance from character. If #distance not provided use 10 ... |
| `.gobject setphase` | 2 GM | Syntax: .gobject setphase #guid #phasemask\n\nGameobject with DB guid #guid phasemask changed to #phasemask with related world vision update for players. Gameobject state saved to DB and persistent. |
| `.gobject target` | 2 GM | Syntax: .gobject target [#go_id\|#go_name_part]\n\nLocate and show position nearest gameobject. If #go_id or #go_name_part provide then locate and show position of nearest gameobject with gameobject t... |
| `.gobject turn` | 2 GM | Syntax: .gobject turn #goguid [#z_angle]\n\nChanges gameobject #goguid orientation (rotates gameobject around z axis). Optional parameters are (#y_angle,#x_angle) values that represents rotation angle... |

## .goname (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.goname` | 1 Модер | Syntax: .goname [$charactername]\n\nTeleport to the given character. Either specify the character name or click on the character's portrait, e.g. when you are in a group. Character can be offline. |

## .gps (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.gps` | 1 Модер | Syntax: .gps [$name\|$shift-link]\n\nDisplay the position information for a selected character or creature (also if player name $name provided then for named player, or if creature/gameobject shift-li... |

## .groupgo (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.groupgo` | 1 Модер | Syntax: .groupgo [$charactername]\n\nTeleport the given character and his group to you. Teleported only online characters but original selected group member can be offline. |

## .guid (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.guid` | 2 GM | Syntax: .guid\n\nDisplay the GUID for the selected character. |

## .guild (5 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.guild create` | 2 GM | Syntax: .guild create [$GuildLeaderName] "$GuildName"\n\nCreate a guild named $GuildName with the player $GuildLeaderName (or selected) as leader.  Guild name must in quotes. |
| `.guild delete` | 2 GM | Syntax: .guild delete "$GuildName"\n\nDelete guild $GuildName. Guild name must in quotes. |
| `.guild invite` | 2 GM | Syntax: .guild invite [$CharacterName] "$GuildName"\n\nAdd player $CharacterName (or selected) into a guild $GuildName. Guild name must in quotes. |
| `.guild rank` | 2 GM | Syntax: .guild rank [$CharacterName] #Rank\n\nSet for player $CharacterName (or selected) rank #Rank in a guild. |
| `.guild uninvite` | 2 GM | Syntax: .guild uninvite [$CharacterName]\n\nRemove player $CharacterName (or selected) from a guild. |

## .help (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.help` | 0 Игрок | Syntax: .help [$command]\n\nDisplay usage instructions for the given $command. If no $command provided show list available commands. |

## .hidearea (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.hidearea` | 3 Админ | Syntax: .hidearea #areaid\n\nHide the area of #areaid to the selected character. If no character is selected, hide this area to you. |

## .honor (3 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.honor add` | 2 GM | Syntax: .honor add $amount\n\nAdd a certain amount of honor (gained today) to the selected player. |
| `.honor addkill` | 2 GM | Syntax: .honor addkill\n\nAdd the targeted unit as one of your pvp kills today (you only get honor if it's a racial leader or a player) |
| `.honor update` | 2 GM | Syntax: .honor update\n\nForce the yesterday's honor fields to be updated with today's data, which will get reset for the selected player. |

## .instance (4 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.instance listbinds` | 3 Админ | Syntax: .instance listbinds\n  Lists the binds of the selected player. |
| `.instance savedata` | 3 Админ | Syntax: .instance savedata\n  Save the InstanceData for the current player's map to the DB. |
| `.instance stats` | 3 Админ | Syntax: .instance stats\n  Shows statistics about instances. |
| `.instance unbind` | 3 Админ | Syntax: .instance unbind all\n  All of the selected\nplayer's binds will be cleared.\n.instance unbind #mapid\n Only the\nspecified #mapid instance will be cleared. |

## .itemmove (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.itemmove` | 2 GM | Syntax: .itemmove #sourceslotid #destinationslotid\n\nMove an item from slots #sourceslotid to #destinationslotid in your inventory\n\nNot yet implemented |

## .kick (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.kick` | 2 GM | Syntax: .kick [$charactername]\n\nKick the given character name from the world. If no character name is provided then the selected player (except for yourself) will be kicked. |

## .learn (11 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.learn` | 3 Админ | Syntax: .learn #spell [all]\n\nSelected character learn a spell of id #spell. If 'all' provided then all ranks learned. |
| `.learn all` | 3 Админ | Syntax: .learn all\n\nLearn all big set different spell maybe useful for Administaror. |
| `.learn all_crafts` | 2 GM | Syntax: .learn crafts\n\nLearn all professions and recipes. |
| `.learn all_default` | 1 Модер | Syntax: .learn all_default [$playername]\n\nLearn for selected/$playername player all default spells for his race/class and spells rewarded by completed quests. |
| `.learn all_gm` | 2 GM | Syntax: .learn all_gm\n\nLearn all default spells for Game Masters. |
| `.learn all_lang` | 1 Модер | Syntax: .learn all_lang\n\nLearn all languages |
| `.learn all_myclass` | 3 Админ | Syntax: .learn all_myclass\n\nLearn all spells and talents available for his class. |
| `.learn all_mypettalents` | 3 Админ | Syntax: .learn all_mypettalents\n\nLearn all talents for your pet available for his creature type (only for hunter pets). |
| `.learn all_myspells` | 3 Админ | Syntax: .learn all_myspells\n\nLearn all spells (except talents and spells with first rank learned as talent) available for his class. |
| `.learn all_mytalents` | 3 Админ | Syntax: .learn all_mytalents\n\nLearn all talents (and spells with first rank learned as talent) available for his class. |
| `.learn all_recipes` | 2 GM | Syntax: .learn all_recipes [$profession]Learns all recipes of specified profession and sets skill level to max.Example: .learn all_recipes enchanting |

## .levelup (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.levelup` | 3 Админ | Syntax: .levelup [$playername] [#numberoflevels]\n\nIncrease/decrease the level of character with $playername (or the selected if not name provided) by #numberoflevels Or +1 if no #numberoflevels prov... |

## .linkgrave (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.linkgrave` | 3 Админ | Syntax: .linkgrave #graveyard_id [alliance\|horde]\n\nLink current zone to graveyard for any (or alliance/horde faction ghosts). This let character ghost from zone teleport to graveyard after die if g... |

## .list (5 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.list areatriggers` | 3 Админ | Syntax: .list areatriggers\n\nShow areatriggers within the same map (if inside an instanceable map) or area (if inside a continent) as the user. |
| `.list creature` | 3 Админ | Syntax: .list creature #creature_id [#max_count]\n\nOutput creatures with creature id #creature_id found in world. Output creature guids and coordinates sorted by distance from character. Will be outp... |
| `.list item` | 3 Админ | Syntax: .list item #item_id [#max_count]\n\nOutput items with item id #item_id found in all character inventories, mails, auctions, and guild banks. Output item guids, item owner guid, owner account a... |
| `.list object` | 3 Админ | Syntax: .list object #gameobject_id [#max_count]\n.list object #gameobject_id [world\|zone\|area\|map] [#max_count]\nOutput gameobjects with gameobject id #gameobject_id found in world. Output gameobj... |
| `.list talents` | 3 Админ | Syntax: .list talents\n\nShow list all really known (as learned spells) talent rank spells for selected player or self. |

## .loadscripts (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.loadscripts` | 3 Админ | Syntax: .loadscripts $scriptlibraryname\n\nUnload current and load the script library $scriptlibraryname or reload current if $scriptlibraryname omitted, in case you changed it while the server was ru... |

## .lookup (21 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.lookup account email` | 2 GM | Syntax: .lookup account email $emailpart [#limit] \n\n Searchs accounts, which email including $emailpart with optional parametr #limit of results. If #limit not provided expected 100. |
| `.lookup account ip` | 2 GM | Syntax: lookup account ip $ippart [#limit] \n\n Searchs accounts, which last used ip inluding $ippart (textual) with optional parametr #$limit of results. If #limit not provided expected 100. |
| `.lookup account name` | 2 GM | Syntax: .lookup account name $accountpart [#limit] \n\n Searchs accounts, which username including $accountpart with optional parametr #limit of results. If #limit not provided expected 100. |
| `.lookup achievement` | 2 GM | Syntax: .lookup $name\nLooks up a achievement by $namepart, and returns all matches with their quest ID's. Achievement shift-links generated with information about achievment state for selected player... |
| `.lookup area` | 1 Модер | Syntax: .lookup area $namepart\n\nLooks up an area by $namepart, and returns all matches with their area ID's. |
| `.lookup creature` | 3 Админ | Syntax: .lookup creature $namepart\n\nLooks up a creature by $namepart, and returns all matches with their creature ID's. |
| `.lookup event` | 2 GM | Syntax: .lookup event $name\nAttempts to find the ID of the event with the provided $name. |
| `.lookup faction` | 3 Админ | Syntax: .lookup faction $name\nAttempts to find the ID of the faction with the provided $name. |
| `.lookup item` | 3 Админ | Syntax: .lookup item $itemname\n\nLooks up an item by $itemname, and returns all matches with their Item ID's. |
| `.lookup itemset` | 3 Админ | Syntax: .lookup itemset $itemname\n\nLooks up an item set by $itemname, and returns all matches with their Item set ID's. |
| `.lookup object` | 3 Админ | Syntax: .lookup object $objname\n\nLooks up an gameobject by $objname, and returns all matches with their Gameobject ID's. |
| `.lookup player account` | 2 GM | Syntax: .lookup player account $accountpart [#limit] \n\n Searchs players, which account username including $accountpart with optional parametr #limit of results. If #limit not provided expected 100. |
| `.lookup player email` | 2 GM | Syntax: .lookup player email $emailpart [#limit] \n\n Searchs players, which account email including $emailpart with optional parametr #limit of results. If #limit not provided expected 100. |
| `.lookup player ip` | 2 GM | Syntax: .lookup player ip $ippart [#limit] \n\n Searchs players, which account last used ip inluding $ippart (textual) with optional parametr #limit of results. If #limit not provided expected 100. |
| `.lookup pool` | 2 GM | Syntax: .lookup pool $pooldescpart\n\nList of pools (anywhere) with substring in description. |
| `.lookup quest` | 3 Админ | Syntax: .lookup quest $namepart\n\nLooks up a quest by $namepart, and returns all matches with their quest ID's. |
| `.lookup skill` | 3 Админ | Syntax: .lookup skill $$namepart\n\nLooks up a skill by $namepart, and returns all matches with their skill ID's. |
| `.lookup spell` | 3 Админ | Syntax: .lookup spell $namepart\n\nLooks up a spell by $namepart, and returns all matches with their spell ID's. |
| `.lookup taxinode` | 3 Админ | Syntax: .lookup taxinode $substring\n\nSearch and output all taxinodes with provide $substring in name. |
| `.lookup tele` | 1 Модер | Syntax: .lookup tele $substring\n\nSearch and output all .tele command locations with provide $substring in name. |
| `.lookup title` | 2 GM | Syntax: .lookup title $$namepart\n\nLooks up a title by $namepart, and returns all matches with their title ID's and index's. |

## .mailbox (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.mailbox` | 3 Админ | Syntax: .mailbox\n\nShow your mailbox content. |

## .maxskill (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.maxskill` | 3 Админ | Syntax: .maxskill\nSets all skills of the targeted player to their maximum values for its current level. |

## .modify (23 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.modify arena` | 1 Модер | Syntax: .modify arena #value\nAdd $amount arena points to the selected player. |
| `.modify aspeed` | 1 Модер | Syntax: .modify aspeed #rate\n\nModify all speeds -run,swim,run back,swim back- of the selected player to "normalbase speed for this move type"*rate. If no player is selected, modify your speed.\n\n #... |
| `.modify bwalk` | 1 Модер | Syntax: .modify bwalk #rate\n\nModify the speed of the selected player while running backwards to "normal walk back speed"*rate. If no player is selected, modify your speed.\n\n #rate may range from 0... |
| `.modify drunk` | 1 Модер | Syntax: .modify drunk #value\n Set drunk level to #value (0..100). Value 0 remove drunk state, 100 is max drunked state. |
| `.modify energy` | 1 Модер | Syntax: .modify energy #energy\n\nModify the energy of the selected player. If no player is selected, modify your energy. |
| `.modify faction` | 1 Модер | Syntax: .modify faction #factionid #flagid #npcflagid #dynamicflagid\n\nModify the faction and flags of the selected creature. Without arguments, display the faction and flags of the selected creature... |
| `.modify fly` | 1 Модер | Syntax: .modify fly #rate\n.fly #rate\n\nModify the flying speed of the selected player to "normal base fly speed"*rate. If no player is selected, modify your fly.\n\n #rate may range from 0.1 to 10. |
| `.modify gender` | 2 GM | Syntax: .modify gender male/female\n\nChange gender of selected player. |
| `.modify honor` | 1 Модер | Syntax: .modify honor $amount\n\nAdd $amount honor points to the selected player. |
| `.modify hp` | 1 Модер | Syntax: .modify hp #newhp\n\nModify the hp of the selected player. If no player is selected, modify your hp. |
| `.modify mana` | 1 Модер | Syntax: .modify mana #newmana\n\nModify the mana of the selected player. If no player is selected, modify your mana. |
| `.modify money` | 1 Модер | Syntax: .modify money #money\n.money #money\n\nAdd or remove money to the selected player. If no player is selected, modify your money.\n\n #gold can be negative to remove money. |
| `.modify morph` | 2 GM | Syntax: .modify morph #displayid\n\nChange your current model id to #displayid. |
| `.modify mount` | 1 Модер | Syntax: .modify mount [fast\|slow]\n\nProvide selected player a random unusual land mount. |
| `.modify phase` | 3 Админ | Syntax: .modify phase #phasemask\n\nSelected character phasemask changed to #phasemask with related world vision update. Change active until in game phase changed, or GM-mode enable/disable, or re-log... |
| `.modify rage` | 1 Модер | Syntax: .modify rage #newrage\n\nModify the rage of the selected player. If no player is selected, modify your rage. |
| `.modify rep` | 2 GM | Syntax: .modify rep #repId (#repvalue \| $rankname [#delta])\nSets the selected players reputation with faction #repId to #repvalue or to $reprank.\nIf the reputation rank name is provided, the result... |
| `.modify runicpower` | 1 Модер | Syntax: .modify runicpower #newrunicpower\n\nModify the runic power of the selected player. If no player is selected, modify your runic power. |
| `.modify scale` | 1 Модер | Syntax: .modify scale #scale\n\nChange model scale for targeted player (util relogin) or creature (until respawn). |
| `.modify speed` | 1 Модер | Syntax: .modify speed #rate\n.speed #rate\n\nModify the running speed of the selected player to "normal base run speed"*rate. If no player is selected, modify your speed.\n\n #rate may range from 0.1 ... |
| `.modify standstate` | 2 GM | Syntax: .modify standstate #emoteid\n\nChange the emote of your character while standing to #emoteid. |
| `.modify swim` | 1 Модер | Syntax: .modify swim #rate\n\nModify the swim speed of the selected player to "normal swim speed"*rate. If no player is selected, modify your speed.\n\n #rate may range from 0.1 to 10. |
| `.modify tp` | 1 Модер | Syntax: .modify tp #amount\n\nSte free talent pointes for selected character or character's pet. It will be reset to default expected at next levelup/login/quest reward. |

## .movement (3 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.movement cometome` | 3 Админ | Syntax: .movement cometome  Move selected creature to you. |
| `.movement movegens` | 2 GM | Syntax: .movement movegens  Show movement generators stack for selected creature or player. |
| `.movement movespeed` | 2 GM | Syntax: .movement movespeed  Show speed of selected creature. |

## .mute (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.mute` | 1 Модер | Syntax: .mute [$playerName] $timeInMinutes\n\nDisible chat messaging for any character from account of character $playerName (or currently selected) at $timeInMinutes minutes. Player can be offline. |

## .namego (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.namego` | 1 Модер | Syntax: .namego [$charactername]\n\nTeleport the given character to you. Character can be offline. |

## .neargrave (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.neargrave` | 3 Админ | Syntax: .neargrave [alliance\|horde]\n\nFind nearest graveyard linked to zone (or only nearest from accepts alliance or horde faction ghosts). |

## .notify (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.notify` | 1 Модер | Syntax: .notify $MessageToBroadcast\n\nSend a global message to all players online in screen. |

## .npc (27 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.npc add` | 2 GM | Syntax: .npc add #creatureid\n\nSpawn a creature by the given template id of #creatureid. |
| `.npc additem` | 2 GM | Syntax: .npc additem #itemId <#maxcount><#incrtime><#extendedcost>r\n\nAdd item #itemid to item list of selected vendor. Also optionally set max count item in vendor item list and time to item count r... |
| `.npc addweapon` | 3 Админ | Not yet implemented. |
| `.npc aiinfo` | 2 GM | Syntax: .npc npc aiinfo\n\nShow npc AI and script information. |
| `.npc allowmove` | 3 Админ | Syntax: .npc allowmove\n\nEnable or disable movement creatures in world. Not implemented. |
| `.npc changelevel` | 2 GM | Syntax: .npc changelevel #level\n\nChange the level of the selected creature to #level.\n\n#level may range from 1 to 63. |
| `.npc delete` | 2 GM | Syntax: .npc delete [#guid]\n\nDelete creature with guid #guid (or the selected if no guid is provided) |
| `.npc delitem` | 2 GM | Syntax: .npc delitem #itemId\n\nRemove item #itemid from item list of selected vendor. |
| `.npc factionid` | 2 GM | Syntax: .npc factionid #factionid\n\nSet the faction of the selected creature to #factionid. |
| `.npc flag` | 2 GM | Syntax: .npc flag #npcflag\n\nSet the NPC flags of creature template of the selected creature and selected creature to #npcflag. NPC flags will applied to all creatures of selected creature template a... |
| `.npc follow` | 2 GM | Syntax: .npc follow\n\nSelected creature start follow you until death/fight/etc. |
| `.npc info` | 3 Админ | Syntax: .npc info\n\nDisplay a list of details for the selected creature.\n\nThe list includes:\n- GUID, Faction, NPC flags, Entry ID, Model ID,\n- Level,\n- Health (current/maximum),\n\n- Field flags... |
| `.npc move` | 2 GM | Syntax: .npc move [#creature_guid]\n\nMove the targeted creature spawn point to your coordinates. |
| `.npc name` | 2 GM | Syntax: .npc name $name\n\nChange the name of the selected creature or character to $name.\n\nCommand disabled. |
| `.npc playemote` | 3 Админ | Syntax: .npc playemote #emoteid\n\nMake the selected creature emote with an emote of id #emoteid. |
| `.npc say` | 1 Модер | Syntax: .npc say #text\nMake the selected npc says #text. |
| `.npc setmodel` | 2 GM | Syntax: .npc setmodel #displayid\n\nChange the model id of the selected creature to #displayid. |
| `.npc setmovetype` | 2 GM | Syntax: .npc setmovetype [#creature_guid] stay/random/way [NODEL]\n\nSet for creature pointed by #creature_guid (or selected if #creature_guid not provided) movement type and move it to respawn positi... |
| `.npc setphase` | 2 GM | Syntax: .npc setphase #phasemask\n\nSelected unit or pet phasemask changed to #phasemask with related world vision update for players. In creature case state saved to DB and persistent. In pet case ch... |
| `.npc spawndist` | 2 GM | Syntax: .npc spawndist #dist\n\nAdjust spawndistance of selected creature to dist. |
| `.npc spawntime` | 2 GM | Syntax: .npc spawntime #time \n\nAdjust spawntime of selected creature to time. |
| `.npc subname` | 2 GM | Syntax: .npc subname $Name\n\nChange the subname of the selected creature or player to $Name.\n\nCommand disabled. |
| `.npc tame` | 2 GM | Syntax: .npc tame\n\nTame selected creature (tameable non pet creature). You don't must have pet. |
| `.npc textemote` | 1 Модер | Syntax: .npc textemote #emoteid\n\nMake the selected creature to do textemote with an emote of id #emoteid. |
| `.npc unfollow` | 2 GM | Syntax: .npc unfollow\n\nSelected creature (non pet) stop follow you. |
| `.npc whisper` | 1 Модер | Syntax: .npc whisper #playerguid #text\nMake the selected npc whisper #text to  #playerguid. |
| `.npc yell` | 1 Модер | Syntax: .npc yell #text\nMake the selected npc yells #text. |

## .pdump (2 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.pdump load` | 3 Админ | Syntax: .pdump load $filename $account [$newname] [$newguid]\nLoad character dump from dump file into character list of $account with saved or $newname, with saved (or first free) or $newguid guid. |
| `.pdump write` | 3 Админ | Syntax: .pdump write $filename $playerNameOrGUID\nWrite character dump with name/guid $playerNameOrGUID to file $filename. |

## .pinfo (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.pinfo` | 2 GM | Syntax: .pinfo [$player_name]\n\nOutput account information for selected player or player find by $player_name. |

## .pool (3 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.pool` | 2 GM | Syntax: .pool #pool_id\n\nPool information and full list creatures/gameobjects included in pool. |
| `.pool list` | 2 GM | Syntax: .pool list\n\nList of pools with spawn in current map (only work in instances. Non-instanceable maps share pool system state os useless attempt get all pols at all continents. |
| `.pool spawns` | 2 GM | Syntax: .pool spawns #pool_id\n\nList current creatures/objects listed in pools (or in specific #pool_id) and spawned (added to grid data, not meaning show in world. |

## .quest (3 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.quest add` | 3 Админ | Syntax: .quest add #quest_id\n\nAdd to character quest log quest #quest_id. Quest started from item can't be added by this command but correct .additem call provided in command output. |
| `.quest complete` | 3 Админ | Syntax: .quest complete #questid\nMark all quest objectives as completed for target character active quest. After this target character can go and get quest reward. |
| `.quest remove` | 3 Админ | Syntax: .quest remove #quest_id\n\nSet quest #quest_id state to not completed and not active (and remove from active quest list) for selected player. |

## .quit (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.quit` | 4 Root | Syntax: quit\n\nClose RA connection. Command must be typed fully (quit). |

## .recall (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.recall` | 1 Модер | Syntax: .recall [$playername]\n\nTeleport $playername or selected player to the place where he has been before last use of a teleportation command. If no $playername is entered and no player is select... |

## .reload (12 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.reload all` | 3 Админ | Syntax: .reload all\n\nReload all tables with reload support added and that can be _safe_ reloaded. |
| `.reload all_achievement` | 3 Админ | Syntax: .reload all_achievement\n\nReload all `achievement_*` tables if reload support added for this table and this table can be _safe_ reloaded. |
| `.reload all_area` | 3 Админ | Syntax: .reload all_area\n\nReload all `areatrigger_*` tables if reload support added for this table and this table can be _safe_ reloaded. |
| `.reload all_eventai` | 3 Админ | Syntax: .reload all_eventai\n\nReload `creature_ai_*` tables if reload support added for these tables and these tables can be _safe_ reloaded. |
| `.reload all_item` | 3 Админ | Syntax: .reload all_item\n\nReload `item_required_target`, `page_texts` and `item_enchantment_template` tables. |
| `.reload all_locales` | 3 Админ | Syntax: .reload all_locales\n\nReload all `locales_*` tables with reload support added and that can be _safe_ reloaded. |
| `.reload all_loot` | 3 Админ | Syntax: .reload all_loot\n\nReload all `*_loot_template` tables. This can be slow operation with lags for server run. |
| `.reload all_npc` | 3 Админ | Syntax: .reload all_npc\n\nReload `points_of_interest` and `npc_*` tables if reload support added for these tables and these tables can be _safe_ reloaded. |
| `.reload all_quest` | 3 Админ | Syntax: .reload all_quest\n\nReload all quest related tables if reload support added for this table and this table can be _safe_ reloaded. |
| `.reload all_scripts` | 3 Админ | Syntax: .reload all_scripts\n\nReload `dbscripts_on_*` tables. |
| `.reload all_spell` | 3 Админ | Syntax: .reload all\n\nReload all `spell_*` tables with reload support added and that can be _safe_ reloaded. |
| `.reload config` | 3 Админ | Syntax: .reload config\n\nReload config settings (by default stored in mangosd.conf). Not all settings can be change at reload: some new setting values will be ignored until restart, some values will ... |

## .repairitems (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.repairitems` | 2 GM | Syntax: .repairitems\n\nRepair all selected player's items. |

## .reset (8 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.reset achievements` | 3 Админ | Syntax: .reset achievements [$playername]\n\nReset achievements data for selected or named (online or offline) character. Achievements for persistance progress data like completed quests/etc re-filled... |
| `.reset all` | 3 Админ | Syntax: .reset all spells\n\nSyntax: .reset all talents\n\nRequest reset spells or talents (including talents for all character's pets if any) at next login each existed character. |
| `.reset honor` | 3 Админ | Syntax: .reset honor [Playername]\n  Reset all honor data for targeted character. |
| `.reset level` | 3 Админ | Syntax: .reset level [Playername]\n  Reset level to 1 including reset stats and talents.  Equipped items with greater level requirement can be lost. |
| `.reset specs` | 3 Админ | Syntax: .reset specs [Playername]\n  Removes all talents (for all specs) of the targeted player or named player. Playername can be name of offline character. With player talents also will be reset tal... |
| `.reset spells` | 3 Админ | Syntax: .reset spells [Playername]\n  Removes all non-original spells from spellbook.\n. Playername can be name of offline character. |
| `.reset stats` | 3 Админ | Syntax: .reset stats [Playername]\n  Resets(recalculate) all stats of the targeted player to their original VALUESat current level. |
| `.reset talents` | 3 Админ | Syntax: .reset talents [Playername]\n  Removes all talents (current spec) of the targeted player or pet or named player. With player talents also will be reset talents for all character's pets if any. |

## .respawn (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.respawn` | 3 Админ | Syntax: .respawn\n\nRespawn selected creature or respawn all nearest creatures (if none selected) and GO without waiting respawn time expiration. |

## .revive (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.revive` | 3 Админ | Syntax: .revive\n\nRevive the selected player. If no player is selected, it will revive you. |

## .save (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.save` | 0 Игрок | Syntax: .save\n\nSaves your character. |

## .saveall (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.saveall` | 1 Модер | Syntax: .saveall\n\nSave all characters in game. |

## .send (7 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.send items` | 3 Админ | Syntax: .send items #playername "#subject" "#text" itemid1[:count1] itemid2[:count2] ... itemidN[:countN]\n\nSend a mail to a player. Subject and mail text must be in "". If for itemid not provided re... |
| `.send mail` | 1 Модер | Syntax: .send mail #playername "#subject" "#text"\n\nSend a mail to a player. Subject and mail text must be in "". |
| `.send mass items` | 3 Админ | Syntax: .send mass items #racemask\|$racename\|alliance\|horde\|all "#subject" "#text" itemid1[:count1] itemid2[:count2] ... itemidN[:countN]\n\nSend a mail to players. Subject and mail text must be i... |
| `.send mass mail` | 3 Админ | Syntax: .send mass mail #racemask\|$racename\|alliance\|horde\|all "#subject" "#text"\n\nSend a mail to players. Subject and mail text must be in "". |
| `.send mass money` | 3 Админ | Syntax: .send mass money #racemask\|$racename\|alliance\|horde\|all "#subject" "#text" #money\n\nSend mail with money to players. Subject and mail text must be in "". |
| `.send message` | 3 Админ | Syntax: .send message $playername $message\n\nSend screen message to player from ADMINISTRATOR. |
| `.send money` | 3 Админ | Syntax: .send money #playername "#subject" "#text" #money\n\nSend mail with money to a player. Subject and mail text must be in "". |

## .server (16 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.server corpses` | 2 GM | Syntax: .server corpses\n\nTriggering corpses expire check in world. |
| `.server exit` | 4 Root | Syntax: .server exit\n\nTerminate mangosd NOW. Exit code 0. |
| `.server idlerestart` | 3 Админ | Syntax: .server idlerestart #delay\n\nRestart the server after #delay seconds if no active connections are present (no players). Use #exist_code or 2 as program exist code. |
| `.server idlerestart cancel` | 3 Админ | Syntax: .server idlerestart cancel\n\nCancel the restart/shutdown timer if any. |
| `.server idleshutdown` | 3 Админ | Syntax: .server idleshutdown #delay [#exist_code]\n\nShut the server down after #delay seconds if no active connections are present (no players). Use #exist_code or 0 as program exist code. |
| `.server idleshutdown cancel` | 3 Админ | Syntax: .server idleshutdown cancel\n\nCancel the restart/shutdown timer if any. |
| `.server info` | 0 Игрок | Syntax: .server info\n\nDisplay server version and the number of connected players. |
| `.server log filter` | 4 Root | Syntax: .server log filter [($filtername\|all) (on\|off)]\n\nShow or set server log filters. If used "all" then all filters will be set to on/off state. |
| `.server log level` | 4 Root | Syntax: .server log level [#level]\n\nShow or set server log level (0 - errors only, 1 - basic, 2 - detail, 3 - debug). |
| `.server motd` | 0 Игрок | Syntax: .server motd\n\nShow server Message of the day. |
| `.server plimit` | 3 Админ | Syntax: .server plimit [#num\|-1\|-2\|-3\|reset\|player\|moderator\|gamemaster\|administrator]\n\nWithout arg show current player amount and security level limitations for login to server, with arg se... |
| `.server restart` | 3 Админ | Syntax: .server restart #delay\n\nRestart the server after #delay seconds. Use #exist_code or 2 as program exist code. |
| `.server restart cancel` | 3 Админ | Syntax: .server restart cancel\n\nCancel the restart/shutdown timer if any. |
| `.server set motd` | 3 Админ | Syntax: .server set motd $MOTD\n\nSet server Message of the day. |
| `.server shutdown` | 3 Админ | Syntax: .server shutdown #delay [#exit_code]\n\nShut the server down after #delay seconds. Use #exit_code or 0 as program exit code. |
| `.server shutdown cancel` | 3 Админ | Syntax: .server shutdown cancel\n\nCancel the restart/shutdown timer if any. |

## .setskill (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.setskill` | 3 Админ | Syntax: .setskill #skill #level [#max]\n\nSet a skill of id #skill with a current skill value of #level and a maximum value of #max (or equal current maximum if not provide) for the selected character... |

## .showarea (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.showarea` | 3 Админ | Syntax: .showarea #areaid\n\nReveal the area of #areaid to the selected character. If no character is selected, reveal this area to you. |

## .stable (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.stable` | 3 Админ | Syntax: .stable\n\nShow your pet stable. |

## .start (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.start` | 0 Игрок | Syntax: .start\n\nTeleport you to the starting area of your character. |

## .taxicheat (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.taxicheat` | 1 Модер | Syntax: .taxicheat on/off\n\nTemporary grant access or remove to all taxi routes for the selected character. If no character is selected, hide or reveal all routes to you.\n\nVisited taxi nodes sill a... |

## .tele (5 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.tele` | 1 Модер | Syntax: .tele #location\n\nTeleport player to a given location. |
| `.tele add` | 3 Админ | Syntax: .tele add $name\n\nAdd current your position to .tele command target locations list with name $name. |
| `.tele del` | 3 Админ | Syntax: .tele del $name\n\nRemove location with name $name for .tele command locations list. |
| `.tele group` | 1 Модер | Syntax: .tele group#location\n\nTeleport a selected player and his group members to a given location. |
| `.tele name` | 1 Модер | Syntax: .tele name [#playername] #location\n\nTeleport the given character to a given location. Character can be offline. |

## .ticket (10 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.ticket` | 2 GM | Syntax: .ticket #id\n\nActs as an alias of: ".ticket read"\n |
| `.ticket discard` | 2 GM | Syntax: .ticket discard #id [$conclusion]\n\nClose GM ticket with number #id as discarded. If $conclusion is provided, it will be visible to the author as well. |
| `.ticket escalate` | 2 GM | Syntax: .ticket escalate #id\n\nAttempt to escalate GM ticket with number #id. Current assignee will be unassigned on success. |
| `.ticket go` | 2 GM | Syntax: .ticket go #id\n\nAttempt to teleport to the location where GM ticket with number #id was originally created. |
| `.ticket goname` | 2 GM | Syntax: .ticket goname #id\n\nAttempt to teleport to the author of the GM ticket with number #id. |
| `.ticket note` | 2 GM | Syntax: .ticket note #id $message\n\nAdd a note visible only to GMs to the GM ticket with number #id. |
| `.ticket read` | 2 GM | Syntax: .ticket read #id\n\nShow contents of GM ticket with number #id. |
| `.ticket resolve` | 2 GM | Syntax: .ticket resolve #id [$conclusion]\n\nClose GM ticket with number #id as resolved. If $conclusion is provided, it will be visible to player as well. |
| `.ticket sort` | 2 GM | Syntax: .ticket sort #id #categoryid\n\nAttempt to assign the GM ticket with number #id with a category by id #categoryid. |
| `.ticket whisper` | 2 GM | Syntax: .ticket whisper #id $message\n\nAttempt to answer in-game GM ticket with number #id by sending whisper with $message. Ticket will be assigned regardless of author's online status. |

## .tickets (3 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.tickets` | 2 GM | Syntax: .tickets [on\|off\|[#categoryid #max\|#max] [online]]\n\nIf "on"/"off" provided, enable or disable in-game GM ticket queue notifications and GM ticket alerts. Acts as an alias of ".tickets lis... |
| `.tickets list` | 2 GM | Syntax: .tickets list [#categoryid #max\|#max] [online]\n\nShow current GM tickets queue. If #categoryid is provided, show only GM tickets from that category. |
| `.tickets queue` | 3 Админ | Syntax: .tickets queue on\|off\n\nEnable or disable GM tickets queue until next restart or administrator's command. |

## .titles (4 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.titles add` | 2 GM | Syntax: .titles add #title\nAdd title #title (id or shift-link) to known titles list for selected player. |
| `.titles current` | 2 GM | Syntax: .titles current #title\nSet title #title (id or shift-link) as current selected titl for selected player. If title not in known title list for player then it will be added to list. |
| `.titles remove` | 2 GM | Syntax: .titles remove #title\nRemove title #title (id or shift-link) from known titles list for selected player. |
| `.titles setmask` | 2 GM | Syntax: .titles setmask #mask\n\nAllows user to use all titles from #mask.\n\n #mask=0 disables the title-choose-field |

## .trigger (3 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.trigger` | 2 GM | Syntax: .trigger [#trigger_id\|$trigger_shift-link\|$trigger_target_shift-link]\n\nShow detail infor about areatrigger with id #trigger_id or trigger id associated with shift-link. If areatrigger id o... |
| `.trigger active` | 2 GM | Syntax: .trigger active\n\nShow list of areatriggers with activation zone including current character position. |
| `.trigger near` | 2 GM | Syntax: .trigger near [#distance]\n\nOutput areatriggers at distance #distance from player. If #distance not provided use 10 as default value. |

## .unaura (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.unaura` | 3 Админ | Syntax: .unaura #spellid\n\nRemove aura due to spell #spellid from the selected Unit. |

## .unban (3 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.unban account` | 3 Админ | Syntax: .unban account $Name\nUnban accounts for account name pattern. |
| `.unban character` | 3 Админ | Syntax: .unban character $Name\nUnban accounts for character name pattern. |
| `.unban ip` | 3 Админ | Syntax : .unban ip $Ip\nUnban accounts for IP pattern. |

## .unlearn (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.unlearn` | 3 Админ | Syntax: .unlearn #spell [all]\n\nUnlearn for selected player a spell #spell.  If 'all' provided then all ranks unlearned. |

## .unmute (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.unmute` | 1 Модер | Syntax: .unmute [$playerName]\n\nRestore chat messaging for any character from account of character $playerName (or selected). Character can be ofline. |

## .waterwalk (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.waterwalk` | 2 GM | Syntax: .waterwalk on/off\n\nSet on/off waterwalk state for selected player. |

## .wchange (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.wchange` | 3 Админ | Syntax: .wchange #weathertype #status\n\nSet current weather to #weathertype with an intensity of #status.\n\n#weathertype can be 1 for rain, 2 for snow, and 3 for sand. #status can be 0 for disabled,... |

## .whispers (1 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.whispers` | 1 Модер | Syntax: .whispers on\|off\nEnable/disable accepting whispers by GM from players. By default use mangosd.conf setting. |

## .wp (4 команд)

| Команда | Уровень | Описание |
|---------|---------|----------|
| `.wp add` | 2 GM | Syntax: .wp add [Selected Creature or dbGuid] [pathId [wpOrigin] ] |
| `.wp export` | 3 Админ | Syntax: .wp export [#creature_guid or Select a Creature] $filename |
| `.wp modify` | 2 GM | Syntax: .wp modify command [dbGuid, id] [value]\nwhere command must be one of: waittime  \| scriptid \| orientation \| del \| move\nIf no waypoint was selected, one can be chosen with dbGuid and id.\n... |
| `.wp show` | 2 GM | Syntax: .wp show command [dbGuid] [pathId [wpOrigin] ]\nwhere command can have one of the following values\non (to show all related wp)\nfirst (to see only first one)\nlast (to see only last one)\noff... |
