#!/usr/bin/env bash
#
# safe-deploy hızlı yol testleri.
#
# NEDEN VAR: hızlı yol, yayın kapısının bir kısmını atlıyor. Yanlış
# sınıflandırma "backend değişti ama testleri koşmadan yayınladık"
# demek olur ve bu sessizce olur — kimse fark etmez. O yüzden karar
# mantığı gözle değil testle doğrulanıyor.
#
# Kullanım: deploy/scripts/test-safe-deploy-fastpath.sh

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# safe-deploy source edildiğinde main'i çalıştırmıyor; yalnızca
# fonksiyonları tanımlıyor.
# shellcheck source=/dev/null
source "${SCRIPT_DIR}/safe-deploy.sh"

PASS=0
FAIL=0

expect() {
    local name="$1"
    local expected="$2"
    local input="$3"

    local actual
    actual="$(printf '%s' "$input" | classify_changed_paths)"

    if [ "$actual" = "$expected" ]; then
        PASS=$((PASS + 1))
        printf '  ✓ %s\n' "$name"
    else
        FAIL=$((FAIL + 1))
        printf '  ✗ %s\n      beklenen: %s\n      çıkan   : %s\n' \
            "$name" "$expected" "$actual"
    fi
}

echo "safe-deploy hızlı yol sınıflandırma testleri"
echo ""

# --- Hızlı yola HAK EDEN durumlar ---

expect "tek frontend sayfası" "frontend-only" \
'frontend/enderun-ai/app/hakedis/dosyalar/page.tsx'

expect "birden fazla frontend dosyası" "frontend-only" \
'frontend/enderun-ai/app/satin-alma/page.tsx
frontend/enderun-ai/components/purchasing/supplier-quality-card.tsx
frontend/enderun-ai/services/supplier-quality.service.ts'

expect "frontend testleri ve yapılandırması" "frontend-only" \
'frontend/enderun-ai/tests/modal.test.tsx
frontend/enderun-ai/vitest.config.mts
frontend/enderun-ai/package.json'

# --- TAM tur gerektiren durumlar ---

expect "tek .cs dosyası" "full" \
'backend/EnderunAI.Api/Security/TokenService.cs'

expect "frontend + backend karışık" "full" \
'frontend/enderun-ai/app/hakedis/dosyalar/page.tsx
backend/EnderunAI.Api/Controllers/HakedisController.cs'

expect "migration" "full" \
'backend/EnderunAI.Api/Migrations/20260812000000_AddSomething.cs'

expect "backend testi" "full" \
'backend/EnderunAI.Api.Tests/TokenCookieSizeTests.cs'

expect "deploy scriptinin kendisi" "full" \
'deploy/scripts/safe-deploy.sh'

expect "kök dizindeki belge" "full" \
'TEMIZLIK-TARAMASI.md'

expect "ops yapılandırması" "full" \
'ops/nginx/enderunai.com.tr'

expect "depo kökündeki betik" "full" \
'scripts/uc-ekran-taramasi.mjs'

# --- Sınır durumlar: hepsi TAM tur ---

expect "boş liste" "full" ''

expect "yalnızca boş satırlar" "full" \
'

'

# Önek benzeri ama farklı dizin: "frontend/enderun-ai-backup/..."
# yayınlanan uygulama DEĞİL. Önek eşleşmesi bunu yanlışlıkla
# frontend sayarsa, yedek dizinindeki bir değişiklik kapıyı atlatırdı.
expect "benzer adlı yedek dizini" "full" \
'frontend/enderun-ai-backup-20260730-172650/app/page.tsx'

expect "frontend kökü ama uygulama dışı" "full" \
'frontend/enderun-ai.previous-20260731-105600/app/page.tsx'


# ─────────────────────────────────────────────────────────────────
# YARIM KOŞU KARARI
#
# Sınanan şey saf karar fonksiyonu: "önceki koşu bu aşamada öldüyse
# devam edilebilir mi". İşaret dosyası kurulmuyor — kurulsaydı test,
# kararı değil dosya okumayı sınardı.
#
# Kritik ayrım: YAYINLAMA aşaması. O noktadan sonra publish/ yarım
# kalmış olabilir ve bir sonraki koşu onu sağlam geri-alma kopyasının
# üzerine yazar. Test aşamasında ölmüş bir koşu ise hiçbir şey bozmaz.
# ─────────────────────────────────────────────────────────────────

karar() {
    local name="$1" expected="$2" asama_adi="$3" onay="${4:-}"
    local actual
    actual="$(yarim_kosu_karari "$asama_adi" "$onay")"

    if [ "$actual" = "$expected" ]; then
        PASS=$((PASS + 1)); printf '  ✓ %s\n' "$name"
    else
        FAIL=$((FAIL + 1))
        printf '  ✗ %s\n      beklenen: %s\n      çıkan   : %s\n' "$name" "$expected" "$actual"
    fi
}

echo ""
echo "yarım koşu kararı testleri"
echo ""

# Yayınlama BAŞLAMAMIŞ — geri-alma kopyası sağlam, devam edilir.
karar "başlangıçta öldü"          "devam" "baslangic"
karar "backend testinde öldü"     "devam" "backend-testleri"
karar "ön yüz testinde öldü"      "devam" "on-yuz-testleri"
karar "sürüm yedeği alınırken öldü" "devam" "surum-yedegi"

# Yayınlama BAŞLAMIŞ — publish/ yarım olabilir, DURDUR.
karar "yayınlama sırasında öldü"  "dur" "yayinlama"
karar "ön yüz derlenirken öldü"   "dur" "on-yuz-derleme"
karar "veritabanı yedeğinde öldü" "dur" "veritabani-yedegi"
karar "servis başlatılırken öldü" "dur" "servis-baslatma"
karar "sağlık kontrolünde öldü"   "dur" "saglik-kontrolu"
karar "geri alınırken öldü"       "dur" "geri-alma"

# Tanınmayan aşama adı GÜVENLİ TARAFA düşmez — düşmemeli de.
#
# Aşama adı bilinmiyorsa koşunun nerede öldüğü de bilinmiyordur.
# "Bilmiyorum" durumunda devam etmek, tam da korunmak istenen
# senaryoyu serbest bırakırdı. Bu satır o varsayımı sabitliyor.
karar "aşama adı bilinmiyor"      "dur" "bilinmiyor"
karar "aşama adı boş"             "dur" ""
karar "uydurma aşama adı"         "dur" "filanca-asama"

# Açık onay verildiğinde geçilebilir — ama yalnız TAM eşleşmeyle.
karar "onay verildi"                    "devam-onayli" "yayinlama" "evet"
karar "onay 'e' yazılmış"               "dur"          "yayinlama" "e"
karar "onay 'EVET' (büyük harf)"        "dur"          "yayinlama" "EVET"
karar "onay 'yes'"                      "dur"          "yayinlama" "yes"

# Zararsız aşamada onay ARANMAZ.
karar "zararsız aşama, onaysız"   "devam" "backend-testleri" ""

echo ""
echo "Geçen: ${PASS}, Kalan: ${FAIL}"

[ "$FAIL" -eq 0 ] || exit 1
