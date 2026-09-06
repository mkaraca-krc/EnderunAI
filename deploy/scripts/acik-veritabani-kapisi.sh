#!/usr/bin/env bash
#
# AÇIK VERİTABANI KAPISI — PUBLIC'E AÇIK BİR DB SESSİZCE DOĞMASIN.
#
# ═══ NEDEN VAR (2026-09-06) ═══
#
# PostgreSQL'de yeni bir veritabanı, ACL'i `NULL` olarak doğar ve
# `NULL` ACL "yerleşik varsayılan" demektir: PUBLIC'e CONNECT + TEMP.
# `template1`'den geri almak bunu DEĞİŞTİRMİYOR — ölçüldü: şablonun
# ACL'i yeni veritabanına kopyalanmıyor. `ALTER DEFAULT PRIVILEGES` de
# veritabanı nesnesini kapsamıyor.
#
# Yani PostgreSQL'de "yeni veritabanı kapalı doğsun" diye bir ayar YOK.
# Geriye iki şey kalıyor:
#   A) KURAN KAPATIR — DB oluşturan her yol, hemen ardından
#      `REVOKE CONNECT ... FROM PUBLIC` çalıştırır.
#   B) BU KAPI — A'nın unutulduğu günü yakalar.
#
# A tek başına yeterli değildir çünkü unutulabilir; B onsuz da
# yeterli değildir çünkü ancak sonradan yakalar. İkisi birlikte.
#
# KULLANIM:  acik-veritabani-kapisi.sh

set -uo pipefail

BEYAZ_LISTE="${BEYAZ_LISTE:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../bekci" && pwd)/acik-veritabanlari-beyaz-liste.txt}"

hata() { echo "[acik-db] $*" >&2; }

[ -r "$BEYAZ_LISTE" ] || { hata "HATA: beyaz liste okunamadı: $BEYAZ_LISTE"; exit 1; }

# ── BEYAZ LİSTEYİ OKU — GEREKÇESİZ SATIR KABUL EDİLMEZ ──
declare -A IZINLI=()
satir_no=0
while IFS= read -r satir; do
    satir_no=$((satir_no + 1))
    case "$satir" in ''|'#'*) continue ;; esac

    # BOŞLUK KIRPMA: `xargs` KULLANILMIYOR. Gerekçe metni Türkçe ve
    # içinde kesme işareti geçiyor ("PostgreSQL'in"); xargs tırnağı
    # sözdizimi sanıp uyarı basıyor ve metni bozabiliyor.
    kirp() { sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//'; }
    ad="$(printf '%s' "$satir"       | cut -d'|' -f1  | kirp)"
    kategori="$(printf '%s' "$satir" | cut -d'|' -f2  | kirp)"
    gerekce="$(printf '%s' "$satir"  | cut -d'|' -f3- | kirp)"

    if [ -z "$ad" ] || [ -z "$kategori" ] || [ -z "$gerekce" ]; then
        hata "HATA: beyaz liste satır ${satir_no} eksik — biçim: <ad> | <kategori> | <gerekçe>"
        hata "      Gerekçesiz muafiyet kabul edilmez."
        exit 1
    fi
    IZINLI["$ad"]="$kategori"
done < "$BEYAZ_LISTE"

# ── CANLI DURUM ──
SORGU="select datname, has_database_privilege('public', datname, 'CONNECT')
       from pg_database where datallowconn order by 1;"
CIKTI="$(sudo -u postgres psql -tA -F'|' -c "$SORGU" 2>/dev/null)"

TARANAN=0; ACIK=0; KACAK=0
declare -A GORULEN_ACIK=()

while IFS='|' read -r ad acik_mi; do
    [ -n "$ad" ] || continue
    TARANAN=$((TARANAN + 1))
    [ "$acik_mi" = "t" ] || continue
    ACIK=$((ACIK + 1))
    GORULEN_ACIK["$ad"]=1
    if [ -z "${IZINLI[$ad]:-}" ]; then
        hata "KAÇAK: '${ad}' PUBLIC'e CONNECT açık ve beyaz listede DEĞİL."
        KACAK=$((KACAK + 1))
    fi
done <<< "$CIKTI"

# ── TARAMA SAĞLIĞI — SIFIR TARAMA YEŞİL VERMEZ ──
#
# "Hiç açık DB yok" ile "hiç DB görülmedi" aynı çıktıyı verir; ayıran
# tek şey sayımdır (Kural 48). psql düşerse CIKTI boş gelir ve döngü
# hiç dönmez; o hâlde kapı sessizce yeşil verirdi.
if [ "$TARANAN" -eq 0 ]; then
    hata "HATA: hiçbir veritabanı taranamadı — psql çalışmıyor olabilir."
    hata "      Boş küme, yokluğun kanıtı değildir; kapı yeşil vermiyor."
    exit 1
fi

# ── ÇİFT YÖNLÜ: ÇÜRÜMÜŞ MUAFİYET DE DÜŞÜRÜR ──
CURUYEN=0
for ad in "${!IZINLI[@]}"; do
    if [ -z "${GORULEN_ACIK[$ad]:-}" ]; then
        hata "ÇÜRÜMÜŞ MUAFİYET: '${ad}' beyaz listede ama artık PUBLIC'e açık değil"
        hata "                  (ya da veritabanı yok). Satırı kaldırın."
        CURUYEN=$((CURUYEN + 1))
    fi
done

echo "[acik-db] taranan ${TARANAN} · PUBLIC'e açık ${ACIK} · beyaz listede ${#IZINLI[@]} · kaçak ${KACAK} · çürümüş ${CURUYEN}"

if [ "$KACAK" -gt 0 ] || [ "$CURUYEN" -gt 0 ]; then
    hata "Kapı DÜŞTÜ. Kaçak varsa: veritabanını kapatın"
    hata "  (revoke connect on database <ad> from public)"
    hata "ya da gerekçesiyle beyaz listeye yazın."
    exit 1
fi
