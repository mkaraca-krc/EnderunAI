#!/usr/bin/env bash
#
# UCUZ KAPILAR — TEK TANIM, İKİ ÇAĞIRAN.
#
# ═══ NEDEN VAR (2026-09-04) ═══
#
# Kurumsal kimlik taraması, PAROLA/1 yayınında bir buton rengini
# yakaladı. Bulgu doğruydu ama YERİ yanlıştı: iki tam turdan (~27 dk)
# ve arka uç publish'inden SONRA geldi. Aynı bulgu, sıra tersine
# çevrilseydi 24 SANİYEDE gelirdi.
#
# Kapının doğruluğu değişmiyor — yalnız yeri.
#
# ═══ NEDEN TEK BETİK ═══
#
# Bu kapılar iki yerde koşuyor: yayın turunda (safe-deploy) ve push
# öncesinde (git pre-push kancası). Liste iki yerde ayrı ayrı
# yazılsaydı zamanla ayrışırdı ve AYRIŞAN HER NOKTA, BİRİNİN
# SINAMADIĞI BİR NOKTADIR.
#
# Bu kod tabanının en sık hatası tam olarak bu: aynı kuralın ikinci
# kopyası. Bir günde beş kez görüldü — merkez kuralının PUT kopyası,
# `dotnet ef` çağrısının üç ayrı ortamı, sır bekçisinin taranmayan
# yüzeyi, parola uzunluğunun iki kopyası, parola yazmanın üç ayrı yolu.
#
# ═══ ZAYIFLATMA DEĞİL ═══
#
# Buradaki kapıların hepsi tam turlarda YİNE koşuyor. Buradaki koşu
# yalnızca ERKEN DURDURMAK için. Bir kapı burada geçip tam turda
# düşerse, bu bir çelişki değil — buradaki hızlı sürüm dar, oradaki
# geniş.
#
# KULLANIM:
#   ucuz-kapilar.sh          # hepsini koşar, ilk düşende durur
#   ucuz-kapilar.sh --liste  # yalnız kapı adlarını basar

set -uo pipefail

REPO_ROOT="${REPO_ROOT:-/var/www/enderun-ai}"
FE="${REPO_ROOT}/frontend/enderun-ai"
BE="${REPO_ROOT}/backend"

log()  { echo "[ucuz-kapi] $*"; }
hata() { echo "[ucuz-kapi] HATA: $*" >&2; }

# ── KAPI LİSTESİ — TEK KAYNAK ──
# Biçim: "ad|çalışma dizini|komut"
KAPILAR=(
  "kurumsal kimlik|${FE}|node scripts/kimlik-taramasi.mjs"
  "tip kontrolü|${FE}|npx tsc --noEmit -p tsconfig.json"
  "ön yüz derlemesi|${FE}|npm run build"
  "sır bekçisi|${BE}|dotnet test EnderunAI.Api.Tests/EnderunAI.Api.Tests.csproj -v q --nologo --filter FullyQualifiedName~SecretInSourceGuardTests"
)

if [ "${1:-}" = "--liste" ]; then
    for kapi in "${KAPILAR[@]}"; do echo "${kapi%%|*}"; done
    exit 0
fi

# SIR BEKÇİSİ VERİTABANI İSTEMİYOR ama test projesi ortam değişkeni
# olmadan yüklenemiyor. Varsa canlıdan türetiliyor, yoksa o kapı
# ATLANMIYOR — hata veriyor (sessiz atlama, boş küme sorunudur).
if [ -z "${TEST_DB_CONNECTION:-}" ] && [ -r /etc/enderunai/backend.env ]; then
    canli="$(sudo grep -E '^DB_CONNECTION=' /etc/enderunai/backend.env 2>/dev/null \
             | sed -E 's/^DB_CONNECTION=//' | tr -d "'\"")"
    if [ -n "$canli" ]; then
        export TEST_DB_CONNECTION="${canli//Database=enderun_ai;/Database=enderun_ai_test;}"
        export DB_CONNECTION="$TEST_DB_CONNECTION"
    fi
fi

basladi=$(date +%s)
sira=0

for kapi in "${KAPILAR[@]}"; do
    sira=$((sira + 1))
    ad="${kapi%%|*}"
    kalan="${kapi#*|}"
    dizin="${kalan%%|*}"
    komut="${kalan#*|}"

    log "[$sira/${#KAPILAR[@]}] $ad"
    kapi_basladi=$(date +%s)

    if ! (cd "$dizin" && eval "$komut") ; then
        hata "DÜŞTÜ: $ad"
        hata "Bu kapı ucuzdur; pahalı turlara girmeden durduruldu."
        exit 1
    fi

    log "    ✓ $ad ($(( $(date +%s) - kapi_basladi ))s)"
done

log "Ucuz kapıların hepsi geçti ($(( $(date +%s) - basladi ))s)."
