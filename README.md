# Arcadia

Arcadia is a server emulator for [the sunsetted EA Plasma](http://web.archive.org/web/20240506160521/https://www.ea.com/games/battlefield/legacy-sunset) game backend services. *Primarily* targeting games on Playstation 3.

Not affiliated, associated, authorized, endorsed by, or in any way officially connected with Electronic Arts Inc. or any of its subsidiaries or affiliates. The use of any trademarks, logos, or brand names is for identification purposes only and does not imply endorsement or affiliation.

## Community

Discord: https://discord.gg/9WbQFYEt8B

## Game Compatibility

Both PSN and RPCN clients are supported and can play in the same server.

Game                       |   Status      | Live status
-------------------------- | ----------    | ----- 
Mercenaries 2              | Online        | No leaderboards.
Medal of Honor Airborne    | Online        | No stats.
Lord of the Rings Conquest | Online*       | *Requires patch or PC to host public servers. 
Bad Company 2              | COOP Only     | **Requires Onslaught DLC**, only player hosted servers. No servers for regular multiplayer.
Battlefield 1943           | Tutorial Only | Can connect. No servers.

## RPCS3 Configuration

1. Enable network connection and RPCN
1. Enable UPNP
1. Set `IP/Hosts switches` to the following:

> `theater.ps3.arcadia=152.53.15.83&&bfbc2-ps3.fesl.ea.com=152.53.15.83&&beach-ps3.fesl.ea.com=152.53.15.83&&mercs2-ps3.fesl.ea.com=152.53.15.83&&mohair-ps3.fesl.ea.com=152.53.15.83`

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
* https://github.com/iplocate/ip-address-databases

## Licenses

All code in this project is licensed under GNU General Public License v2.0 (GPL 2.0) except as otherwise noted. 

Certain parts of this project may be covered by different licenses as explicitly indicated in either a license header at the beginning of the relevant file, or separate LICENSE file within the applicable directory.

Please refer to the documentation of each NuGet package for their specific license details.

## Domain Name Reference Table

Game             | FESL Prefix (`*.fesl.ea.com`)
---------------- | -----------------
Bad Company 2    | bfbc2-ps3
Battlefield 1943 | beach-ps3
Army of Two 2010 | ao3-ps3
NFS Shift        | nfsps2-ps3
Bad Company 1    | bfbc-ps3
Army of Two 2008 | ao2-ps3
The Simpsons     | simpsons-ps3
MoH Airborne     | mohair-ps3
NFS ProStreet    | nfsps-ps3
NFS Carbon       | nfs-ps3
NFS Undercover   | nfsmw2-ps3
Team Fortress 2  | hl2-ps3
LOTR Conquest    | lotr-pandemic-ps3
Mercenaries 2    | mercs2-ps3
Godfather 2      | godfather2-ps3

* See [`Ports.cs`](/src/server/EA/Ports.cs) for default fesl client ports
* Theater address and port in PS3 games is controlled by Fesl's Hello message
* Arcadia always sends `theater.ps3.arcadia`
