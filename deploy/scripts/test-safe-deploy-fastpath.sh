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

echo ""
echo "Geçen: ${PASS}, Kalan: ${FAIL}"

[ "$FAIL" -eq 0 ] || exit 1
