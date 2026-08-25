#!/usr/bin/env bash
#
# EnderunAI günlük yedekleme scripti.
# Veritabanı (pg_dump, custom format) + uploads + proje dosyaları.
# systemd timer (enderun-backup.timer) ile her gece 03:00'te çalışır.
# safe-deploy.sh de her yayından önce bu betiği çağırır.
#
# TEK KOPYASI REPODA: scripts/enderun-backup.sh. /usr/local/bin altındaki
# çalışan kopya bunun aynısıdır ve senkronu testle korunuyor
# (BackupScriptSyncTests). nginx yapılandırmasındaki disiplinin aynısı.

set -uo pipefail

# Yedek dosyaları root'a özel oluşsun: 0600. Şifrelenmiş de olsalar
# dosya izni ilk savunma hattıdır.
umask 077

BACKUP_DIR="/var/backups/enderun"
LOG_FILE="/var/log/enderun-backup.log"
ENV_FILE="/etc/enderunai/backend.env"
UPLOADS_DIR="/var/www/enderun-ai/uploads"
PROJECT_FILES_DIR="/var/www/enderun-data/project-files"
RETENTION_DAYS=30

# ═══════════════════════════════════════════════════════════════════
# YEDEK ŞİFRELEME ANAHTARI — SIR, BU DOSYADA DEĞİL.
#
# Anahtar BU BETİKTE ÜRETİLMEZ ve yazılmaz; root'a ait 0400 bir
# dosyadan okunur.
#
# ANAHTAR YOKSA YEDEK ALINMAZ — BETİK DURUR.
#
# Bu, betiğin önceki davranışının TERSİ. Önce "anahtar yoksa yedeği
# yine al, düz bırak, ERROR yaz" deniyordu; gerekçe 2026-08 başında
# sistemin saatlerce yedeksiz kalmasıydı. Karar değişti (Mehmet
# Karacabey, 2026-08-25): şifresiz bir dump diske HİÇ düşmemeli.
#
# Gerekçe ölçümle geldi: 2 Ağustos'tan beri 532 düz veritabanı yedeği
# birikmişti ve içlerinde bugün tablodan/kayıttan/günlükten temizlenen
# token açık metin duruyordu. Diskteki düz kopya, temizliğin tamamını
# anlamsız kılıyor.
#
# YEDEKSİZ KALMA RİSKİ NASIL KARŞILANIYOR: bu betik ARTIK SESSİZ
# BAŞARISIZ OLMUYOR — exit 1 ile duruyor, systemd birimi "failed"
# durumuna düşüyor ve safe-deploy yedek adımında yayını kesiyor.
# Görünür hata, sessiz hatadan iyidir.
BACKUP_KEY_FILE="/etc/enderunai/backup-key"

# ANAHTARIN NEREDE DURACAĞI — AÇIK KARAR (BEKLEYEN KARARLAR).
# Bugün anahtar da yedekler de AYNI DİSKTE. Diski ele geçiren ikisini
# birden alır; disk kaybolursa ikisi birden gider. Sunucu dışı bir
# kopya olmadan şifreleme, "diski çalan okuyamasın" korumasını
# vermiyor — yalnız yanlışlıkla kopyalanan tek dosyayı koruyor.
# ═══════════════════════════════════════════════════════════════════

DB_HOST="127.0.0.1"
DB_PORT="5432"
DB_NAME="enderun_ai"
DB_USER="enderun_user"

TIMESTAMP="$(date +%Y%m%d_%H%M%S)"
DB_BACKUP_FILE="${BACKUP_DIR}/db_${TIMESTAMP}.dump.gpg"
UPLOADS_BACKUP_FILE="${BACKUP_DIR}/uploads_${TIMESTAMP}.tar.gz.gpg"
PROJECT_FILES_BACKUP_FILE="${BACKUP_DIR}/project-files_${TIMESTAMP}.tar.gz.gpg"

log() {
    echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) [$1] $2" | tee -a "$LOG_FILE"
}

fail() {
    log "ERROR" "$1"
    exit 1
}

