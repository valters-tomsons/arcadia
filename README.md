# Arcadia

Arcadia is an experimental emulator for [the sunsetted EA Plasma](http://web.archive.org/web/20240506160521/https://www.ea.com/games/battlefield/legacy-sunset) game backend services. Primarily targeting Frostbite games on PS3. 
It's currently in early development and only supports player-hosted co-op Onslaught mode DLC. 

Not affiliated with EA or DICE.

## Game Compatibility

Both PSN and RPCN clients are supported unless noted otherwise.

Game             |   Status | Notes | IP/Hosts switches
-----------------| ------   | ----- | ----
Bad Company 2    | Playable | Player hosted onslaught games are supported | `bfbc2-ps3.fesl.ea.com=152.53.15.83&&theater.ps3.arcadia=152.53.15.83`
Battlefield 1943 | Login    | Can access the tutorial | `N/A`

## RPCS3 Configuration

1. Enable network connection and RPCN
1. Enable UPNP
1. Set IP/Hosts switches as listed in table above

## Special Thanks

* *[cetteup](https://github.com/cetteup)* - lot of proxy stuff, lots of knowledge of ea systems, lots of captures and for fixing my ea packet implementation! Thanks! 
* *And799* for devmenu and general frostbite knowledge
* Aim4kill for the great ProtoSSL write-up
* Battlefield Modding Discord server
* `Dorian_N` & PS Rewired `#packet-captures` channel for PS3 captures

## Resources

* https://github.com/Aim4kill/Bug_OldProtoSSL
* https://github.com/Tratos/BFBC2_MasterServer
* https://github.com/GrzybDev/BFBC2_MasterServer
* https://github.com/zivillian/ism7mqtt

## Domain Name Reference Table

Game             | FESL Domain
-----------------| ------   
Bad Company 2    | bfbc2-ps3.fesl.ea.com
Battlefield 1943 | beach-ps3.fesl.ea.com
Bad Company 1    | bfbc-ps3.fesl.ea.com
Army of Two      | ao2-ps3.fesl.ea.com

Theater address is generally controlled via Fesl's Server Hello, so Arcadia always sends `theater.ps3.arcadia`. Only `Win32` game has a hardcoded theater address. 