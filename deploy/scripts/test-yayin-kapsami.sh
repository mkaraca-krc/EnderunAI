#!/usr/bin/env bash
#
# YAYIN KAPSAMI KAPISI — SONDA.
#
# Kapı, en az bir kez KIRMIZI yanmadan var sayılmaz. Bu sonda kapıyı
# gerçek bir git deposunda (geçici, çalışma ağacının DIŞINDA) koşturur;
# beklenen sonuç her ayakta ÖNCEDEN yazılıdır (Kural 61).
#
# Depo: taban → J1 (JETON/1) → X (İLGİSİZ) → J2 (JETON/1)
#       ayrıca taban → J1 → J2' (X'siz temiz dal)
#
#   (d) SABOTAJ: JETON/1 paketine ilgisiz X sokulmuş, ilan yalnız J1 J2
#       → KIRMIZI (çıkış 1), çıktıda X "KAPSAM DIŞI"
#   (e) POZİTİF KONTROL: paket yalnız J1 J2', ilan J1 J2' → YEŞİL (0)
#   ilan yok                     → KIRMIZI (1)
#   ilanda pakette olmayan commit → KIRMIZI (1)
#   bozuk ilan                   → KIRMIZI (1)
#   boş paket (taban = uç)       → ÖLÇEMEDİ (2)
#   taban uç'un atası değil      → ÖLÇEMEDİ (2)
#   taban depoda yok             → ÖLÇEMEDİ (2)
#   birleştirme commit'i ilansız → KIRMIZI (1)
#
# SAFE-DEPLOY BAĞLANTISI (yayın betiği YÜKLENİR, `main` koşmaz; `fail` ve
# günlük alt kabukta zararsız sürümle ezilir — gerçek yayın durum
# dosyalarına dokunulmaz):
#   ilan yok              → yayın DURUR
#   ilgisiz commit        → yayın DURUR
#   ilanla birebir        → yayın GEÇER
#   sıra: main() kapıyı `git pull`dan sonra, göç kapısından ÖNCE çağırıyor
#
# TARAMA SAĞLIĞI: her ayak, kapının "karşılaştırılan commit: N" satırını
# da denetler — sayı yanlışsa ayak düşer; doğru çıkış kodu, yanlış
# sayılmış bir paketle geçemez.
#
# Kullanım: deploy/scripts/test-yayin-kapsami.sh

set -uo pipefail

KAPI="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/yayin-kapsami.sh"
DEPO="$(mktemp -d)"
trap 'rm -rf "$DEPO"' EXIT

GECTI=0
DUSTU=0

git -C "$DEPO" init -q -b ana
git -C "$DEPO" config user.email sonda@example.invalid
git -C "$DEPO" config user.name sonda
yap() { echo "$1" > "$DEPO/$1.txt"; git -C "$DEPO" add -A; git -C "$DEPO" commit -q -m "$2"; git -C "$DEPO" rev-parse HEAD; }

TABAN="$(yap taban 'taban: son yayın')"
J1="$(yap j1 'JETON/1: arka uç tek kaynak')"
X="$(yap x 'İLGİSİZ: başka bir iş')"
J2="$(yap j2 'JETON/1: jetondan izin talepleri çıkarıldı')"
git -C "$DEPO" checkout -q -b temiz "$J1"
J2T="$(yap j2t 'JETON/1: jetondan izin talepleri çıkarıldı (temiz dal)')"
git -C "$DEPO" checkout -q -b birlesme "$TABAN"
Y="$(yap y 'yan dal')"
git -C "$DEPO" merge -q --no-ff -m 'birleştirme' "$J1" >/dev/null
MERGE="$(git -C "$DEPO" rev-parse HEAD)"
git -C "$DEPO" checkout -q ana

ayak() {
    local ad="$1" beklenen_kod="$2" beklenen_sayi="$3" beklenen_metin="$4"; shift 4
    local cikti kod
    cikti="$(cd "$DEPO" && bash "$KAPI" "$@" 2>&1)"; kod=$?
    local sorun=""
    [ "$kod" = "$beklenen_kod" ] || sorun="çıkış ${kod} (beklenen ${beklenen_kod})"
    if [ -n "$beklenen_sayi" ] && ! printf '%s' "$cikti" | grep -q "karşılaştırılan commit: ${beklenen_sayi} "; then
        sorun="${sorun:+$sorun; }sayaç 'karşılaştırılan commit: ${beklenen_sayi}' yok"
    fi
    if [ -n "$beklenen_metin" ] && ! printf '%s' "$cikti" | grep -qF "$beklenen_metin"; then
        sorun="${sorun:+$sorun; }çıktıda '${beklenen_metin}' yok"
    fi
    if [ -z "$sorun" ]; then
        GECTI=$((GECTI + 1)); printf '  ✓ %s\n' "$ad"
    else
        DUSTU=$((DUSTU + 1)); printf '  ✗ %s — %s\n%s\n' "$ad" "$sorun" "$(printf '%s' "$cikti" | sed 's/^/      /')"
    fi
}

echo "[sonda] yayın kapsamı kapısı"
ayak "(d) SABOTAJ: ilgisiz commit pakete sokuldu → KIRMIZI" 1 3 "KAPSAM DIŞI: ${X:0:7}" \
    --taban "$TABAN" --uc "$J2" --kapsam "$J1 $J2"
