#!/usr/bin/env bash
#
# ETİKETSİZ HÜKÜM ÇIRASI — BİLGİ VERİR, KIRMIZI YAKMAZ
#
# Kural 88: her hüküm satırının yanında ya ölçüm yöntemi ya
# `[ÖLÇÜLMEDİ]` etiketi bulunur — kod yorumları dahil.
#
# ═══ NEDEN TOPLU DENETİM YOK ═══
#
# Ölçüldü (2026-09-13): belgelerde 1.756, kod yorumlarında 5.542
# etiketsiz hüküm. 6.168 yorumu tek seferde ölçmek hem imkânsız hem
# gereksiz. Beşi ölçüldü ve beşi de doğru çıktı — ama 6.168'in 5'i bir
# örneklem bile değildir ve ondan genelleme YAPILMAZ.
#
# UYGULAMA BİÇİMİ: bir dosyaya İŞ İÇİN dokunduğunda o dosyadaki hüküm
# cümlelerini etiketlersin. Temas ettiğin yeri temiz bırakırsın; borç
# kendiliğinden erir, ayrı bir proje olmaz.
#
# ═══ NEDEN KIRMIZI DEĞİL ═══
#
# Artış bir kusur değil, yeni yazılmış koddur. Kapının işi engellemek
# değil SAYIYI GÖRÜNÜR TUTMAK. Kırmızı yakan bir çıra, yeni kod yazmayı
# cezalandırır ve ilk haftada devre dışı bırakılır.
#
set -uo pipefail
KOK="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CIZGI="$KOK/bekci/etiketsiz-hukum-cizgi.txt"

[ -r "$CIZGI" ] || { echo "[etiketsiz-hüküm] ÖLÇEMEDİ: çizgi dosyası yok: $CIZGI"; exit 3; }

CIKTI="$(python3 "$KOK/scripts/kayit-taramasi.py" 2>/dev/null)" || {
    echo "[etiketsiz-hüküm] ÖLÇEMEDİ: tarayıcı koşmadı."; exit 3; }

# İki "İDDİA" satırı: birincisi belge, ikincisi kod yorumu.
# HER SATIRIN İLK SAYISI alınır. `grep -oE '[0-9]+' | head -2` YANLIŞTI:
# aynı satırdaki yüzdeyi (%59) ikinci küme sanıp "5483 azaldı" dedi
# (2026-09-13, kendi provamda yakalandı — aletin yanlış yeşili).
mapfile -t SAYILAR < <(printf '%s\n' "$CIKTI" | grep -E '^\s+İDDİA' \
    | sed -E 's/.*İDDİA[^0-9]*([0-9]+).*/\1/' | head -2)
[ "${#SAYILAR[@]}" -eq 2 ] || { echo "[etiketsiz-hüküm] ÖLÇEMEDİ: sayı okunamadı."; exit 3; }

belge_cizgi="$(awk '$1=="belge"{print $2}' "$CIZGI")"
kod_cizgi="$(awk '$1=="kod"{print $2}' "$CIZGI")"
[ -n "$belge_cizgi" ] && [ -n "$kod_cizgi" ] || { echo "[etiketsiz-hüküm] ÖLÇEMEDİ: çizgi okunamadı."; exit 3; }

bildir() {
    local ad="$1" simdi="$2" cizgi="$3"
    local fark=$(( simdi - cizgi ))
    if [ "$fark" -lt 0 ]; then
        echo "[etiketsiz-hüküm] $ad: $simdi (çizgi $cizgi) — ${fark#-} AZALDI. Çizgiyi indirin."
    elif [ "$fark" -gt 0 ]; then
        echo "[etiketsiz-hüküm] $ad: $simdi (çizgi $cizgi) — +$fark. BİLGİ: dokunduğunuz dosyalarda etiketleyin (Kural 88)."
    else
        echo "[etiketsiz-hüküm] $ad: $simdi (çizgi $cizgi) — değişmedi."
    fi
}

bildir "belge      " "${SAYILAR[0]}" "$belge_cizgi"
bildir "kod yorumu " "${SAYILAR[1]}" "$kod_cizgi"
exit 0
