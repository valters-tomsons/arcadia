# Arcadia

An experimental project in emulating backend services for Battlefield 1943 (possibly others!). Project is in early development and is not yet playable. 

**Note**: This project is not affiliated with EA or DICE.

## Features

Currently, enough is "implemented" to allow accessing the game's tutorial. When asked to enter email/password, just submit whatever (or keep them empty).

## RPCS3 Client Configuration

1. Enable network connection and RPCN
1. Set IP/Hosts switches to:

```
bfbc2-ps3.fesl.ea.com=127.0.0.1&&beach-ps3.fesl.ea.com&&bfbc-ps3.fesl.ea.com&&theater.ps3.arcadia=127.0.0.1
```

## Special Thanks

* *[cetteup](https://github.com/cetteup)* - lot of proxy stuff, lots of knowledge of ea systems, lots of captures and for fixing my ea packet implementation! Thanks! 
* *And799* for devmenu and general frostbite knowledge
* Battlefield Modding Discord server
* PS Rewired `#packet-captures` channel

## References

* https://github.com/Aim4kill/Bug_OldProtoSSL
* https://github.com/Tratos/BFBC2_MasterServer