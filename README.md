# Arcadia

Arcadia is an experimental emulator for [the sunsetted EA Plasma](http://web.archive.org/web/20240506160521/https://www.ea.com/games/battlefield/legacy-sunset) game backend services. Primarily targeting Frostbite games on PS3.
It's currently in early development and currently only supports player-hosted co-op Onslaught DLC in Bad Company 2. 

Not affiliated with EA or DICE.

## Community

Discord: https://discord.gg/9WbQFYEt8B

## Game Compatibility

Both PSN and RPCN clients are supported and can play in the same server.

Game             |   Status | Live status
-----------------| ------   | ----- 
Bad Company 2    | Semi-Restored | **Requires Onslaught DLC!** Players can only host and play public CO-OP Onslaught matches.
Battlefield 1943 | Not Playable    | Only access to tutorial works. Playgroups/lobbies are semi-functional. No servers.

## RPCS3 Configuration

1. Enable network connection and RPCN
1. Enable UPNP
1. Set IP/Hosts switches as listed in table above:

- BFBC2: `fbc2-ps3.fesl.ea.com=152.53.15.83&&theater.ps3.arcadia=152.53.15.83`

## PS3 Configuration

1. [Follow this PSRewired guide.](https://psrewired.com/guides/ps3)
5. Start the game and sign-in to PSN when prompted

## Special Thanks

* *[cetteup](https://github.com/cetteup)* - lot of proxy stuff, lots of knowledge of ea systems, lots of captures and for fixing my ea packet implementation! Thanks! 
* [Aim4kill](https://github.com/Aim4kill) for the great ProtoSSL vulnerability write-up
* *And799* for devmenu and general frostbite knowledge
* [Battlefield Modding](https://duckduckgo.com/?t=ffab&q=battlefield+modding+discord)
* 1UP, Dorian_N from [PSRewired](https://psrewired.com)

## Resources

* https://github.com/Aim4kill/Bug_OldProtoSSL
* https://github.com/Tratos/BFBC2_MasterServer
* https://github.com/GrzybDev/BFBC2_MasterServer
* https://github.com/zivillian/ism7mqtt
* https://github.com/RipleyTom/rpcn
* https://www.psdevwiki.com/ps3/X-I-5-Ticket

## Domain Name Reference Table

Game             | FESL Domain
-----------------| ------   
Bad Company 2    | bfbc2-ps3.fesl.ea.com
Battlefield 1943 | beach-ps3.fesl.ea.com
Bad Company 1    | bfbc-ps3.fesl.ea.com
Army of Two      | ao2-ps3.fesl.ea.com
General "CDN"    | easo.ea.com

Theater address is generally controlled via Fesl's Server Hello, so Arcadia always sends `theater.ps3.arcadia`. Only `Win32` game has a hardcoded theater address. 