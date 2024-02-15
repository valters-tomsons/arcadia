# Arcadia

This project is an experimental effort to emulate backend services for Battlefield games built on the Frostbite 1.x engine, with a focus on PlayStation 3 clients. It's currently in early development and **is not playable in any form**.

**Disclaimer**: This project is not affiliated with EA or DICE.

## Features

Currently, enough is implemented to allow connection to backend services and browse menus which require connection with "*EA Online*".

### Game Compatibility

Both PSN and RPCN clients are supported unless noted otherwise.

* **BC2** - login, partial game server connection
* **BC2 Server** - login, create game, initialize player connection
* **1943** - login *(easy access to game tutorial)*
* **BC1** - partial connection, requires a game patch, PSN likely won't be supported

## PS3 Client Configuration

1. Set `DnsSettings.EnableDns` to `true` and `DnsSettings.ArcadiaAddress` to IP address of the backend
2. Open PS3 Network configuration, set DNS address to IP address of the backend (or DNS, if hosted separately)

**Notice:** Valid PSN sign-in is still required.

## RPCS3 Client Configuration

1. Enable network connection and RPCN
1. Set IP/Hosts switches to:

```
bfbc2-ps3.fesl.ea.com=127.0.0.1&&beach-ps3.fesl.ea.com&&bfbc-ps3.fesl.ea.com&&theater.ps3.arcadia=127.0.0.1
```

## PC Client Configuration

**Notice:** Arcadia will not support PC clients, instruction below only for development purposes.

1. Override hosts as seen in `dist/pc/hosts.etc` or configure the DNS server
2. When logging in: enter username in email field, leave the password empty and make sure "Remember password" is not checked
3. When logging in again, make sure to clear the password field and uncheck "Remember password"

## Special Thanks

* *[cetteup](https://github.com/cetteup)* - lot of proxy stuff, lots of knowledge of ea systems, lots of captures and for fixing my ea packet implementation! Thanks! 
* *And799* for devmenu and general frostbite knowledge
* Aim4kill for the great ProtoSSL write-up
* Battlefield Modding Discord server
* `Dorian_N` & PS Rewired `#packet-captures` channel for PS3 captures

## References

* https://github.com/Aim4kill/Bug_OldProtoSSL
* https://github.com/Tratos/BFBC2_MasterServer
* https://github.com/GrzybDev/BFBC2_MasterServer
* https://github.com/zivillian/ism7mqtt
