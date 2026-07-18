#!/usr/bin/env bash
set -Eeuo pipefail
D="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${D}/common.sh"
BACKUP="${1:-}"
[[ -n "${BACKUP}" && -d "${BACKUP}/frontend" ]] || fail "Geçerli yedek klasörü verin"
cp -a "${BACKUP}/frontend/." "${FRONTEND_SOURCE}/"
cd "${FRONTEND_SOURCE}"
npm run build
systemctl restart "${FRONTEND_SERVICE}"
"${D}/healthcheck.sh"
log "Rollback tamamlandı"
