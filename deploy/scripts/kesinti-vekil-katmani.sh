#!/usr/bin/env bash
#
# KESİNTİ — VEKİL KATMANI ÖLÇÜMÜ (nginx). TEK KAYNAK, SINANABİLİR.
#
# ═══ NEDEN VAR ═══
#
# 2026-09-15 yayınında kesinti kapısı "hatasız" dedi ve DOĞRU söyledi —
# ama yalnız uygulama katmanını izliyordu (ön yüz parçası + /api/health).
# Aynı pencerede nginx **8 adet 502** kaydetti ve kullanıcı onları gördü.
#
# **Bir kapı, kullanıcının gördüğü katmanı ölçmüyorsa, ölçtüğü katman
# temizken de yalan söyler.**
#
# ═══ AYRIM ═══
#
#   502          → takas penceresi. BEKLENEN ve ölçülmüş bedel (6–14 sn).
#                  Sayılır, raporlanır, kırmızı YAKMAZ.
#   502 dışı 5xx → uygulama hatası. KIRMIZI.
#
# ═══ PENCERE ═══
#
# Günlük append-only: başlangıçtaki satır sayısı verilir, ondan sonrası
# okunur. Günlük yayın sırasında DÖNERSE sayı küçülür — o hâl
# **ÖLÇEMEDİ**'dir, sessizce 0 sayılmaz.
#
# ÇIKIŞ: 0 temiz (502 olabilir) · 1 502 dışı 5xx var · 3 ÖLÇEMEDİ
#
set -uo pipefail
BASLANGIC="${1:?başlangıç satır sayısı gerekli}"
GUNLUK="${KESINTI_NGINX_GUNLUK:-/var/log/nginx/access.log}"

if [ ! -r "$GUNLUK" ]; then
    echo "durum=ÖLÇEMEDİ sebep=günlük okunamadı"; exit 3
fi
case "$BASLANGIC" in ''|*[!0-9]*) echo "durum=ÖLÇEMEDİ sebep=başlangıç sayı değil"; exit 3 ;; esac

simdi="$(wc -l < "$GUNLUK" 2>/dev/null || echo 0)"
if [ "$simdi" -lt "$BASLANGIC" ]; then
    echo "durum=ÖLÇEMEDİ sebep=günlük dönmüş (${simdi} < ${BASLANGIC})"; exit 3
fi

satirlar="$(tail -n +"$((BASLANGIC + 1))" "$GUNLUK" 2>/dev/null | grep -E '"[A-Z]+ /api/' || true)"
b502="$(printf '%s\n' "$satirlar" | grep -c '" 502 ' || true)"
diger="$(printf '%s\n' "$satirlar" | grep -cE '" 5(0[013-9]|[1-9][0-9]) ' || true)"

echo "durum=ölçüldü 502=${b502} diger=${diger}"
[ "${diger:-0}" -eq 0 ]
