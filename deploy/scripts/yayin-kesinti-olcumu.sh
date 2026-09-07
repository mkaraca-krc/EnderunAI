#!/usr/bin/env bash
#
# YAYIN KESİNTİ ÖLÇÜMÜ — SANİYEDE BİR SAYFA İSTEĞİ, 404 SAYIMI
#
# NEDEN VAR (DAĞITIM/1): "yayın sırasında kesinti var mı" sorusu
# günlükten okunamaz. Ölçülecek şey KULLANICININ gördüğü: sayfayı
# açtığında parçalar geliyor mu.
#
# NE YAPIYOR: verilen adrese saniyede bir istek atar, HTML'deki ilk
# JS parçasını da ister ve durumlarını sayar. Sayfa 200 dönüp
# parçası 404 dönüyorsa ekran BOZUKTUR — o yüzden ikisi de ölçülür.
#
# KULLANIM: yayin-kesinti-olcumu.sh <taban-url> <saniye> [cikti]
set -uo pipefail

TABAN="${1:?taban url}"
SURE="${2:-180}"
CIKTI="${3:-/tmp/kesinti-olcumu.txt}"

: > "$CIKTI"
SAYFA_HATA=0; PARCA_HATA=0; TOPLAM=0

bitis=$(( $(date +%s) + SURE ))
while [ "$(date +%s)" -lt "$bitis" ]; do
    TOPLAM=$((TOPLAM + 1))
    govde="$(curl -sk -m 4 "${TABAN}/login" 2>/dev/null)"
    kod="$(curl -sk -m 4 -o /dev/null -w '%{http_code}' "${TABAN}/login" 2>/dev/null)"

    [ "$kod" != "200" ] && { SAYFA_HATA=$((SAYFA_HATA + 1)); echo "$(date -u +%H:%M:%S) SAYFA $kod" >> "$CIKTI"; }

    # HTML'deki ilk JS parçası — asıl kırılma noktası.
    parca="$(printf '%s' "$govde" | grep -oE '/_next/static/chunks/[A-Za-z0-9_.-]+\.js' | head -1)"
    if [ -n "$parca" ]; then
        pk="$(curl -sk -m 4 -o /dev/null -w '%{http_code}' "${TABAN}${parca}" 2>/dev/null)"
        [ "$pk" != "200" ] && { PARCA_HATA=$((PARCA_HATA + 1)); echo "$(date -u +%H:%M:%S) PARCA $pk $parca" >> "$CIKTI"; }
    fi

    sleep 1
done

echo "toplam=${TOPLAM} sayfa_hata=${SAYFA_HATA} parca_hata=${PARCA_HATA}"
