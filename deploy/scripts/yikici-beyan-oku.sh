#!/usr/bin/env bash
#
# YIKICI BEYANI OKU — TEK KAYNAK, SINANABİLİR.
#
# ═══ NEDEN AYRI BETİK ═══
#
# Bu mantık `goc-provasi.sh`in içindeydi ve oraya ancak BEKLEYEN YIKICI
# GÖÇ varken ulaşılıyordu — yani sondalanması için sahte göç üretmek
# gerekiyordu. Sonda bunun yerine mantığın bir KOPYASINI sınıyordu ve
# kopya ayrışmıştı (`diff` yakaladı). İki kopya, zamanla iki davranış
# demektir.
#
# ═══ NE YAPAR ═══
#
# Beyanı PAKET ARALIĞINDA arar (son yayın..HEAD), bulamazsa HEAD'e düşer
# ve hangisine baktığını AÇIKÇA söyler.
#
# ÖLÇÜLEN KUSUR (2026-09-15 yayını): beyan göçü getiren commit'te
# yazılıydı ama 20 adım geride kaldı; yalnız HEAD'e bakan kapı geçerli
# bir beyanı GÖRMEDİ ve yayını durdurdu. Bir yayın bir commit değil,
# bir ARALIK taşır.
#
# ÇIKIŞ: 0 beyan bulundu (stdout'a yazılır) · 1 beyan yok
#
set -uo pipefail
REPO_ROOT="${1:?depo kökü gerekli}"
SON_YAYIN_DOSYASI="${SON_YAYIN_DOSYASI:-/var/lib/enderun-ai/last-deployed-commit}"

# Dosya YOKSA kabuk kendi hatasını basar ve çıktıyı kirletir; önce
# okunabilirlik sınanıyor (yokluk bir arıza değil, geri düşme sebebidir).
taban=""
[ -r "$SON_YAYIN_DOSYASI" ] && taban="$(tr -d '[:space:]' < "$SON_YAYIN_DOSYASI" 2>/dev/null || true)"

if [ -n "$taban" ] && git -C "$REPO_ROOT" merge-base --is-ancestor "$taban" HEAD 2>/dev/null; then
    kaynak="paket aralığı ${taban:0:8}..HEAD"
    ham="$(git -C "$REPO_ROOT" log --format=%B "${taban}..HEAD" 2>/dev/null || true)"
else
    kaynak="YALNIZ HEAD (son yayın kaydı yok ya da HEAD'in atası değil)"
    ham="$(git -C "$REPO_ROOT" log --format=%B -1 HEAD 2>/dev/null || true)"
fi

beyan="$(printf '%s\n' "$ham" | grep -E '^YIKICI-BEYAN:' | sed 's/^YIKICI-BEYAN:[[:space:]]*//' || true)"

printf 'kaynak: %s\n' "$kaynak" >&2
[ -n "$beyan" ] || exit 1
printf '%s\n' "$beyan"
