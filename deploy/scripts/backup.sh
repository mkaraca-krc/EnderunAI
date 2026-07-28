#!/usr/bin/env bash
set -Eeuo pipefail
D="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${D}/common.sh"
ensure_dirs
STAMP="$(timestamp)"
DEST="${BACKUP_ROOT}/release-foundation-rc1-${STAMP}"
mkdir -p "${DEST}/frontend"
for item in middleware.ts app/api/auth/login/route.ts app/api/backend/'[...path]'/route.ts app/login/page.tsx app/globals.css components/erp/erp-shell.tsx; do
  if [[ -e "${FRONTEND_SOURCE}/${item}" ]]; then
    mkdir -p "${DEST}/frontend/$(dirname "${item}")"
    cp -a "${FRONTEND_SOURCE}/${item}" "${DEST}/frontend/${item}"
  fi
done
echo "${DEST}"
