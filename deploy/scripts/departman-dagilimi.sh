#!/usr/bin/env bash
# Departman atama doğrulaması — sorguyu canlıda koşar.
# Sır basmaz: bağlantı dizesi ortam dosyasından okunur, ekrana gelmez.
set -uo pipefail
KOK="${REPO_ROOT:-/var/www/enderun-ai}"
canli="$(sudo grep -E '^DB_CONNECTION=' /etc/enderunai/backend.env | sed -E 's/^DB_CONNECTION=//' | tr -d "'\"")"
h=$(sed -E 's/.*Host=([^;]*).*/\1/' <<<"$canli")
u=$(sed -E 's/.*Username=([^;]*).*/\1/' <<<"$canli")
p=$(sed -E 's/.*Password=([^;]*).*/\1/' <<<"$canli")
d=$(sed -E 's/.*Database=([^;]*).*/\1/' <<<"$canli")
PGPASSWORD="$p" psql -h "$h" -U "$u" -d "$d" -F ' | ' -A \
    -f "${KOK}/deploy/scripts/departman-dagilimi.sql"
