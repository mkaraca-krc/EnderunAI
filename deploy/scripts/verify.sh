#!/usr/bin/env bash
set -Eeuo pipefail
D="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${D}/common.sh"
[[ -d "${BACKEND_SOURCE}" ]] || fail "Backend kaynak klasörü yok"
[[ -d "${FRONTEND_SOURCE}" ]] || fail "Frontend kaynak klasörü yok"
[[ -d "${BACKEND_PUBLISH}" ]] || fail "Publish klasörü yok"
grep -q 'enderun_token' "${FRONTEND_SOURCE}/app/api/auth/login/route.ts" || fail "Login cookie standardı yanlış"
grep -q 'enderun_token' "${FRONTEND_SOURCE}/app/api/backend/[...path]/route.ts" || fail "Proxy cookie standardı yanlış"
"${D}/healthcheck.sh"
log "Foundation RC1 doğrulaması başarılı"
