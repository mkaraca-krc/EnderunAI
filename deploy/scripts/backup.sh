#!/usr/bin/env bash
#
# DİKKAT — BU BİR VERİ YEDEĞİ DEĞİLDİR VE HİÇBİR YERDEN ÇAĞRILMIYOR.
#
# ÖLÇÜLDÜ (2026-09-06): depoda hiçbir betik, servis ya da zamanlayıcı
# bu dosyayı çağırmıyor. `safe-deploy.sh` kendi içindeki
# `backup_current_release()` fonksiyonunu kullanıyor; veri yedeğini ise
# `/usr/local/bin/enderun-backup.sh` alıyor. Burası yalnız ELLE
# çalıştırıldığında iş görür.
#
# NE YAPAR: altı ön yüz kaynak dosyasını kopyalar. Veritabanına,
# yüklenen dosyalara, proje dosyalarına DOKUNMAZ. Buradan geri yükleme
# yapmak veriyi geri getirmez.
#
# DURUM.md'deki eski kayıt "safe-deploy her yayından önce ikisini de
# çağırıyor" diyordu; O KAYIT YANLIŞTI, 2026-09-06'da düzeltildi.
#
# Silinip silinmeyeceği AÇIK KARARLAR'da (AK-8).
set -Eeuo pipefail
D="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${D}/common.sh"
ensure_dirs
STAMP="$(timestamp)"
DEST="${BACKUP_ROOT}/release-foundation-rc1-${STAMP}"
mkdir -p "${DEST}/frontend"
for item in middleware.ts app/api/auth/login/route.ts app/api/backend/'[...path]'/route.ts app/login/page.tsx app/globals.css components/erp/erp-shell.tsx; do
  if [[ -e "${FRONTEND_SOURCE}/${item}" ]]; then
    mkdir -p "${DEST}/frontend/$(dirname "${item}")"
    cp -a "${FRONTEND_SOURCE}/${item}" "${DEST}/frontend/${item}"
  fi
done
echo "${DEST}"
