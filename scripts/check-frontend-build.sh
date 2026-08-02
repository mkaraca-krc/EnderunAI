#!/usr/bin/env bash
#
# Frontend'in hatasız build olduğunu ve kritik sayfaların (login, dashboard,
# projeler, işveren portalı) derleme çıktısında yer aldığını doğrular.
# CI'da ve safe-deploy.sh içinde kullanılır.

set -uo pipefail

FRONTEND_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../frontend/enderun-ai" && pwd)"
REQUIRED_ROUTES=("/login" "/dashboard" "/projeler" "/portal/\[token\]")

cd "$FRONTEND_DIR" || exit 1

echo "[check-frontend-build] npm run build çalıştırılıyor: $FRONTEND_DIR"

BUILD_OUTPUT="$(npm run build 2>&1)"
BUILD_EXIT_CODE=$?

echo "$BUILD_OUTPUT"

if [ $BUILD_EXIT_CODE -ne 0 ]; then
    echo "[check-frontend-build] HATA: npm run build başarısız oldu (exit $BUILD_EXIT_CODE)."
    exit 1
fi

FAILED=0
for route in "${REQUIRED_ROUTES[@]}"; do
    if echo "$BUILD_OUTPUT" | grep -qE "$route"; then
        echo "[check-frontend-build] OK   - $route derleme çıktısında bulundu."
    else
        echo "[check-frontend-build] EKSİK - $route derleme çıktısında bulunamadı."
        FAILED=1
    fi
done

if [ $FAILED -ne 0 ]; then
    echo "[check-frontend-build] HATA: bazı kritik sayfalar derleme çıktısında yok."
    exit 1
fi

echo "[check-frontend-build] Tüm kritik sayfalar başarıyla derlendi."
exit 0