mkdir -p "$BACKUP_DIR" || fail "Yedek dizini oluşturulamadı: $BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

[ -f "$ENV_FILE" ] || fail "Ortam değişkeni dosyası bulunamadı: $ENV_FILE"

# ÖN KOŞUL: anahtar. Yedek almadan ÖNCE bakılıyor — yarısı alınmış bir
# koşunun ortasında durmak, hiç başlamamaktan kötü.
[ -s "$BACKUP_KEY_FILE" ] || fail "ŞİFRELEME ANAHTARI YOK ($BACKUP_KEY_FILE) — YEDEK ALINMADI. Şifresiz yedek yazılmıyor."

command -v gpg >/dev/null 2>&1 || fail "gpg bulunamadı — YEDEK ALINMADI."

DB_PASSWORD="$(grep -E '^DB_CONNECTION=' "$ENV_FILE" | sed -E "s/.*Password=([^;']*).*/\1/")"
[ -n "$DB_PASSWORD" ] || fail "DB_CONNECTION içinden şifre okunamadı."

# Standart girdiyi şifreleyip hedefe yazar.
#
# AKIŞTA ŞİFRELEME — DİSKE DÜZ HALİ HİÇ DÜŞMEZ. Önce yazıp sonra
# şifrelemek, arada bir pencere bırakır: o pencerede düz dump diskte
# durur ve süreç ölürse orada KALIR.
sifreli_yaz() {
    gpg --batch --yes --quiet \
        --passphrase-file "$BACKUP_KEY_FILE" \
        --symmetric --cipher-algo AES256 \
        --output "$1"
}

coz() {
    gpg --batch --quiet --passphrase-file "$BACKUP_KEY_FILE" --decrypt "$1" 2>/dev/null
}

# Şifreli dosyanın gerçekten AÇILDIĞINI doğrular — DÜZ KOPYA ÜRETMEDEN.
#
# Yazılmış olması okunabilir olduğunu göstermez. Bu dizinde
# "BOZUK-YARIM_db_20260814" adlı bir dosya duruyor: doğrulanmamış
# yedeğin ne demek olduğunun kanıtı.
#
# NEDEN `pg_restore --list` KULLANILMIYOR: özel biçimli arşivi
# BORUDAN okuyamıyor (ölçüldü: borudan çıkış 2, dosyadan çıkış 0).
# Dosyadan okutmak için düz dump'ı diske yazmak gerekirdi — kaçındığımız
# şeyin ta kendisi. `/dev/shm` de çözüm değil: bu makinede takas
# AÇIK (4 GB), tmpfs sayfası takas dosyası üzerinden diske düşebilir.
#
# YERİNE İKİ AŞAMA:
#   1. Tam çözme → gpg'nin kendi bütünlük denetimi (MDC) çalışır.
#      Kırpılmış, bozulmuş veya yanlış anahtarlı dosya burada düşer.
#   2. İlk 5 bayt "PGDMP" mi — içindekinin gerçekten bir pg_dump
#      arşivi olduğunu gösterir.
#
# pg_restore ile TAM doğrulama, ayrı ve elle yapılan GERİ YÜKLEME
# PROVASINDA yapılıyor (DURUM.md, yedek bölümü). Nöbet ile prova
# ayrı işler: nöbet her gece, prova dönemsel.
dogrula() {
    local dosya="$1" tur="$2"

    case "$tur" in
        dump)
            coz "$dosya" > /dev/null || return 1
            [ "$(coz "$dosya" | head -c 5)" = "PGDMP" ] || return 1
            ;;
        tar)
            # tar boruyu sorunsuz okuyor: tam yapısal doğrulama.
            coz "$dosya" | tar -tzf - >/dev/null 2>&1 || return 1
            ;;
    esac
    return 0
}

log "INFO" "Yedekleme başladı."

# ── VERİTABANI ────────────────────────────────────────────────────
export PGPASSWORD="$DB_PASSWORD"
pg_dump -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -F c \
    | sifreli_yaz "$DB_BACKUP_FILE"
