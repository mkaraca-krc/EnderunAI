#!/usr/bin/env bash
#
# KIRMIZIYI OKUNDU İŞARETLE — SİLMEZ, ARŞİVE TAŞIR.
#
# Kullanım: kirmizi-okundu.sh --hepsi [--not "ne yapıldı"]
#
# Satırlar `okunan-kirmizilar.txt`e, okuma zamanı ve notla birlikte
# taşınır. Defterden satır SİLİNMİYOR — "bazı satırları temizleyen bir
# defter delil olmaktan çıkar" (DERSLER).
#
set -uo pipefail

DEFTER="${KIRMIZI_DEFTERI:-/var/lib/enderun-ai/okunmamis-kirmizilar.txt}"
ARSIV="${KIRMIZI_ARSIVI:-/var/lib/enderun-ai/okunan-kirmizilar.txt}"
NOT=""
HEPSI=0

while [ $# -gt 0 ]; do
    case "$1" in
        --hepsi) HEPSI=1; shift ;;
        --not)   NOT="${2:-}"; shift 2 || shift ;;
        *) echo "[kirmizi-okundu] bilinmeyen argüman: $1" >&2; exit 2 ;;
    esac
done

[ "$HEPSI" = "1" ] || { echo "[kirmizi-okundu] --hepsi gerekli." >&2; exit 2; }

if [ ! -s "$DEFTER" ]; then
    echo "[kirmizi-okundu] Okunacak kırmızı yok."
    exit 0
fi

mkdir -p "$(dirname "$ARSIV")" 2>/dev/null || true
simdi="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
adet=0
while IFS= read -r satir; do
    [ -n "$satir" ] || continue
    printf '%s | OKUNDU %s | %s\n' "$satir" "$simdi" "${NOT:-(not yok)}" >> "$ARSIV"
    adet=$((adet + 1))
done < "$DEFTER"

: > "$DEFTER"
echo "[kirmizi-okundu] $adet satır arşive taşındı: $ARSIV"
