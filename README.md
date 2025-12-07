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
1. Set `IP/Hosts switches` to the following:

> `theater.ps3.arcadia=152.53.15.83&&bfbc2-ps3.fesl.ea.com=152.53.15.83&&beach-ps3.fesl.ea.com=152.53.15.83`

## PS3 Configuration

1. [Follow this PSRewired guide.](https://psrewired.com/guides/ps3)
5. Start the game and sign-in to PSN when prompted

## Special Thanks

* *[cetteup](https://github.com/cetteup)* - lot of proxy stuff, lots of knowledge of ea systems, lots of captures and for fixing my ea packet implementation! Thanks! 
* *[Aim4kill](https://github.com/Aim4kill)* for the great ProtoSSL vulnerability write-up
* *[And799](https://www.youtube.com/@andersson799)* for devmenu and general frostbite knowledge
* [PSRewired](https://psrewired.com): `1UP` for inclusion in DNS, `Dorian_D` for packet captures
* [Battlefield Modding](https://duckduckgo.com/?t=ffab&q=battlefield+modding+discord) community

## Resources

* https://github.com/Aim4kill/Bug_OldProtoSSL
* https://github.com/Tratos/BFBC2_MasterServer
* https://github.com/GrzybDev/BFBC2_MasterServer
* https://github.com/zivillian/ism7mqtt
* https://github.com/RipleyTom/rpcn
* https://www.psdevwiki.com/ps3/X-I-5-Ticket

## Licenses

All code in this project is licensed under GNU General Public License v2.0 (GPL 2.0) except as otherwise noted. 

Certain parts of this project may be covered by different licenses as explicitly indicated in either a license header at the beginning of the relevant file, or separate LICENSE file within the applicable directory.

Please refer to the documentation of each NuGet package for their specific license details.

## Domain Name Reference Table

Game             | FESL Prefix (`*.fesl.ea.com`) | Vulnerable SSL
---------------- | ----------------- | --------------
Bad Company 2    | bfbc2-ps3         | yes
Battlefield 1943 | beach-ps3         | yes
Army of Two 2010 | ao3-ps3           | yes
NFS Shift        | nfsps2-ps3        | yes
Bad Company 1    | bfbc-ps3          | no
Army of Two 2008 | ao2-ps3           | no
The Simpsons     | simpsons-ps3      | no
MoH Airborne     | mohair-ps3        | no
NFS ProStreet    | nfsps-ps3         | no
NFS Carbon       | nfs-ps3           | no
NFS Undercover   | nfsmw2-ps3        | no
Team Fortress 2  | hl2-ps3           | no
LOTR Conquest    | lotr-pandemic-ps3 | no
Mercenaries 2    | mercs2-ps3        | no

* See [`Ports.cs`](/src/server/EA/Ports.cs) for default fesl client ports
* Theater address and port in PS3 games is controlled by Fesl's Hello message
* Arcadia always sends `theater.ps3.arcadia`
