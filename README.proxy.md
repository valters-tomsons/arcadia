# arcadia-proxy

A proxy server allowing connection to official Battlefield 1943's backend services via RPCS3.

**Note**: This project is not affiliated with EA or DICE.

## Usage

### Emulator setup

1. Install RPCS3 and Battlefield 1943, if you haven't already
1. Register/Login to [RPCN](https://wiki.rpcs3.net/index.php?title=Help:Netplay)
1. Right click on Battlefield 1943 in RPCS3 and select `Change Custom Configuration`
1. In network tab, set Network Status to `Connected` and set PSN Status to `RPCN`
1. Set IP/Hosts switches to `beach-ps3.fesl.ea.com=127.0.0.1`

### Proxy

1. Download and extract the latest proxy release
1. Open `appsettings.json` and update `ProxyOverrideAccountEmail` & `ProxyOverrideAccountPassword` to your **EA account credentials**, the ones you created on your Playstation 3
1. Start the server executable
1. Start Battlefield 1943 in RPCS3

## Known Issues

* Proxy will crash when client disconnects from backend (timeouts/network issues/RPCS3 is closed)
* Sometimes login fails, just restart the proxy and try again
* Game will tell your credentials are invalid, make sure you're using your **EA account credentials** and not your PSN credentials