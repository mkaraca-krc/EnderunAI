#!/usr/bin/env bash
#
# VEKİL KATMANI ÖLÇÜMÜ — SONDA (dört ayak, beklenen sonuç önden yazılı)
#
# Doğuran olay: 2026-09-15 yayınında kesinti kapısı "hatasız" dedi;
# nginx aynı pencerede 8 adet 502 gördü. Kapı kullanıcının gördüğü
# katmanı ölçmüyordu.
#
# AYAKLAR (Kural 61):
#   1 yalnız 502          → GEÇER (çıkış 0), 502 sayısı raporlanır
#   2 502 dışı 5xx var    → KIRMIZI (çıkış 1)
#   3 günlük dönmüş       → ÖLÇEMEDİ (çıkış 3) — sessiz 0 YOK
#   4 hiç 5xx yok         → GEÇER (çıkış 0)  [pozitif kontrol]
#
set -uo pipefail
KOK="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OLC="$KOK/scripts/kesinti-vekil-katmani.sh"
Z="$(mktemp -d)"; trap 'rm -rf "$Z"' EXIT
gecti=0; dustu=0

satir() { printf '1.2.3.4 - - [15/Sep/2026:12:33:%02d +0000] "GET /api/backend/%s HTTP/1.1" %s 10 "-" "-"\n' "$1" "$2" "$3"; }

ayak() { # ad · beklenen_cikis · beklenen_metin
    local ad="$1" bk="$2" bm="$3" cikti kod
    cikti="$(KESINTI_NGINX_GUNLUK="$Z/access.log" bash "$OLC" 1 2>&1)"; kod=$?
    if [ "$kod" = "$bk" ] && { [ -z "$bm" ] || grep -qF "$bm" <<<"$cikti"; }; then
        echo "  ✓ $ad (çıkış $kod · $cikti)"; gecti=$((gecti+1))
    else
        echo "  ✗ $ad — beklenen çıkış $bk metin '$bm'; gelen $kod '$cikti'"; dustu=$((dustu+1))
    fi
}

echo "[sonda] kesinti vekil katmanı"

{ satir 1 x 200; satir 2 auth/me 502; satir 3 companies 502; } > "$Z/access.log"
ayak "1 yalnız 502 → GEÇER"            0 "502=2"

{ satir 1 x 200; satir 2 auth/me 502; satir 3 cash-flow 500; } > "$Z/access.log"
ayak "2 502 dışı 5xx → KIRMIZI"        1 "diger=1"

# Günlük dönmesi: başlangıç 1 verilirken dosya BOŞALIYOR (0 satır).
: > "$Z/access.log"
ayak "3 günlük dönmüş → ÖLÇEMEDİ"      3 "ÖLÇEMEDİ"

{ satir 1 x 200; satir 2 auth/me 200; } > "$Z/access.log"
ayak "4 hiç 5xx yok → GEÇER"           0 "502=0"

echo "[sonda] geçti: $gecti · düştü: $dustu"
[ "$dustu" -eq 0 ]
