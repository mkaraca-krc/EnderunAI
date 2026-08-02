#!/usr/bin/env bash
#
# srvc141.trwww.com:465 (SMTP SSL) portunun açılıp açılmadığını kontrol eder.
# 10 dakikada bir systemd timer ile çalışır. Port açılınca log'a "PORT AÇILDI"
# yazar ve kendi timer'ını durdurup devre dışı bırakır.

set -uo pipefail

HOST="srvc141.trwww.com"
PORT="465"
LOG_FILE="/var/log/smtp-port-watch.log"
TIMER_UNIT="smtp-port-watch.timer"

log() {
    echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) $1" >> "$LOG_FILE"
}

if timeout 5 bash -c "exec 3<>/dev/tcp/${HOST}/${PORT}" 2>/dev/null; then
    exec 3>&- 2>/dev/null || true
    log "PORT AÇILDI - ${HOST}:${PORT} artık erişilebilir."
    systemctl stop "$TIMER_UNIT" 2>/dev/null
    systemctl disable "$TIMER_UNIT" 2>/dev/null
    log "İzleme durduruldu (${TIMER_UNIT} disable edildi)."
else
    log "kapalı - ${HOST}:${PORT} hâlâ erişilemiyor."
fi
