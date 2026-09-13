#!/usr/bin/env bash
#
# ═══════════════════════════════════════════════════════════════════
# SAY — ÖLÇÜM ÜRETEN SAYIMLAR İÇİN TEK ARAÇ
# ═══════════════════════════════════════════════════════════════════
#
# ═══ NEDEN VAR: KURAL 84 BİR GÜNDE ÜÇ KEZ ISIRDI (2026-09-13) ═══
#
#   1. `sed 's/.*EnderunAI\.Api\.Tests\.//'` — `Passed!` ile `Failed!`ı
#      aynı dizgeye indirdi; ölçümün CEVABI silindi.
#   2. `--filter FullyQualifiedName~GoodsReceiptAccounting` — asıl testi
#      kapsamadı; "uçtan uca test yok" yazmama bir adım kalmıştı.
#   3. `ls Migrations/*.cs` — `Migrations/HumanResources/` altındaki 7
#      göçü görmedi; "canlıda kodda olmayan göç var" diye yanlış alarm.
#
# Üçüncü kez tekrarlayan hata disiplinle değil ARAÇLA kapatılır.
#
# ═══ İKİ TASARIM KARARI ═══
#
#   1. ÖZYİNELEME VARSAYILAN. `*.cs` yazan biri "bu klasördeki" değil
#      "bu kökteki" demek istiyor. Tek klasör isteniyorsa AÇIKÇA
#      istenir (`--duz`).
#
#   2. TARANAN DA BASILIR. Sayının yanında kök, desen ve hariç tutulanlar
#      görünür. Kapsam hatası ancak kapsam görünürse fark edilir —
#      çıplak bir "207" hiçbir şey söylemez, "207 (kök=…, desen=*.cs,
#      hariç=*.Designer.cs)" yanlış kökü ele verir.
#
# ═══ SIFIR İKİ ANLAMA GELİR — AYRILIYOR ═══
#
#   kök yoksa           → ÖLÇEMEDİ, çıkış 3 (sessiz 0 YOK)
#   kök var, eşleşme 0  → 0, çıkış 0 (gerçek ölçüm)
#
# Bu ayrım kuralın kendisi: taranmadığı hâlde "0" basan bir alet,
# yokluğu kanıtlıyormuş gibi görünür (Kural 48/84).
#
# ═══ KULLANIM ═══
#
#   say.sh --kok backend/EnderunAI.Api/Migrations --desen '*.cs' \
#          --haric '*.Designer.cs' --haric '*ModelSnapshot.cs'
#   say.sh --kok deploy --desen '*.sh' --duz
#   say.sh --kok . --desen '*.ts' --icerik 'TODO'     # içinde geçenler
#   say.sh ... --liste                                # eşleşenleri de bas
#   say.sh ... --sade                                 # YALNIZ sayı (boru için)
#
set -uo pipefail

KOK=""; DESEN=""; ICERIK=""; DUZ=0; LISTE=0; SADE=0
HARICLER=()

while [ $# -gt 0 ]; do
    case "$1" in
        --kok)    KOK="${2:?--kok için dizin gerekli}"; shift 2 ;;
        --desen)  DESEN="${2:?--desen için kalıp gerekli}"; shift 2 ;;
        --haric)  HARICLER+=("${2:?--haric için kalıp gerekli}"); shift 2 ;;
        --icerik) ICERIK="${2:?--icerik için düzenli ifade gerekli}"; shift 2 ;;
        --duz)    DUZ=1; shift ;;
        --liste)  LISTE=1; shift ;;
        --sade)   SADE=1; shift ;;
        *) echo "[say] bilinmeyen seçenek: $1" >&2; exit 64 ;;
    esac
done

[ -n "$KOK" ]   || { echo "[say] --kok verilmedi." >&2; exit 64; }
[ -n "$DESEN" ] || { echo "[say] --desen verilmedi." >&2; exit 64; }

# ── SIFIRIN İKİ ANLAMI: KÖK YOKSA ÖLÇEMEDİ ──────────────────────────
if [ ! -d "$KOK" ]; then
    echo "[say] ÖLÇEMEDİ: kök dizin yok: $KOK" >&2
    exit 3
fi

ARG=(find "$KOK")
[ "$DUZ" = "1" ] && ARG+=(-maxdepth 1)
ARG+=(-type f -name "$DESEN")
for h in ${HARICLER+"${HARICLER[@]}"}; do ARG+=(! -name "$h"); done

DOSYALAR="$("${ARG[@]}" 2>/dev/null | sort)"

# İçerik süzgeci istendiyse: eşleşen dosyalar daraltılır.
if [ -n "$ICERIK" ] && [ -n "$DOSYALAR" ]; then
    DOSYALAR="$(printf '%s\n' "$DOSYALAR" | while IFS= read -r d; do
        grep -qE "$ICERIK" "$d" 2>/dev/null && printf '%s\n' "$d"
    done)"
fi

SAYI=0
[ -n "$DOSYALAR" ] && SAYI=$(printf '%s\n' "$DOSYALAR" | grep -c .)

if [ "$SADE" = "1" ]; then
    printf '%s\n' "$SAYI"
else
    KAPSAM="kök=$KOK · desen=$DESEN"
    [ "$DUZ" = "1" ] && KAPSAM="$KAPSAM · YALNIZ ÜST KLASÖR"     || KAPSAM="$KAPSAM · özyinelemeli"
    [ ${#HARICLER[@]} -gt 0 ] && KAPSAM="$KAPSAM · hariç=${HARICLER[*]}"
    [ -n "$ICERIK" ] && KAPSAM="$KAPSAM · içerik=/$ICERIK/"
    printf '%s   [%s]\n' "$SAYI" "$KAPSAM"
fi

[ "$LISTE" = "1" ] && [ -n "$DOSYALAR" ] && printf '%s\n' "$DOSYALAR" | sed 's/^/  /'
exit 0
