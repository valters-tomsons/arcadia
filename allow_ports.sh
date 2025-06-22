#/bin/sh

sudo sysctl net.ipv4.ip_unprivileged_port_start=80
sudo setcap 'cap_net_bind_service=+ep' src/server/bin/Debug/net9.0/Arcadia