#!/usr/bin/env bash
#
# ═══════════════════════════════════════════════════════════════════
# SORGU DİZGESİ ÇIRASI — YENİ PARAMETRE ADI RAPORLANIR
# ═══════════════════════════════════════════════════════════════════
#
# ═══ NEDEN VAR ═══
#
# 2026-09-13'te bir kaydımız yanlış çıktı: *"sorgu dizgeleri günlüğe
# yazılmıyor"*. Yazılıyormuş — `log_format` alanı `$request_uri` ve o
# sorgu dizgesini içeriyor. O gün ölçüldü: hassas parametre adı 0.
#
# Ama "bugün 0" yarın için hiçbir şey söylemez. Bir uç eklenir,
# parametre sorgu dizgesine konur ve jeton günlüğe düşer — kimse fark
# etmez. Bu çıra o günü yakalamak için var.
#
# ═══ NEDEN KARA LİSTE DEĞİL, DIŞLAMA ═══
#
# "token|password|secret ara" ancak AKLA GELEN adı bulur. Yarın
# `oturumAnahtari` diye bir ad gelirse hiçbir kara liste yakalamaz.
# Burada tersi: bilinen zararsızlar listelenir, LİSTEDE OLMAYAN HER
# YENİ AD raporlanır.
#
# ═══ KAPSAM AÇIKÇA (Kural 84) ═══
#
#   KIRMIZI kovası : `/api/` ile başlayan yollar — BİZİM uçlarımız.
#   BİLGİ kovası   : geri kalan yollar. Saldırı taramaları uydurma
#                    parametrelerle geliyor (`phpinfo`, `cmd`,
#                    `rest_route`); onlar bizim yüzeyimiz değil ve
#                    onları biz üretmiyoruz. Sayılır, kırmızı yakmaz.
#
#   SIR ÇAĞRIŞTIRAN AD: KIRMIZI olması için isteğin BİZİM TARAFIMIZDAN
#   SUNULMUŞ olması gerekir (2xx). Ölçüldü (2026-09-13): böyle tek bir
#   parametre vardı (`passwd`) ve **404** almıştı — yani uydurma bir
#   yola gelen saldırı yoklamasıydı, bizim ucumuz değil.
#
#   Neden 404'e kırmızı yakmıyoruz: saldırganın ne gönderdiğini biz
#   belirlemiyoruz. İlk gün gürültüyle kırmızı yanan bir kapı, ertesi
#   gün kimsenin bakmadığı kapıdır. Sunulmayan istek bizim yüzeyimiz
#   değildir; BİLGİ kovasında adıyla sayılır.
#
# ═══ POZİTİF KONTROL — BOŞ KÜME KANIT DEĞİL (Kural 48) ═══
#
# Çıra, bilinen bir parametreyi (`companyId`) bulamazsa ÖLÇEMEDİ der.
# 2026-09-13 ölçümü: companyId 994 kez. Aramanın kör olmadığının kanıtı
# çıktıda her koşuda basılır.
#
# ÇIKIŞ: 0 temiz · 1 yeni ad ya da sır adı · 3 ÖLÇEMEDİ
#
set -uo pipefail

KOK="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LISTE="$KOK/bekci/sorgu-parametre-beyaz-liste.txt"
GUNLUK_DIZINI="${SORGU_CIRA_GUNLUK:-/var/log/nginx}"

SIR_DESENI='(token|jeton|password|parola|passwd|secret|sir|apikey|api_key|auth|credential|pwd|key)$'

[ -r "$LISTE" ] || { echo "[sorgu-çıra] ÖLÇEMEDİ: beyaz liste okunamadı: $LISTE" >&2; exit 3; }

BEYAZ="$(grep -oE '^[A-Za-z_][A-Za-z0-9_.-]*' "$LISTE" | sort -u)"
[ -n "$BEYAZ" ] || { echo "[sorgu-çıra] ÖLÇEMEDİ: beyaz liste boş — hiçbir şey sınanmaz." >&2; exit 3; }

gunlukler() {
    cat "$GUNLUK_DIZINI"/access.log "$GUNLUK_DIZINI"/access.log.1 2>/dev/null
    zcat "$GUNLUK_DIZINI"/access.log.*.gz 2>/dev/null
}

DOSYA_SAYISI=$(ls "$GUNLUK_DIZINI"/access.log* 2>/dev/null | wc -l)
SATIR_SAYISI=$(gunlukler | wc -l)

adlar() {  # $1: yol süzgeci (grep -E deseni)
    gunlukler \
      | grep -oE "\"[A-Z]+ $1[^\"]*\?[^\"]*\"" \
      | sed -E 's/ HTTP[^"]*//' \
      | grep -oE '\?.*' | tr '?&' '\n\n' | sed -E 's/=.*//' \
      | grep -E '^[A-Za-z_][A-Za-z0-9_.-]*$' | sort -u
}

