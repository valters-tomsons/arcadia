#/bin/sh


dotnet build

sudo sysctl net.ipv4.ip_unprivileged_port_start=80
sudo sysctl net.ipv4.ip_unprivileged_port_start=53

cd src/server/bin/Debug/net8.0/
.Arcadia