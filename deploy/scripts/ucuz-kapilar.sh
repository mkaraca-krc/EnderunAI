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
#
# Biçim: "sınıf|ad|çalışma dizini|komut"
#
# SINIF: `hizli` ya da `agir`. Bu bir İKİNCİ LİSTE DEĞİL, listenin
# kendi üzerindeki bir ÖZELLİK — ayrım burada, tek yerde yazılı.
#
# ═══ NEDEN SINIF GEREKTİ (2026-09-04, ölçüldü) ═══
#
# Kanca ilk hâlinde dört kapıyı da koştu ve push DÜŞTÜ:
# "Connection to github.com closed by remote host."
#
# Sebep: git, SSH bağlantısını kancadan ÖNCE açıyor. Kanca 362 saniye
# sürünce uzak uç boşta kalan bağlantıyı kapattı.
#
# Süre dağılımı sorunu tek bir kapıya indirdi:
#   kurumsal kimlik    0-1 sn
#   tip kontrolü       5-8 sn
#   ön yüz derlemesi  79-92 sn
#   sır bekçisi      278 sn   ← arka ucu yeniden derliyor
#
# İlk düzeltmede yalnız sır bekçisi `agir` yapıldı ve hızlı küme 101
# saniyeye indi. Yine de riskliydi: düşen koşu 362 saniyedeydi ve SSH'ın
# boşta kalma toleransı belirsiz. Süreyi götüren ikinci kapı ön yüz
# derlemesiydi (92 sn), o da `agir` oldu.
#
# HIZLI KÜME ARTIK ~10 SANİYE: bu olayı doğuran kapı (kurumsal kimlik,
# 1 sn) ve ona en yakın koruma (tip kontrolü, 8 sn).
#
# ═══ DÜRÜST SINIR ═══
#
# Kanca artık `agir` kapıları koşmuyor. Yani SIR BEKÇİSİ VE ÖN YÜZ
# DERLEMESİ push öncesinde ÇALIŞMIYOR — yalnız yayın turunda
# çalışıyorlar. Bu bir eksiklik ve gizlenmiyor: kancanın verdiği
# güvence, listenin `hizli` kısmıdır.
#
# Tip kontrolü, derlemenin yakaladığı hataların çoğunu zaten
# yakalıyor; derlemenin ek olarak gördüğü şey Next'e özgü sorunlar.
#
# Buna rağmen liste TEK: iki çağıran aynı dosyayı okuyor, ayrım
# listenin kendi alanında duruyor. İki ayrı liste olsaydı biri
# güncellenip diğeri kalırdı.
KAPILAR=(
  "hizli|kurumsal kimlik|${FE}|node scripts/kimlik-taramasi.mjs"
  "hizli|tip kontrolü|${FE}|npx tsc --noEmit -p tsconfig.json"
  "agir|ön yüz derlemesi|${FE}|npm run build"
  "agir|sır bekçisi|${BE}|dotnet test EnderunAI.Api.Tests/EnderunAI.Api.Tests.csproj -v q --nologo --filter FullyQualifiedName~SecretInSourceGuardTests"
)

YALNIZ_HIZLI=0

case "${1:-}" in
    --liste)
        for kapi in "${KAPILAR[@]}"; do
            kalan="${kapi#*|}"
            echo "${kapi%%|*}  ${kalan%%|*}"
        done
        exit 0 ;;
    --hizli)
        YALNIZ_HIZLI=1 ;;
esac

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
    sinif="${kapi%%|*}"
    kalan="${kapi#*|}"
    ad="${kalan%%|*}"
    kalan="${kalan#*|}"
    dizin="${kalan%%|*}"
    komut="${kalan#*|}"

    if [ "$YALNIZ_HIZLI" = "1" ] && [ "$sinif" != "hizli" ]; then
        log "    — $ad ATLANDI (ağır kapı; yayın turunda koşacak)"
        continue
    fi

    sira=$((sira + 1))
    log "[$sira] $ad"
    kapi_basladi=$(date +%s)

    if ! (cd "$dizin" && eval "$komut") ; then
        hata "DÜŞTÜ: $ad"
        hata "Bu kapı ucuzdur; pahalı turlara girmeden durduruldu."
        exit 1
    fi

    log "    ✓ $ad ($(( $(date +%s) - kapi_basladi ))s)"
done

log "Ucuz kapıların hepsi geçti ($(( $(date +%s) - basladi ))s)."
