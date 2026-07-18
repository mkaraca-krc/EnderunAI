#!/usr/bin/env bash
set -Eeuo pipefail
ENDERUN_ROOT="${ENDERUN_ROOT:-/var/www/enderun-ai}"
DEPLOY_ROOT="${DEPLOY_ROOT:-${ENDERUN_ROOT}/deploy}"
BACKUP_ROOT="${BACKUP_ROOT:-${ENDERUN_ROOT}/backups}"
LOG_ROOT="${LOG_ROOT:-${DEPLOY_ROOT}/logs}"
BACKEND_SOURCE="${BACKEND_SOURCE:-${ENDERUN_ROOT}/backend/EnderunAI.Api}"
FRONTEND_SOURCE="${FRONTEND_SOURCE:-${ENDERUN_ROOT}/frontend/enderun-ai}"
BACKEND_PUBLISH="${BACKEND_PUBLISH:-${ENDERUN_ROOT}/publish}"
BACKEND_SERVICE="${BACKEND_SERVICE:-enderunai-backend.service}"
FRONTEND_SERVICE="${FRONTEND_SERVICE:-enderunai-frontend.service}"
BACKEND_HEALTH_URL="${BACKEND_HEALTH_URL:-http://127.0.0.1:5155/api/projects}"
FRONTEND_HEALTH_URL="${FRONTEND_HEALTH_URL:-http://127.0.0.1:3000/login}"
timestamp(){ date +%Y%m%d_%H%M%S; }
log(){ printf '[%s] %s\n' "$(date '+%F %T')" "$*"; }
fail(){ log "HATA: $*"; exit 1; }
ensure_dirs(){ mkdir -p "${DEPLOY_ROOT}" "${BACKUP_ROOT}" "${LOG_ROOT}"; }
