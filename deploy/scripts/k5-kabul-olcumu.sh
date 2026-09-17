#!/usr/bin/env bash
#
# K5 KABUL ÖLÇÜMÜ — PAKETTEN, KAYNAKTAN DEĞİL.
#
# ═══ NEDEN PAKETTEN ═══
#
# Muhafızım (`QuickGridTekTanimTests`) KAYNAK dosyayı okuyor. Mehmet Bey
# PAKETİ okudu ve haklı çıktı: kaynak düzeltilmişti ama dağıtım
# inmemişti, yani kullanıcının gördüğü katmanda kusur duruyordu.
# *Kaynağı düzeltip paketi ölçmeyen kapı, kullanıcının gördüğü katmanı
# ölçmüyor* (Kural 91).
#
# ═══ "TAŞMA 0" AYIRT EDİCİ DEĞİLDİR (2026-09-17, ölçüldü) ═══
#
# Mehmet Bey canlıyı 2560 px'te ölçtü: taşma 0 çıktı — AMA CSS hâlâ
# düzeltilmemişti. Sebep: geniş pencerede `aside` büyüyor, kutu 351
# px'ten 678 px'e çıkıyor ve dört sütun sığıyor.
#
# Yani DÜZELTİLMEMİŞ sürüm de geniş pencerede 0 verir. "Taşma 0" tek
# başına delil değildir; kusuru GİZLEYEN bir koşulda ölçmektir.
#
# Bu betik o yüzden yalnız AYIRT EDİCİ iki ölçütü ölçer ve üçüncüyü
# paketten TÜRETİR. Tarayıcı ölçümü (sütun sayısı + taşma, 1280 ve
# 1536'da) ayrı iştir ve genişliği YAZILMADAN rapora girmez.
#
set -uo pipefail
ADRES="${K5_ADRES:-https://enderunai.com.tr}"
ESKI_KARMA="${K5_ESKI_KARMA:-0d-p5jtxagtp5}"

log() { echo "[k5-kabul] $*"; }
HATA=0

# ── 1) CSS dosya karması ─────────────────────────────────────────────
sayfa="$(curl -sL --max-time 20 "${ADRES}/login")"
css_yol="$(printf '%s' "$sayfa" | grep -oE '/_next/static/[^"'"'"'\\]+\.css' | head -1)"

if [ -z "$css_yol" ]; then
    log "ÖLÇEMEDİ: sayfada CSS bağlantısı bulunamadı. Bu bir onay DEĞİLDİR."
    exit 3
fi
css_ad="$(basename "$css_yol")"
log "yayındaki CSS: ${css_ad}"

if printf '%s' "$css_ad" | grep -q "$ESKI_KARMA"; then
    log "KIRMIZI [1/3]: karma DEĞİŞMEMİŞ (${ESKI_KARMA}). Dağıtım CSS'e dokunmamış."
    log "KIRMIZI [1/3]: 'derleme yeşildi' bunu göstermez."
    HATA=1
else
    log "GEÇTİ  [1/3]: karma değişmiş (eski: ${ESKI_KARMA})."
fi

# ── 2) Paket içeriği ─────────────────────────────────────────────────
css="$(curl -sL --max-time 20 "${ADRES}${css_yol}")"
if [ -z "$css" ]; then
    log "ÖLÇEMEDİ: CSS indirilemedi."
    exit 3
fi
log "CSS boyutu: $(printf '%s' "$css" | wc -c) bayt"

# ── TABAN TANIM: @media BLOKLARI AYIKLANARAK ─────────────────────────
#
# İLK YAZIMIMDA KUSUR VARDI (2026-09-17): `grep ... | head -1`
# küçültülmüş CSS'te İLK eşleşmeyi alıyordu ve o, medya sorgusu
# içindeki `.erp-quick-grid{grid-template-columns:1fr}` kuralıydı —
# taban tanım değil. Ölçüt bu yüzden YANLIŞ SEBEPLE geçti.
#
# Aynı ayrımı kaynak muhafızında (`QuickGridTekTanimTests`) yapmıştım,
# burada yapmamıştım. Küçültülmüş CSS'te süslü parantez sayarak medya
# blokları ayıklanıyor.
taban="$(printf '%s' "$css" | python3 -c '
import sys, re
css = sys.stdin.read()
# @media bloklarini cikar (ic ice suslu parantezleri sayarak)
out, i = [], 0
while i < len(css):
    m = css.find("@media", i)
    if m == -1:
        out.append(css[i:]); break
    out.append(css[i:m])
    j = css.find("{", m)
    if j == -1: break
    depth, j = 1, j + 1
    while j < len(css) and depth:
        if css[j] == "{": depth += 1
        elif css[j] == "}": depth -= 1
        j += 1
    i = j
taban_css = "".join(out)
m = re.search(r"\.erp-quick-grid\{[^}]*\}", taban_css)
print(m.group(0) if m else "")
')"
if [ -z "$taban" ]; then
    log "ÖLÇEMEDİ: pakette .erp-quick-grid taban tanımı bulunamadı."
    exit 3
fi
log "taban tanım: ${taban}"

if printf '%s' "$taban" | grep -qE 'repeat\(4, ?1fr\)'; then
    log "KIRMIZI [2/3]: taban tanımda repeat(4,1fr) VAR."
    HATA=1
elif printf '%s' "$taban" | grep -qE 'repeat\([0-9]+, ?1fr\)'; then
    log "KIRMIZI [2/3]: taban tanımda ÇIPLAK 1fr var (minmax(0,...) değil)."
    log "KIRMIZI [2/3]: 1fr = minmax(auto,1fr); sütun içeriğin altına inmez."
    HATA=1
else
    log "GEÇTİ  [2/3]: repeat(4,1fr) yok, çıplak 1fr yok."
fi

# ── 3) Sütun sayısı — PAKETTEN TÜRETİLİYOR ───────────────────────────
#
# Tarayıcı ölçümü değil, TÜRETME: taban tanımdaki sütun sayısı okunuyor.
# Zayıf olduğu yer açıkça yazılıyor — gerçek sütun sayısı ancak
# tarayıcıda, belirli bir genişlikte ölçülür.
sutun="$(printf '%s' "$taban" | grep -oE 'repeat\([0-9]+' | grep -oE '[0-9]+' | head -1)"
if [ "${sutun:-0}" = "2" ]; then
    log "GEÇTİ  [3/3]: taban tanım 2 sütun (türetildi, tarayıcı ölçümü değil)."
else
    log "KIRMIZI [3/3]: taban tanım ${sutun:-?} sütun — 2 bekleniyor."
    HATA=1
fi

echo
if [ "$HATA" -eq 0 ]; then
    log "SONUÇ: ÜÇ AYIRT EDİCİ ÖLÇÜT GEÇTİ."
else
    log "SONUÇ: KIRMIZI — yukarıdaki maddelere bakın."
fi
log "EKSİK OLAN: tarayıcıda sütun sayısı ve taşma, 1280 ve 1536'da."
log "EKSİK OLAN: o ölçüm GENİŞLİK YAZILMADAN rapora girmez (2026-09-17 dersi)."
exit "$HATA"
