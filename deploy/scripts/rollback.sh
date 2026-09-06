#!/usr/bin/env bash
#
# DİKKAT — BU, SAFE-DEPLOY'UN GERİ ALMASI DEĞİLDİR.
#
# Yalnız `backup.sh`in kopyaladığı altı ön yüz dosyasını geri koyar ve
# hiçbir yerden çağrılmaz (ölçüldü 2026-09-06). safe-deploy kendi geri
# almasını `*_ROLLBACK_DIR` üzerinden yapar. Veri geri yükleme için
# felaket yordamına bakın.
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
