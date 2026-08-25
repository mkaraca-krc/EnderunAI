#!/usr/bin/env bash
#
# GERİ YÜKLEME TATBİKATI — ÜÇ AYDA BİR.
#
# DENENMEMİŞ YEDEK YEDEK DEĞİLDİR. Bir yedeğin var olduğunu bilmek,
# açıldığını bilmek değildir; açıldığını bilmek de içindekinin doğru
# olduğunu bilmek değildir. Bu betik üçünü de sınar.
#
# Gece nöbeti her yedeğin AÇILDIĞINI doğruluyor (gpg bütünlük + PGDMP
# başlığı). Tatbikat ondan farklı ve daha ağır: yedeği GERÇEK bir
# veritabanına yükleyip içindekini canlıyla karşılaştırıyor.
#
# CANLIYA DOKUNMAZ: ayrı bir veritabanı kurar, sonunda düşürür.

set -uo pipefail

BACKUP_DIR="/var/backups/enderun"
BACKUP_KEY_FILE="/etc/enderunai/backup-key"
LOG_FILE="/var/log/enderun-backup.log"
ENV_FILE="/etc/enderunai/backend.env"
PROVA_DB="enderun_geri_yukleme_tatbikati"
CANLI_DB="enderun_ai"

log() { echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) [$1] TATBİKAT: $2" | tee -a "$LOG_FILE"; }
fail() { log "ERROR" "$1"; temizle; exit 1; }
temizle() { sudo -u postgres psql -q -c "DROP DATABASE IF EXISTS $PROVA_DB;" >/dev/null 2>&1; }

trap 'temizle' EXIT

[ -s "$BACKUP_KEY_FILE" ] || fail "Şifreleme anahtarı yok — tatbikat yapılamadı."

# EN YENİ YEDEK, DOSYA ADINDAKİ ZAMAN DAMGASINA GÖRE SEÇİLİR.
#
# Dosya değiştirme zamanına (`%T@`) göre seçmek YANLIŞ ÇIKTI: geçmiş
# yedekler toplu şifrelendiğinde hepsinin zamanı "şimdi" oldu ve
# tatbikat 17 gün önceki bir yedeği "en yeni" sanıp seçti. Şema o
# günden beri 184'ten 236 tabloya büyüdüğü için tatbikat düştü —
# yedek sağlamdı, seçim yanlıştı.
#
# Ad biçimi db_YYYYAAGG_SSDDss.dump.gpg olduğu için sözlük sırası
# zaman sırasıdır.
YEDEK="$(find "$BACKUP_DIR" -maxdepth 1 -name 'db_*.dump.gpg' | sort | tail -1)"
[ -n "$YEDEK" ] || fail "Şifreli veritabanı yedeği bulunamadı."

log "INFO" "Sınanan yedek: $(basename "$YEDEK")"

sudo -u postgres psql -v ON_ERROR_STOP=1 -q -c "DROP DATABASE IF EXISTS $PROVA_DB;" \
    -c "CREATE DATABASE $PROVA_DB;" || fail "Tatbikat veritabanı kurulamadı."

# Şifreli yedek DOĞRUDAN borudan yükleniyor: düz ara dosya yok.
gpg --batch --quiet --passphrase-file "$BACKUP_KEY_FILE" --decrypt "$YEDEK" 2>/dev/null \
    | sudo -u postgres pg_restore --dbname="$PROVA_DB" --no-owner --no-privileges 2>/dev/null
D=("${PIPESTATUS[@]}")
[ "${D[0]}" -eq 0 ] && [ "${D[1]}" -eq 0 ] \
    || fail "Geri yükleme BAŞARISIZ (gpg=${D[0]}, pg_restore=${D[1]})."

say() { sudo -u postgres psql -d "$1" -tAc "$2" 2>/dev/null | tr -d ' '; }

CANLI_TABLO="$(say "$CANLI_DB" "select count(*) from information_schema.tables where table_schema='public';")"
PROVA_TABLO="$(say "$PROVA_DB" "select count(*) from information_schema.tables where table_schema='public';")"

[ "$CANLI_TABLO" = "$PROVA_TABLO" ] \
    || fail "Tablo sayısı farklı: canlı=$CANLI_TABLO tatbikat=$PROVA_TABLO"

# SATIR SAYILARI YAKLAŞIK KARŞILAŞTIRILIYOR.
#
# Yedek alındıktan SONRA yazılan satırlar canlıda fazladır; bu bir
# eksiklik değil. Tatbikat "yedek boş mu, yarım mı" sorusunu soruyor.
# Tam eşitlik beklemek her tatbikatı sahte alarma çevirirdi.
SORUN=0
for T in personnel users companies projects cheques; do
    C="$(say "$CANLI_DB" "select count(*) from $T;")"
    P="$(say "$PROVA_DB" "select count(*) from $T;")"
    if [ -z "$P" ] || [ "$P" = "0" ] && [ "$C" != "0" ]; then
        log "ERROR" "  $T: canlı=$C tatbikat=$P — YEDEKTE VERİ YOK"
        SORUN=1
    elif [ "$P" -gt "$C" ] 2>/dev/null; then
        log "ERROR" "  $T: canlı=$C tatbikat=$P — yedekte canlıdan FAZLA satır, beklenmiyor"
        SORUN=1
    else
        log "INFO" "  $T: canlı=$C tatbikat=$P"
    fi
done

[ "$SORUN" -eq 0 ] || fail "Tatbikat BAŞARISIZ — yukarıdaki satırlara bakın."

log "INFO" "TATBİKAT BAŞARILI — $(basename "$YEDEK") gerçek bir veritabanına yüklendi, $PROVA_TABLO tablo doğrulandı."
