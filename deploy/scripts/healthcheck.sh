#!/usr/bin/env bash
set -Eeuo pipefail
D="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${D}/common.sh"
ensure_dirs
systemctl is-active --quiet "${BACKEND_SERVICE}" || fail "${BACKEND_SERVICE} aktif değil"
systemctl is-active --quiet "${FRONTEND_SERVICE}" || fail "${FRONTEND_SERVICE} aktif değil"
B="$(curl -s -o /dev/null -w '%{http_code}' "${BACKEND_HEALTH_URL}")"
F="$(curl -s -o /dev/null -w '%{http_code}' "${FRONTEND_HEALTH_URL}")"
log "Backend: ${B}"
log "Frontend: ${F}"
[[ "${B}" == "200" || "${B}" == "401" ]] || fail "Backend sağlık kontrolü başarısız"
[[ "${F}" == "200" ]] || fail "Frontend sağlık kontrolü başarısız"
log "Sağlık kontrolü başarılı"
