#!/usr/bin/env bash
#
# YAYIN KAPSAMI KAPISI — PAKET, İLAN EDİLEN KAPSAMIN DIŞINA TAŞAMAZ.
#
# ═══ NEDEN VAR (2026-09-10, ölçüldü) ═══
#
# Yayın betiği neyi TAŞIDIĞINI biliyordu ama neyi TAŞIMASI GEREKTİĞİNİ
# sormuyordu. `6dec0211` yayını tabandan beri 5 commit taşıdığını
# biliyordu ("[sir-tara] 5 commit"), ama o listeyi hiçbir ilanla
# karşılaştırmadı; ilan alacak bir girdi de yoktu. Push'la main'e giren
# SW/1 commit'leri (`e92b8c41`, `e4d54cdc`) bu yüzden, hangi iş için
# olursa olsun, ilk yayınla SESSİZCE canlıya çıkacaktı. Mehmet Karacabey
# bu kapı kurulana kadar yayını yasakladı.
#
# ═══ NE YAPAR ═══
#
# Paket = `TABAN..UC` aralığındaki commit'ler (son başarılı yayın → HEAD).
# İlan = `--kapsam` ile verilen commit kimlikleri (boşluk ya da virgül).
# Paketteki HER commit ilanda olmalı; ilandaki HER kimlik pakette olmalı.
#
# ═══ VARSAYILAN RET (sınıflandıramadığını geçirmez) ═══
#
#   KIRMIZI (çıkış 1): ilan yok · ilan dışı commit · çözülemeyen ya da
#                      birden çok commit'e uyan ilan · pakette olmayan ilan
#   ÖLÇEMEDİ (çıkış 2): taban yok ya da uç'un atası değil · paket boş ·
#                      git hatası — "karar veremedim" ile "geçer" aynı
#                      şey değildir; o da yayını durdurur
#   YEŞİL (çıkış 0): paket ve ilan birebir aynı küme
#
# ═══ TARAMA SAĞLIĞI ═══
#
# Her sonuç satırı karşılaştırılan commit SAYISINI yazar. "0 kapsam dışı"
# ancak yanında "karşılaştırılan N" ile anlamlıdır; boş paket hiçbir
# zaman YEŞİL değildir.
#
# Kullanım:
#   yayin-kapsami.sh --taban <commit> --uc <commit> --kapsam "<id> <id> ..."
#   (safe-deploy: --taban son yayın, --uc HEAD, --kapsam "$YAYIN_KAPSAMI")
#
# Sonda: deploy/scripts/test-yayin-kapsami.sh

set -uo pipefail

log() { echo "[yayin-kapsami] $*"; }

TABAN=""
UC=""
KAPSAM=""
KAPSAM_VERILDI=0

while [ $# -gt 0 ]; do
    case "$1" in
        --taban)  TABAN="${2:-}"; shift 2 ;;
        --uc)     UC="${2:-}"; shift 2 ;;
        --kapsam) KAPSAM="${2:-}"; KAPSAM_VERILDI=1; shift 2 ;;
        *) log "ÖLÇEMEDİ: bilinmeyen argüman: $1"; exit 2 ;;
    esac
done

olcemedi() { log "ÖLÇEMEDİ: $*"; log "Yayın DURDU — karar verilemeyen paket geçirilmez."; exit 2; }
kirmizi()  { log "KIRMIZI: $*"; log "Yayın DURDU."; exit 1; }

[ -n "$TABAN" ] || olcemedi "taban verilmedi (son yayın commit'i bilinmiyor)."
[ -n "$UC" ]    || olcemedi "uç verilmedi."

taban_tam="$(git rev-parse --verify --quiet "${TABAN}^{commit}")" \
    || olcemedi "taban depoda yok: ${TABAN}"
uc_tam="$(git rev-parse --verify --quiet "${UC}^{commit}")" \
    || olcemedi "uç depoda yok: ${UC}"

git merge-base --is-ancestor "$taban_tam" "$uc_tam" \
    || olcemedi "taban (${taban_tam:0:8}) uç'un (${uc_tam:0:8}) atası değil — paket tanımsız."

paket_ham="$(git rev-list "${taban_tam}..${uc_tam}")" \
    || olcemedi "git rev-list başarısız."
mapfile -t PAKET < <(printf '%s\n' "$paket_ham" | sed '/^$/d')
paket_sayisi="${#PAKET[@]}"

[ "$paket_sayisi" -gt 0 ] \
    || olcemedi "paket BOŞ (taban = uç = ${uc_tam:0:8}): karşılaştırılan commit 0 — boş küme her iddiayı doğrular."

if [ "$KAPSAM_VERILDI" -eq 0 ] || [ -z "$(printf '%s' "$KAPSAM" | tr -d ' ,\n\t')" ]; then
    log "karşılaştırılan commit: ${paket_sayisi} · ilan edilen: 0"
    kirmizi "kapsam İLAN EDİLMEDİ (YAYIN_KAPSAMI boş). İlansız paket geçirilmez."
fi

declare -A ILAN=()
hatali_ilan=()
for kimlik in $(printf '%s' "$KAPSAM" | tr ',' ' '); do
    if ! printf '%s' "$kimlik" | grep -qE '^[0-9a-f]{7,40}$'; then
        hatali_ilan+=("${kimlik} (commit kimliği biçiminde değil)")
        continue
    fi
    # `^{commit}` birden çok commit'e uyan kısa kimlikte başarısız olur.
    if ! tam="$(git rev-parse --verify --quiet "${kimlik}^{commit}")"; then
        hatali_ilan+=("${kimlik} (depoda yok ya da birden çok commit'e uyuyor)")
        continue
    fi
    ILAN["$tam"]=1
done
ilan_sayisi="${#ILAN[@]}"

declare -A PAKETTE=()
kapsam_disi=()
for c in "${PAKET[@]}"; do
    PAKETTE["$c"]=1
    [ -n "${ILAN[$c]:-}" ] || kapsam_disi+=("$c")
done

pakette_olmayan=()
for c in "${!ILAN[@]}"; do
    [ -n "${PAKETTE[$c]:-}" ] || pakette_olmayan+=("$c")
done

log "karşılaştırılan commit: ${paket_sayisi} · ilan edilen: ${ilan_sayisi} · kapsam dışı: ${#kapsam_disi[@]} · pakette olmayan ilan: ${#pakette_olmayan[@]} · hatalı ilan: ${#hatali_ilan[@]}"
log "paket: ${taban_tam:0:8}..${uc_tam:0:8}"

for c in "${kapsam_disi[@]}"; do
    log "  ✗ KAPSAM DIŞI: $(git log -1 --format='%h %s' "$c")"
done
for c in "${pakette_olmayan[@]}"; do
    log "  ✗ İLANDA VAR, PAKETTE YOK: $(git log -1 --format='%h %s' "$c")"
done
for h in "${hatali_ilan[@]}"; do
    log "  ✗ HATALI İLAN: $h"
done

if [ "${#kapsam_disi[@]}" -gt 0 ] || [ "${#pakette_olmayan[@]}" -gt 0 ] || [ "${#hatali_ilan[@]}" -gt 0 ]; then
    kirmizi "paket ilan edilen kapsamla birebir değil."
fi

log "YEŞİL: ${paket_sayisi} commit'in ${paket_sayisi}'i ilan edilmiş; ilan dışı 0."
exit 0
