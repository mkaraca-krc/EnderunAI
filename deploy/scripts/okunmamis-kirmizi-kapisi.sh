#!/usr/bin/env bash
#
# OKUNMAMIŞ KIRMIZI KAPISI — BİR KIRMIZI EN GEÇ BİR SONRAKİ YAYINDA GÖRÜLÜR.
#
# ═══ NE YAPAR ═══
#
# `okunmamis-kirmizilar.txt` boş değilse YAYIN DURUR (çıkış 1) ve
# satırları basar. Defter yoksa ya da boşsa geçer (çıkış 0).
#
# ═══ NEDEN YAYINI DURDURUYOR ═══
#
# Kırmızıyı bir dosyaya yazmak, onu okunur YAPMAZ — 15 Eylül'de kırmızı
# zaten journald'daydı ve 15 saat görülmedi. Kimsenin bakmadığı bir
# dosya, kimsenin bakmadığı bir günlükten daha iyi değildir.
#
# Bu kapı üst sınırı koyar: yayın yapmak için kırmızıyı OKUMAK gerekir.
# Okumak = `kirmizi-okundu.sh` ile işaretlemek; ve o işaretleme bir
# insan kararıdır, sessiz bir temizlik değil.
#
# ═══ "OKUNDU" SİLMEK DEĞİLDİR ═══
#
# İşaretlenen satırlar SİLİNMİYOR, arşive taşınıyor
# (`okunan-kirmizilar.txt`). Denetim kaydından satır silmek kaydın
# kendisine güveni bozar; burada da aynı ilke.
#
set -uo pipefail

DEFTER="${KIRMIZI_DEFTERI:-/var/lib/enderun-ai/okunmamis-kirmizilar.txt}"

if [ ! -s "$DEFTER" ]; then
    echo "[okunmamis-kirmizi] GEÇTİ: okunmamış kırmızı yok (defter: $DEFTER)."
    exit 0
fi

adet="$(grep -c . "$DEFTER" 2>/dev/null || true)"
echo "[okunmamis-kirmizi] KIRMIZI: $adet okunmamış kırmızı var." >&2
echo "[okunmamis-kirmizi] Bir kapı yandı ve kimse bakmadı; yayın DURUYOR." >&2
while IFS= read -r satir; do
    [ -n "$satir" ] && echo "    $satir" >&2
done < "$DEFTER"
echo "[okunmamis-kirmizi] Okuyup karar verdikten SONRA:" >&2
echo "[okunmamis-kirmizi]   deploy/scripts/kirmizi-okundu.sh --hepsi" >&2
echo "[okunmamis-kirmizi] Satırlar silinmez, arşive taşınır." >&2
exit 1
