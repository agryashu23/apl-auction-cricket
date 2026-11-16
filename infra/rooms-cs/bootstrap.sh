#!/usr/bin/env bash
# One-time box setup (run by provision.ps1 over SSH): Caddy + systemd unit + 1 GB swap.
# Usage: sudo bash bootstrap.sh <host-name e.g. 203-0-113-10.sslip.io>
set -euo pipefail
HOST="$1"

# Swap so a 1 GB box survives memory spikes.
if [ ! -f /swapfile ]; then
  fallocate -l 1G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
  echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi

# Caddy from its official apt repo (auto Let's Encrypt).
if ! command -v caddy >/dev/null; then
  apt-get update -y
  apt-get install -y debian-keyring debian-archive-keyring apt-transport-https curl gnupg
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | gpg --dearmor --yes -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' > /etc/apt/sources.list.d/caddy-stable.list
  apt-get update -y
  apt-get install -y caddy
fi

cat > /etc/caddy/Caddyfile <<EOF
$HOST {
	handle /ws {
		reverse_proxy 127.0.0.1:8787
	}
	handle /health {
		reverse_proxy 127.0.0.1:8787
	}
	handle {
		respond "APL rooms" 404
	}
}
EOF
systemctl enable caddy
systemctl reload caddy || systemctl restart caddy

mkdir -p /opt/aplrooms/data
touch /etc/aplrooms.env
install -m 644 /tmp/aplrooms.service /etc/systemd/system/aplrooms.service
systemctl daemon-reload
systemctl enable aplrooms
echo "bootstrap done for $HOST"
