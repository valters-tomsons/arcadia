#/bin/sh

dotnet build

cd src/server

sudo setcap 'cap_net_bind_service=+ep' bin/Debug/net7.0/Arcadia
./bin/Debug/net7.0/Arcadia