DURUM=("${PIPESTATUS[@]}")
unset PGPASSWORD

# İKİ ÇIKIŞ KODU DA KONTROL EDİLİYOR. pg_dump yarıda ölse bile gpg
# geçerli bir .gpg üretir — içinde YARIM bir dump'la. pipefail tek
# başına hangi ucun düştüğünü söylemiyor; PIPESTATUS söylüyor.
if [ "${DURUM[0]}" -ne 0 ] || [ "${DURUM[1]}" -ne 0 ]; then
    rm -f "$DB_BACKUP_FILE"
    fail "Veritabanı yedeği başarısız (pg_dump=${DURUM[0]}, gpg=${DURUM[1]}) — yarım dosya silindi."
fi

dogrula "$DB_BACKUP_FILE" dump || { rm -f "$DB_BACKUP_FILE"; fail "Veritabanı yedeği AÇILAMADI, silindi: $(basename "$DB_BACKUP_FILE")"; }
log "INFO" "Veritabanı yedeği şifreli alındı ve açıldığı doğrulandı: $(basename "$DB_BACKUP_FILE") ($(du -h "$DB_BACKUP_FILE" | cut -f1))"

# ── KLASÖRLER ─────────────────────────────────────────────────────
klasor_yedekle() {
    local dizin="$1" hedef="$2" ad="$3"

    if [ ! -d "$dizin" ]; then
        log "WARN" "$ad klasörü bulunamadı, atlandı: $dizin"
        return 0
    fi

    tar -czf - -C "$(dirname "$dizin")" "$(basename "$dizin")" | sifreli_yaz "$hedef"
    local d=("${PIPESTATUS[@]}")

    if [ "${d[0]}" -ne 0 ] || [ "${d[1]}" -ne 0 ]; then
        rm -f "$hedef"
        fail "$ad yedeği başarısız (tar=${d[0]}, gpg=${d[1]}) — yarım dosya silindi."
    fi

    dogrula "$hedef" tar || { rm -f "$hedef"; fail "$ad yedeği AÇILAMADI, silindi: $(basename "$hedef")"; }
    log "INFO" "$ad yedeği şifreli alındı ve açıldığı doğrulandı: $(basename "$hedef") ($(du -h "$hedef" | cut -f1))"
}

klasor_yedekle "$UPLOADS_DIR" "$UPLOADS_BACKUP_FILE" "Uploads"
klasor_yedekle "$PROJECT_FILES_DIR" "$PROJECT_FILES_BACKUP_FILE" "Proje dosyaları"

# ── TEMİZLİK ──────────────────────────────────────────────────────
# Düz adlar listede KALIYOR: geçmişte kalmış düz dosyalar da saklama
# süresine tabi olsun. Bugün hepsi şifrelendi, ama süzgeç daralırsa
# eski bir düz dosya sessizce sonsuza kadar kalırdı.
DELETED_COUNT="$(find "$BACKUP_DIR" -maxdepth 1 -type f \
    \( -name 'db_*.dump' -o -name 'db_*.dump.gpg' \
    -o -name 'uploads_*.tar.gz' -o -name 'uploads_*.tar.gz.gpg' \
    -o -name 'project-files_*.tar.gz' -o -name 'project-files_*.tar.gz.gpg' \) \
    -mtime "+${RETENTION_DAYS}" -print -delete | wc -l)"
log "INFO" "${RETENTION_DAYS} günden eski ${DELETED_COUNT} yedek dosyası silindi."

# DÜZ DOSYA NÖBETİ: şifresiz bir yedek diske düşerse sessiz kalmasın.
DUZ="$(find "$BACKUP_DIR" -maxdepth 1 -type f \( -name 'db_*.dump' -o -name 'uploads_*.tar.gz' -o -name 'project-files_*.tar.gz' \) | wc -l)"
if [ "$DUZ" -ne 0 ]; then
    log "ERROR" "DİZİNDE ${DUZ} ADET ŞİFRESİZ YEDEK VAR — beklenmiyor, incelenmeli."
fi

log "INFO" "Yedekleme tamamlandı."
