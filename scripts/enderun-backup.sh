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
PROJECT_FILES_DIR="/var/www/enderun-data/project-files"
RETENTION_DAYS=30

# YEDEK ŞİFRELEME ANAHTARI — SIR, BU DOSYADA DEĞİL.
#
# Yedekler bugüne kadar DÜZ duruyordu: db_*.dump içinde çek, maaş ve
# personel verisi açık metin. Dizin ayrıca `drwxr-xr-x` idi, yani
# makinedeki her kullanıcı okuyabiliyordu (o düzeltildi).
#
# Anahtar BU BETİKTE ÜRETİLMEZ ve yazılmaz; root'a ait bir dosyadan
# okunur. Dosyayı Mehmet Karacabey oluşturur.
#
# ANAHTAR YEDEĞİN YANINDA DURMAMALI: aynı diskteki bir anahtar,
# diski ele geçirene her ikisini birden verir. Anahtarın kopyası
# sunucu DIŞINDA saklanmalı — kaybedilirse şifreli yedek kurtarılamaz.
BACKUP_KEY_FILE="/etc/enderunai/backup-key"

DB_HOST="127.0.0.1"
DB_PORT="5432"
DB_NAME="enderun_ai"
DB_USER="enderun_user"

TIMESTAMP="$(date +%Y%m%d_%H%M%S)"
DB_BACKUP_FILE="${BACKUP_DIR}/db_${TIMESTAMP}.dump"
UPLOADS_BACKUP_FILE="${BACKUP_DIR}/uploads_${TIMESTAMP}.tar.gz"
PROJECT_FILES_BACKUP_FILE="${BACKUP_DIR}/project-files_${TIMESTAMP}.tar.gz"

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

# Bir dosyayı yerinde şifreler; başarılıysa düz kopyayı siler.
#
# ANAHTAR YOKSA YEDEK YİNE ALINIR — yalnız şifresiz kalır ve ERROR
# olarak kayda düşer. Sebebi tarihsel: 2026-08 başında tablo sahipliği
# yüzünden sistem SAATLERCE yedeksiz kaldı ve bunu kimse fark etmedi.
# Şifreleme uğruna yedeğin KENDİSİNİ kaybetmek, düzeltmeye
# çalıştığımız riskten büyük bir risk olurdu.
sifrele() {
    local dosya="$1"

    if [ ! -s "$BACKUP_KEY_FILE" ]; then
        log "ERROR" "ŞİFRELEME ANAHTARI YOK ($BACKUP_KEY_FILE) — yedek DÜZ bırakıldı: $(basename "$dosya")"
        return 0
    fi

    if gpg --batch --yes --quiet \
           --passphrase-file "$BACKUP_KEY_FILE" \
           --symmetric --cipher-algo AES256 \
           --output "${dosya}.gpg" "$dosya"; then
        rm -f "$dosya"
        chmod 600 "${dosya}.gpg"
        log "INFO" "Şifrelendi: $(basename "${dosya}.gpg")"
    else
        # Şifreleme patlarsa düz dosya DURUR; yedeksiz kalmaktansa
        # şifresiz kalmak yeğdir, ama sessiz kalmaz.
        log "ERROR" "ŞİFRELEME BAŞARISIZ, yedek düz bırakıldı: $(basename "$dosya")"
    fi
}

log "INFO" "Yedekleme başladı."

export PGPASSWORD="$DB_PASSWORD"
if pg_dump -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -F c -f "$DB_BACKUP_FILE"; then
    DB_SIZE="$(du -h "$DB_BACKUP_FILE" | cut -f1)"
    log "INFO" "Veritabanı yedeği alındı: $DB_BACKUP_FILE ($DB_SIZE)"
    sifrele "$DB_BACKUP_FILE"
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
        sifrele "$UPLOADS_BACKUP_FILE"
    else
        rm -f "$UPLOADS_BACKUP_FILE"
        fail "uploads klasörü yedeklenemedi."
    fi
else
    log "WARN" "Uploads klasörü bulunamadı, atlandı: $UPLOADS_DIR"
fi

if [ -d "$PROJECT_FILES_DIR" ]; then
    if tar -czf "$PROJECT_FILES_BACKUP_FILE" -C "$(dirname "$PROJECT_FILES_DIR")" "$(basename "$PROJECT_FILES_DIR")"; then
        PROJECT_FILES_SIZE="$(du -h "$PROJECT_FILES_BACKUP_FILE" | cut -f1)"
        log "INFO" "Proje dosyaları yedeği alındı: $PROJECT_FILES_BACKUP_FILE ($PROJECT_FILES_SIZE)"
        sifrele "$PROJECT_FILES_BACKUP_FILE"
    else
        rm -f "$PROJECT_FILES_BACKUP_FILE"
        fail "project-files klasörü yedeklenemedi."
    fi
else
    log "WARN" "Proje dosyaları klasörü bulunamadı, atlandı: $PROJECT_FILES_DIR"
fi

DELETED_COUNT="$(find "$BACKUP_DIR" -maxdepth 1 -type f \( -name 'db_*.dump' -o -name 'db_*.dump.gpg' -o -name 'uploads_*.tar.gz' -o -name 'uploads_*.tar.gz.gpg' -o -name 'project-files_*.tar.gz' -o -name 'project-files_*.tar.gz.gpg' \) -mtime "+${RETENTION_DAYS}" -print -delete | wc -l)"
log "INFO" "${RETENTION_DAYS} günden eski ${DELETED_COUNT} yedek dosyası silindi."

log "INFO" "Yedekleme tamamlandı."