ayak "(e) POZİTİF: yalnız JETON/1 commit'leri → YEŞİL" 0 2 "YEŞİL" \
    --taban "$TABAN" --uc "$J2T" --kapsam "$J1 $J2T"
ayak "(e') kısa kimlik ve virgülle ilan → YEŞİL" 0 2 "YEŞİL" \
    --taban "$TABAN" --uc "$J2T" --kapsam "${J1:0:10},${J2T:0:10}"
ayak "ilan yok → KIRMIZI" 1 2 "İLAN EDİLMEDİ" \
    --taban "$TABAN" --uc "$J2T" --kapsam ""
ayak "ilanda pakette olmayan commit → KIRMIZI" 1 2 "İLANDA VAR, PAKETTE YOK" \
    --taban "$TABAN" --uc "$J2T" --kapsam "$J1 $J2T $X"
ayak "bozuk ilan → KIRMIZI" 1 2 "HATALI İLAN" \
    --taban "$TABAN" --uc "$J2T" --kapsam "$J1 $J2T zzz"
ayak "birleştirme commit'i ilansız → KIRMIZI" 1 3 "KAPSAM DIŞI: ${MERGE:0:7}" \
    --taban "$TABAN" --uc "$MERGE" --kapsam "$Y $J1"
ayak "boş paket → ÖLÇEMEDİ" 2 "" "paket BOŞ" \
    --taban "$J2" --uc "$J2" --kapsam "$J2"
ayak "taban uç'un atası değil → ÖLÇEMEDİ" 2 "" "atası değil" \
    --taban "$X" --uc "$J2T" --kapsam "$J2T"
ayak "taban depoda yok → ÖLÇEMEDİ" 2 "" "taban depoda yok" \
    --taban "0000000000000000000000000000000000000000" --uc "$J2" --kapsam "$J2"

# ── SAFE-DEPLOY'UN KENDİ ÇAĞRISI ──
SAFE_DEPLOY="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/safe-deploy.sh"
TABAN_DOSYASI="$DEPO/.son-yayin"
echo "$TABAN" > "$TABAN_DOSYASI"

bagli_ayak() {
    local ad="$1" dal="$2" ilan="$3" beklenen_kod="$4" beklenen_metin="$5"
    local cikti kod
    git -C "$DEPO" checkout -q "$dal"
    cikti="$(
        cd "$DEPO" || exit 9
        # shellcheck source=/dev/null
        source "$SAFE_DEPLOY"
        LOG_FILE=/dev/null
        LAST_DEPLOYED_COMMIT_FILE="$TABAN_DOSYASI"
        fail() { echo "YAYIN DURDU: $1"; exit 1; }
        YAYIN_KAPSAMI="$ilan" yayin_kapsami_kapisi
        echo "YAYIN SÜRÜYOR"
    )" ; kod=$?
    git -C "$DEPO" checkout -q ana
    if [ "$kod" = "$beklenen_kod" ] && printf '%s' "$cikti" | grep -qF "$beklenen_metin"; then
        GECTI=$((GECTI + 1)); printf '  ✓ %s\n' "$ad"
    else
        DUSTU=$((DUSTU + 1)); printf '  ✗ %s — çıkış %s (beklenen %s)\n%s\n' "$ad" "$kod" "$beklenen_kod" "$(printf '%s' "$cikti" | sed 's/^/      /')"
    fi
}

bagli_ayak "safe-deploy: ilan yok → yayın DURUR" temiz "" 1 "İLAN EDİLMEDİ"
bagli_ayak "safe-deploy: ilgisiz commit → yayın DURUR" ana "$J1 $J2" 1 "KAPSAM DIŞI: ${X:0:7}"
bagli_ayak "safe-deploy: ilanla birebir → yayın GEÇER" temiz "$J1 $J2T" 0 "YAYIN SÜRÜYOR"

# Sıra: kapı `git pull`dan SONRA, ilk pahalı aşamadan (göç kapısı) ÖNCE.
govde="$(sed -n '/^main() {/,/^}/p' "$SAFE_DEPLOY")"
satir() { printf '%s\n' "$govde" | grep -nF "$1" | head -1 | cut -d: -f1; }
p="$(satir 'if ! git pull')"; k="$(satir 'yayin_kapsami_kapisi')"; g="$(satir 'asama "goc-kapisi"')"
if [ -n "$p" ] && [ -n "$k" ] && [ -n "$g" ] && [ "$p" -lt "$k" ] && [ "$k" -lt "$g" ]; then
    GECTI=$((GECTI + 1)); printf '  ✓ sıra: git pull (%s) < kapsam kapısı (%s) < göç kapısı (%s)\n' "$p" "$k" "$g"
else
    DUSTU=$((DUSTU + 1)); printf '  ✗ sıra: git pull=%s kapsam=%s göç=%s — kapı yerinde değil\n' "${p:-YOK}" "${k:-YOK}" "${g:-YOK}"
fi

echo "[sonda] geçti: ${GECTI} · düştü: ${DUSTU} · ayak: $((GECTI + DUSTU))"
[ "$DUSTU" -eq 0 ] && [ "$GECTI" -eq 14 ]
