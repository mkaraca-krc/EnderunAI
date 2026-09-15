#!/usr/bin/env bash
#
# YIKICI BEYAN OKUYUCUSU — SONDA (beş ayak, beklenen sonuç önden yazılı)
#
# NEDEN: `goc-provasi.sh`in beyan okuması 2026-09-15 yayınında GEÇERLİ
# BİR BEYANI görmedi (beyan 20 commit geride, kapı yalnız HEAD'e
# bakıyordu) ve yayını yanlış durdurdu. Okuma tek kaynağa çıkarıldı;
# bu sonda o kaynağı GERÇEK kodla sınıyor — kopyayla değil.
#
# AYAKLAR (Kural 61: beklenen önce yazılır):
#   1 beyan geride, taban var          → BULUR      (çıkış 0)
#   2 taban dosyası yok                → HEAD'e düşer, beyan yok (çıkış 1)
#   3 taban HEAD'in atası değil        → HEAD'e düşer, beyan yok (çıkış 1)
#   4 beyan HEAD'de, taban yok         → BULUR      (çıkış 0)  [pozitif kontrol]
#   5 aralıkta iki beyan               → İKİSİNİ DE toplar     (çıkış 0)
#
set -uo pipefail
KOK="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OKU="$KOK/scripts/yikici-beyan-oku.sh"
Z="$(mktemp -d)"
trap 'rm -rf "$Z"' EXIT

gecti=0; dustu=0
ayak() { # ad · beklenen_cikis · beklenen_metin(boş olabilir) · SON_YAYIN_DOSYASI
    local ad="$1" bk="$2" bm="$3" dosya="$4"
    local cikti kod
    cikti="$(SON_YAYIN_DOSYASI="$dosya" bash "$OKU" "$Z/depo" 2>/dev/null || true)"
    kod=$(SON_YAYIN_DOSYASI="$dosya" bash "$OKU" "$Z/depo" >/dev/null 2>&1; echo $?)
    if [ "$kod" = "$bk" ] && { [ -z "$bm" ] || grep -qF "$bm" <<<"$cikti"; }; then
        echo "  ✓ $ad (çıkış $kod)"; gecti=$((gecti+1))
    else
        echo "  ✗ $ad — beklenen çıkış $bk metin '$bm', gelen çıkış $kod metin '$cikti'"; dustu=$((dustu+1))
    fi
}

git init -q "$Z/depo"
git -C "$Z/depo" config user.email s@s; git -C "$Z/depo" config user.name s
git -C "$Z/depo" commit -q --allow-empty -m taban
TABAN=$(git -C "$Z/depo" rev-parse HEAD)
printf 'Göçü getiren commit\n\nYIKICI-BEYAN: audit_logs\n' | git -C "$Z/depo" commit -q --allow-empty -F -
for i in 1 2 3; do git -C "$Z/depo" commit -q --allow-empty -m "araya giren $i"; done
echo "$TABAN" > "$Z/taban.txt"
echo "0000000000000000000000000000000000000000" > "$Z/kotu.txt"

echo "[sonda] yıkıcı beyan okuyucusu"
ayak "1 beyan geride, taban var → BULUR"        0 "audit_logs" "$Z/taban.txt"
ayak "2 taban dosyası yok → HEAD, beyan yok"    1 ""           "$Z/yok.txt"
ayak "3 taban atası değil → HEAD, beyan yok"    1 ""           "$Z/kotu.txt"

printf "HEAD'de beyan\n\nYIKICI-BEYAN: baska_tablo\n" | git -C "$Z/depo" commit -q --allow-empty -F -
ayak "4 beyan HEAD'de, taban yok → BULUR"       0 "baska_tablo" "$Z/yok.txt"
ayak "5 aralıkta iki beyan → İKİSİ DE"          0 "audit_logs"  "$Z/taban.txt"

echo "[sonda] geçti: $gecti · düştü: $dustu"
[ "$dustu" -eq 0 ]
