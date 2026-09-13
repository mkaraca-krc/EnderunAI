#!/usr/bin/env bash
#
# ═══════════════════════════════════════════════════════════════════
# IP'SİZ GÜNLÜK ÖZET — SÜRESİZ SAKLANIR
# ═══════════════════════════════════════════════════════════════════
#
# ═══ NEDEN VAR ═══
#
# Erişim günlüğü kişisel veri (IP) taşıdığı için süresiz saklanamaz.
# Ama günlüğün asıl işi olay incelemesi ve DÜZELTME ETKİSİNİN
# DOĞRULANMASI; ikisi de kişisel veri gerektirmiyor.
#
# ÖLÇÜLDÜ (2026-09-13): GİRİŞ-DÖNGÜ/1'in düzeltildiği, kodu okuyarak
# değil 08 Eylül'ün 401 sayısının 64.531'den 113'e düşmesiyle
# doğrulandı. O doğrulama ancak günlük DURUYORSA yapılabiliyor —
# 15 günlük pencere, 16 gün önceki bir düzeltmeyi doğrulanamaz yapar.
#
# Bu betik o doğrulamayı mümkün kılan ASGARİ veriyi ayırıyor:
#
#     {tarih, uç kalıbı, durum kodu, adet}
#
# IP YOK · sorgu dizgesi YOK · kullanıcı YOK · referer YOK · ajan YOK.
# Kalan veri tek bir kişiye bağlanamaz; bu yüzden süresiz saklanır.
#
# ═══ UÇ KALIBI — NEDEN HAM YOL DEĞİL ═══
#
# Ham yol da tanımlayıcı taşır (`/projeler/<uuid>`), üstelik binlerce
# ayrı satıra dağılır ve sayım işe yaramaz. Kalıp, değişken parçaları
# yerine koyar: UUID → `:id`, sayı → `:n`, uzun belirteç → `:tkn`.
#
# ═══ ÇAĞRI ═══
#
#   gunluk-ozet.sh                 # dün (access.log.1) → arşive ekler
#   gunluk-ozet.sh --dosya <yol>   # verilen dosyayı özetler, ekranа basar
#
set -uo pipefail

ARSIV="/var/lib/enderun-ai/gunluk-ozet"
KAYNAK="/var/log/nginx/access.log.1"
EKRANA=0

while [ $# -gt 0 ]; do
    case "$1" in
        --dosya) KAYNAK="${2:?--dosya için yol gerekli}"; EKRANA=1; shift 2 ;;
        *) echo "[gunluk-ozet] bilinmeyen seçenek: $1" >&2; exit 64 ;;
    esac
done

if [ ! -r "$KAYNAK" ]; then
    echo "[gunluk-ozet] ÖLÇEMEDİ: kaynak okunamadı: $KAYNAK" >&2
    exit 3
fi

ozet() {
    awk '
    {
        # [13/Sep/2026:18:54:44 → 2026-09-13
        for (i = 1; i <= NF; i++) {
            if ($i ~ /^\[[0-9]{2}\//) { damga = substr($i, 2); break }
        }
        split(damga, d, ":"); split(d[1], t, "/")
        ay["Jan"]="01"; ay["Feb"]="02"; ay["Mar"]="03"; ay["Apr"]="04"
        ay["May"]="05"; ay["Jun"]="06"; ay["Jul"]="07"; ay["Aug"]="08"
        ay["Sep"]="09"; ay["Oct"]="10"; ay["Nov"]="11"; ay["Dec"]="12"
        tarih = t[3] "-" ay[t[2]] "-" t[1]

        # "GET /yol?sorgu HTTP/1.1" → yöntem + yol (sorgu ATILIR)
        satir = $0
        if (match(satir, /"[A-Z]+ [^"]*"/) == 0) next
        istek = substr(satir, RSTART + 1, RLENGTH - 2)
        split(istek, p, " ")
        yontem = p[1]; yol = p[2]
        sub(/\?.*$/, "", yol)

        # durum kodu: istek satırından SONRAKİ ilk üç haneli sayı
        kalan = substr(satir, RSTART + RLENGTH)
        durum = "???"
        if (match(kalan, /[0-9][0-9][0-9]/)) durum = substr(kalan, RSTART, 3)

        anahtar = tarih "|" yontem " " yol "|" durum
        say[anahtar]++
    }
    END { for (a in say) print a "|" say[a] }
    ' "$KAYNAK" |
    # KALIPLAŞTIRMA: değişken parçalar yerine konur.
    sed -E '
        s#/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}#/:id#g
        s#/[A-Za-z0-9_-]{24,}#/:tkn#g
        s#/[0-9]+#/:n#g
    ' |
    # Kalıplaştırma sonrası aynı anahtarlar birleşir; yeniden toplanır.
    awk -F'|' '{ s[$1 "|" $2 "|" $3] += $4 } END { for (a in s) print a "|" s[a] }' |
    sort -t'|' -k1,1 -k4,4nr
}

if [ "$EKRANA" = "1" ]; then
    printf '# tarih|uç kalıbı|durum|adet\n'
    ozet
    exit 0
fi

mkdir -p "$ARSIV"
GUN="$(ozet | head -1 | cut -d'|' -f1)"
if [ -z "$GUN" ]; then
    echo "[gunluk-ozet] ÖLÇEMEDİ: kaynakta satır yok: $KAYNAK" >&2
    exit 3
fi

HEDEF="$ARSIV/${GUN}.txt"
{
    printf '# IP YOK · SORGU DİZGESİ YOK · KULLANICI YOK — süresiz saklanır\n'
    printf '# kaynak: %s\n' "$(basename "$KAYNAK")"
    printf '# üretildi: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    printf '# tarih|uç kalıbı|durum|adet\n'
    ozet
} > "$HEDEF"
chmod 644 "$HEDEF"

echo "[gunluk-ozet] $GUN → $HEDEF ($(grep -vc '^#' "$HEDEF") satır, $(du -h "$HEDEF" | cut -f1))"
