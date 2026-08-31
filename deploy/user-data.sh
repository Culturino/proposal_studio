#!/bin/bash
set -eux
dnf update -y
dnf install -y postgresql16 unzip tar
curl -sL "https://caddyserver.com/api/download?os=linux&arch=amd64" -o /usr/local/bin/caddy
chmod +x /usr/local/bin/caddy
mkdir -p /opt/proposal-studio /etc/proposal-studio /var/log/proposal-studio