API_ADLAR="$(adlar '/api/')"
DIGER_ADLAR="$(adlar '/' | comm -23 - <(printf '%s\n' "$API_ADLAR"))"

echo "[sorgu-çıra] kapsam: $DOSYA_SAYISI günlük dosyası, $SATIR_SAYISI satır · beyaz liste $(printf '%s\n' "$BEYAZ" | wc -l) ad"

# ── POZİTİF KONTROL ─────────────────────────────────────────────────
if ! printf '%s\n' "$API_ADLAR" | grep -qx "companyId"; then
    echo "[sorgu-çıra] ÖLÇEMEDİ: pozitif kontrol düştü — bilinen parametre 'companyId' bulunamadı." >&2
    echo "[sorgu-çıra] Arama kör olabilir; boş sonuç kanıt sayılmaz (Kural 48)." >&2
    exit 3
fi
echo "[sorgu-çıra] pozitif kontrol GEÇTİ: 'companyId' bulundu ($(gunlukler | grep -cE '[?&]companyId=') kez)"

KOD=0

# ── KOVA 1: BİZİM UÇLAR — LİSTEDE OLMAYAN AD KIRMIZI ────────────────
YENI="$(printf '%s\n' "$API_ADLAR" | comm -23 - <(printf '%s\n' "$BEYAZ"))"
if [ -n "$YENI" ]; then
    echo "[sorgu-çıra] KIRMIZI — /api/ uçlarında BEYAZ LİSTEDE OLMAYAN parametre:"
    printf '%s\n' "$YENI" | sed 's/^/    /'
    echo "    Meşruysa gerekçesiyle $LISTE dosyasına ekleyin."
    KOD=1
else
    echo "[sorgu-çıra] /api/ uçlarında liste dışı parametre YOK ($(printf '%s\n' "$API_ADLAR" | wc -l) ad tarandı)"
fi

# ── HER YERDE: SIR ÇAĞRIŞTIRAN AD KOŞULSUZ KIRMIZI ──────────────────
# SUNULAN (2xx) isteklerde sır çağrıştıran ad → KIRMIZI
SUNULAN_SIRLI="$(gunlukler \
    | grep -oE "\"[A-Z]+ [^\"]*\?[^\"]*\" 2[0-9][0-9]" \
    | sed -E 's/ HTTP[^"]*//' \
    | grep -oE '\?[^"]*' | sed -E 's/" 2[0-9][0-9]$//' \
    | tr '?&' '\n\n' | sed -E 's/=.*//' \
    | grep -E '^[A-Za-z_][A-Za-z0-9_.-]*$' | sort -u \
    | grep -EI "$SIR_DESENI" || true)"

if [ -n "$SUNULAN_SIRLI" ]; then
    echo "[sorgu-çıra] KIRMIZI — SUNULAN (2xx) bir istekte sır çağrıştıran parametre:"
    printf '%s\n' "$SUNULAN_SIRLI" | sed 's/^/    /'
    echo "    Bu bizim ucumuz: DERHAL gövdeye ya da Authorization başlığına taşınmalı."
    KOD=1
else
    echo "[sorgu-çıra] sunulan isteklerde sır çağrıştıran parametre adı YOK"
fi

# Sunulmayan (404/3xx) isteklerdeki sırlı adlar: BİLGİ, kırmızı değil
# `comm` KULLANILMIYOR: girdilerin sıralı olması şart ve boş küme
# verildiğinde başlığı basıp içeriği yutuyordu — bilgi satırının kendisi
# yanıltıcı oluyordu (2026-09-13, kendi provamda yakalandı).
SUNULMAYAN_SIRLI="$(printf '%s\n%s\n' "$API_ADLAR" "$DIGER_ADLAR" \
    | grep -EI "$SIR_DESENI" | sort -u \
    | { if [ -n "$SUNULAN_SIRLI" ]; then grep -vxF "$SUNULAN_SIRLI"; else cat; fi; } || true)"
if [ -n "$SUNULMAYAN_SIRLI" ]; then
    echo "[sorgu-çıra] BİLGİ: sunulmayan isteklerde sırlı ad (saldırı yoklaması):"
    printf '%s\n' "$SUNULMAYAN_SIRLI" | sed 's/^/    /'
fi

# ── KOVA 2: BİLGİ ───────────────────────────────────────────────────
DIS="$(printf '%s\n' "$DIGER_ADLAR" | comm -23 - <(printf '%s\n' "$BEYAZ") | grep -c . || true)"
echo "[sorgu-çıra] BİLGİ: /api/ dışı yollarda liste dışı $DIS ad (saldırı taraması beklenir, kırmızı yakmaz)"

exit $KOD
