#!/usr/bin/env bash
#
# EnderunAI günlük yedekleme scripti.
# Veritabanı (pg_dump, custom format) + uploads klasörünü (tar.gz)
# /var/backups/enderun altına tarihli olarak yedekler, 30 günden eski
# yedekleri siler. systemd timer (enderun-backup.timer) ile her gece
# 03:00'te çalıştırılır.

set -uo pipefail

BACKUP_DIR="/var/backups/enderun"
LOG_FILE="/var/log/enderun-backup.log"
ENV_FILE="/etc/enderunai/backend.env"
UPLOADS_DIR="/var/www/enderun-ai/uploads"
RETENTION_DAYS=30

DB_HOST="127.0.0.1"
DB_PORT="5432"
DB_NAME="enderun_ai"
DB_USER="enderun_user"

TIMESTAMP="$(date +%Y%m%d_%H%M%S)"
DB_BACKUP_FILE="${BACKUP_DIR}/db_${TIMESTAMP}.dump"
UPLOADS_BACKUP_FILE="${BACKUP_DIR}/uploads_${TIMESTAMP}.tar.gz"

log() {
    echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) [$1] $2" | tee -a "$LOG_FILE"
}

fail() {
    log "ERROR" "$1"
    exit 1
}

mkdir -p "$BACKUP_DIR" || fail "Yedek dizini oluşturulamadı: $BACKUP_DIR"

if [ ! -f "$ENV_FILE" ]; then
    fail "Ortam değişkeni dosyası bulunamadı: $ENV_FILE"
fi

DB_PASSWORD="$(grep -E '^DB_CONNECTION=' "$ENV_FILE" | sed -E "s/.*Password=([^;']*).*/\1/")"
if [ -z "$DB_PASSWORD" ]; then
    fail "DB_CONNECTION içinden şifre okunamadı."
fi

log "INFO" "Yedekleme başladı."

export PGPASSWORD="$DB_PASSWORD"
if pg_dump -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -F c -f "$DB_BACKUP_FILE"; then
    DB_SIZE="$(du -h "$DB_BACKUP_FILE" | cut -f1)"
    log "INFO" "Veritabanı yedeği alındı: $DB_BACKUP_FILE ($DB_SIZE)"
else
    unset PGPASSWORD
    rm -f "$DB_BACKUP_FILE"
    fail "pg_dump başarısız oldu."
fi
unset PGPASSWORD

if [ -d "$UPLOADS_DIR" ]; then
    if tar -czf "$UPLOADS_BACKUP_FILE" -C "$(dirname "$UPLOADS_DIR")" "$(basename "$UPLOADS_DIR")"; then
        UPLOADS_SIZE="$(du -h "$UPLOADS_BACKUP_FILE" | cut -f1)"
        log "INFO" "Uploads yedeği alındı: $UPLOADS_BACKUP_FILE ($UPLOADS_SIZE)"
    else
        rm -f "$UPLOADS_BACKUP_FILE"
        fail "uploads klasörü yedeklenemedi."
    fi
else
    log "WARN" "Uploads klasörü bulunamadı, atlandı: $UPLOADS_DIR"
fi

DELETED_COUNT="$(find "$BACKUP_DIR" -maxdepth 1 -type f \( -name 'db_*.dump' -o -name 'uploads_*.tar.gz' \) -mtime "+${RETENTION_DAYS}" -print -delete | wc -l)"
log "INFO" "${RETENTION_DAYS} günden eski ${DELETED_COUNT} yedek dosyası silindi."

log "INFO" "Yedekleme tamamlandı."
