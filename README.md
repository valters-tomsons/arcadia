# Arcadia

An experimental project in emulating backend services for Battlefield games based on Frostbite *1.x*, targeting Playstation 3 clients. Project is in early development and is **not playable in any form**. 

**Note**: This project is not affiliated with EA or DICE.

## Features

Currently, enough is implemented to allow connection to backend services and browse menus which require connection with "*EA Online*".

Both PSN and RPCN clients are supported, for now.

* PS3 BC2 - login, partial game server connection
* PS3 1943 - login, access to game tutorial
* PS3 BC1 - partial connection (*requires a game patch*)

## PS3 Client Configuration

1. Set `DnsSettings.EnableDns` to `true` and `DnsSettings.ArcadiaAddress` to IP address of the backend
2. Open PS3 Network configuration, set DNS address to IP address of the backend (or DNS, if hosted seperately)

**Notice:** Valid PSN sign-in is still required.

## RPCS3 Client Configuration

1. Enable network connection and RPCN
1. Set IP/Hosts switches to:

```
bfbc2-ps3.fesl.ea.com=127.0.0.1&&beach-ps3.fesl.ea.com&&bfbc-ps3.fesl.ea.com&&theater.ps3.arcadia=127.0.0.1
```

## Special Thanks

* *[cetteup](https://github.com/cetteup)* - lot of proxy stuff, lots of knowledge of ea systems, lots of captures and for fixing my ea packet implementation! Thanks! 
* *And799* for devmenu and general frostbite knowledge
* Aim4kill for the great ProtoSSL write-up
* Battlefield Modding Discord server
* PS Rewired `#packet-captures` channel

## References

* https://github.com/Aim4kill/Bug_OldProtoSSL
* https://github.com/Tratos/BFBC2_MasterServer
* https://github.com/GrzybDev/BFBC2_MasterServer
* https://github.com/zivillian/ism7mqtt