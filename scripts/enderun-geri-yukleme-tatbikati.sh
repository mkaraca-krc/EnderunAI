#!/usr/bin/env bash
#
# GERİ YÜKLEME TATBİKATI — HER GECE.
#
# DENENMEMİŞ YEDEK YEDEK DEĞİLDİR. Bir yedeğin var olduğunu bilmek,
# açıldığını bilmek değildir; açıldığını bilmek de içindekinin doğru
# olduğunu bilmek değildir. Bu betik üçünü de sınar.
#
# Gece nöbeti her yedeğin AÇILDIĞINI doğruluyor (gpg bütünlük + PGDMP
# başlığı). Tatbikat ondan farklı ve daha ağır: yedeği GERÇEK bir
# veritabanına yükleyip içindekini YEDEĞİN KENDİ DAMGASIYLA
# karşılaştırıyor.
#
# ═══ CANLIYA HİÇ BAĞLANMAZ ═══
#
# Eski sürüm satır sayılarını CANLIYLA karşılaştırıyordu ve bu yüzden
# karşılaştırma "yaklaşık" olmak zorundaydı: canlı, yedek alındıktan
# sonra değişir. Şimdi karşılaştırma yedeğin yanındaki damgayla
# yapılıyor — damga değişmez, yani TAM EŞİTLİK istenebiliyor.
#
# Yan kazanç, asıl kazançtan büyük: tatbikatın canlı veritabanına
# bağlanmak için hiçbir sebebi kalmadı. Kimliği de bağlanamıyor.
#
# ═══ ÜÇ KATMANLI MUHAFIZ — CANLIYA YAZMA İHTİMALİ ═══
#
# Günde bir koşan bir işin canlıya değme ihtimali, üç ayda bir
# koşandan 90 kat fazladır. Üç katman, ÜÇÜ DE FAIL-CLOSED:
#
#   1. BEYAZ LİSTE — hedef adı yalnız tek bir değer olabilir. Kara
#      liste değil: "bilinen kötüleri say" her zaman eksik kalır
#      (`postgres` veritabanı bunun canlı örneğiydi — 0 tablosu var,
#      tablo kontrolünden de geçerdi).
#   2. DOLULUK — hedef veritabanı zaten varsa ve İÇİNDE TABLO VARSA
#      hiçbir şey yapmadan durulur. DROP'a hiç gelinmez.
#   3. VERİTABANI YETKİSİ — bağlanan rol `enderun_tatbikat`, ve o
#      rolün `enderun_ai` üzerinde CONNECT hakkı YOK.
#
# ÜÇÜNCÜ KATMAN NEDEN ŞART: ilk ikisi de BU DOSYADA duruyor ve tek bir
# düzenlemeyle birlikte kaldırılabilirler. Veritabanı yetkisi betiğin
# DIŞINDA; betiği düzenleyerek aşılamaz. Sabotaj sondası tam olarak
# bunu sınıyor: iki kontrol de kaldırılıp hedef canlıya çevrildiğinde
# bağlantı yetki hatasıyla düşüyor.

set -uo pipefail

BASLANGIC=$(date +%s)

BACKUP_DIR="/var/backups/enderun"
BACKUP_KEY_FILE="/etc/enderunai/backup-key"
LOG_FILE="/var/log/enderun-backup.log"
TATBIKAT_ENV="/etc/enderunai/tatbikat.env"
DAMGA_DOSYASI="/var/lib/enderun-ai/tatbikat-son-basari.txt"

# BEYAZ LİSTE — tek kabul edilen ad. Değiştirilirse aşağıdaki kontrol düşer.
IZINLI_HEDEF="enderun_geri_yukleme_tatbikati"
PROVA_DB="enderun_geri_yukleme_tatbikati"

log()  { echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) [$1] TATBİKAT: $2" | tee -a "$LOG_FILE"; }

# TEMİZLİK DE MUHAFIZLI: bu fonksiyon EXIT tuzağında koşuyor, yani
# betiğin her çıkışında çalışıyor. Muhafız geçmeden DROP çalıştırmaz —
# aksi hâlde en tehlikeli satır, en sık koşan satır olurdu.
MUHAFIZ_GECTI=0
temizle() {
    [ "$MUHAFIZ_GECTI" = "1" ] || return 0
    tatbikat_psql postgres -c "DROP DATABASE IF EXISTS $PROVA_DB;" >/dev/null 2>&1
}
fail() { log "ERROR" "$1"; temizle; exit 1; }

trap 'temizle' EXIT

# ── KİMLİK: enderun_tatbikat, TCP ÜZERİNDEN, PAROLAYLA ──────────────
#
# Yerel soket `peer` doğrulaması kullanıyor; root olarak soketten
# `enderun_tatbikat` gibi bağlanmak MÜMKÜN DEĞİL. Yani bu betik
# zorunlu olarak TCP + parola yolundan geçiyor ve `sudo -u postgres`
# süperkullanıcı yolu buradan tamamen çıktı.
[ -r "$TATBIKAT_ENV" ] || { log "ERROR" "TATBİKAT: $TATBIKAT_ENV okunamadı."; exit 1; }
# shellcheck disable=SC1090
. "$TATBIKAT_ENV"
: "${TATBIKAT_DB_KULLANICI:=}" "${TATBIKAT_DB_PAROLASI:=}"
[ -n "$TATBIKAT_DB_KULLANICI" ] && [ -n "$TATBIKAT_DB_PAROLASI" ] \
    || { log "ERROR" "TATBİKAT: tatbikat kimliği eksik ($TATBIKAT_ENV)."; exit 1; }

tatbikat_psql() {
    local db="$1"; shift
    PGPASSWORD="$TATBIKAT_DB_PAROLASI" psql -h 127.0.0.1 -p 5432 \
        -U "$TATBIKAT_DB_KULLANICI" -d "$db" -Aqt "$@"
}

# ── KATMAN 1: BEYAZ LİSTE ───────────────────────────────────────────
[ "$PROVA_DB" = "$IZINLI_HEDEF" ] \
    || { log "ERROR" "TATBİKAT: hedef '$PROVA_DB' beyaz listede değil (izinli: '$IZINLI_HEDEF'). DURDURULDU."; exit 1; }

# ── KATMAN 2: HEDEF DOLU MU ─────────────────────────────────────────
#
# Tatbikat veritabanı her koşunun sonunda düşürülüyor, yani koşu
# başında ya yoktur ya boştur. İÇİNDE TABLO OLAN bir veritabanı bizim
# değildir ve dokunulmaz — DROP'a gelmeden durulur.
MEVCUT_TABLO="$(tatbikat_psql postgres -c "
    select case when exists (select 1 from pg_database where datname = '$PROVA_DB')
           then 'VAR' else 'YOK' end;" 2>/dev/null | tr -d ' ')"

if [ "$MEVCUT_TABLO" = "VAR" ]; then
    TABLO_SAYISI="$(tatbikat_psql "$PROVA_DB" -c "select count(*) from information_schema.tables where table_schema='public';" 2>/dev/null | tr -d ' ')"
    if [ -z "$TABLO_SAYISI" ]; then
        log "ERROR" "TATBİKAT: '$PROVA_DB' var ama içine bakılamadı. Emin olunamayan hedefe DOKUNULMAZ."
        exit 1
    fi
    if [ "$TABLO_SAYISI" -ne 0 ]; then
        log "ERROR" "TATBİKAT: '$PROVA_DB' zaten var ve $TABLO_SAYISI tablo taşıyor — bu bizim veritabanımız değil. DURDURULDU."
        exit 1
    fi
fi

MUHAFIZ_GECTI=1

[ -s "$BACKUP_KEY_FILE" ] || fail "Şifreleme anahtarı yok — tatbikat yapılamadı."

# ── SINANACAK YEDEK VE ONUN DAMGASI ─────────────────────────────────
#
# EN YENİ YEDEK, DOSYA ADINDAKİ ZAMAN DAMGASINA GÖRE SEÇİLİR.
# Dosya değiştirme zamanına (`%T@`) göre seçmek YANLIŞ ÇIKTI: geçmiş
# yedekler toplu şifrelendiğinde hepsinin zamanı "şimdi" oldu ve
# tatbikat 17 gün önceki bir yedeği "en yeni" sanıp seçti.
# ── ÖNCE: YEDEK HÂLÂ ALINIYOR MU ────────────────────────────────
#
# Tatbikat 03:31'de, gece yedeği 03:00'te koşuyor. Yedek normalde 7
# saniye sürüyor ama bir gün uzarsa tatbikat ONU BEKLEMELİ — yoksa
# sessizce bir öncekine düşer ve "geri yükleme çalışıyor" der. O yeşil,
# BUGÜNÜN yedeği hakkında hiçbir şey söylemez.
#
# Beklemek de sonsuz olamaz: tavana vurulursa açıkça "hâlâ koşuyor"
# denip durulur. Sessizce eskiye düşmek yok, sonsuz bekleme de yok.
# NEDEN KİLİT DOSYASI, pgrep DEĞİL — ÖLÇÜLDÜ (2026-09-06): ilk sürüm
# `pgrep -f '/usr/local/bin/enderun-backup.sh'` kullanıyordu ve sonda
# sırasında SONDAYI KOŞTURAN KABUĞUN komut satırını eşleştirdi — komut
# satırında yol adı geçiyordu, o kadar. Tatbikat 30 dakika bekleyip
# düşecekti. Bir ad araması varlık ölçümü değildir.
BEKLEME_TAVANI_SN=1800
BEKLENEN=0
YEDEK_KILIDI="/var/lib/enderun-ai/yedek-kosuyor"

yedek_kosuyor_mu() {
    [ -s "$YEDEK_KILIDI" ] || return 1
    local pid
    pid="$(head -1 "$YEDEK_KILIDI" 2>/dev/null | tr -dc '0-9')"
    [ -n "$pid" ] || return 1
    # BAYAT KİLİT KİMSEYİ BEKLETMEZ: dosya duruyor ama süreç ölmüşse
    # kilit geçersizdir.
    kill -0 "$pid" 2>/dev/null
}

while yedek_kosuyor_mu; do
    if [ "$BEKLENEN" -eq 0 ]; then
        log "INFO" "Yedekleme koşuyor — bitmesi bekleniyor (tavan ${BEKLEME_TAVANI_SN}sn)."
    fi
    if [ "$BEKLENEN" -ge "$BEKLEME_TAVANI_SN" ]; then
        fail "Yedekleme $((BEKLENEN / 60)) dakikadır hâlâ koşuyor — tatbikat ESKİ yedeğe düşmüyor, DURDU."
    fi
    sleep 10
    BEKLENEN=$((BEKLENEN + 10))
done
[ "$BEKLENEN" -eq 0 ] || log "INFO" "Yedekleme bitti (${BEKLENEN}sn beklendi)."

YEDEK="$(find "$BACKUP_DIR" -maxdepth 1 -name 'db_*.dump.gpg' | sort | tail -1)"
[ -n "$YEDEK" ] || fail "Şifreli veritabanı yedeği bulunamadı."

# ── YEDEK TAZE Mİ ───────────────────────────────────────────────
#
# TATBİKATIN SORUSU "geri yükleme çalışıyor mu" DEĞİL, "BUGÜNÜN yedeği
# geri yüklenebiliyor mu"dur (Mehmet, 2026-09-06). Eski bir yedekle
# alınan yeşil, yedekleme zincirinin koptuğunu GİZLER — hatta zincir
# koptukça tatbikat aynı eski dosyayı her gece başarıyla yükleyip her
# gece yeşil vermeye devam eder.
#
# ÖLÇÜT SAAT, TAKVİM GÜNÜ DEĞİL — SEBEBİ VAR: gece yedeği 03:00'te
# alınıyor. "Bugüne ait olsun" kuralı, tatbikat gece yarısından sonra
# elle koşturulduğunda SAHTE KIRMIZI verirdi (o an en yeni yedek dün
# 03:00'ün yedeğidir ve hiçbir şey bozuk değildir). Saat cinsinden yaş
# ikisini de doğru ayırır: 26 saat, günlük döngü artı payı.
YEDEK_TAVANI_SAAT=26
YEDEK_ADI="$(basename "$YEDEK")"
YEDEK_TARIH="$(printf '%s' "$YEDEK_ADI" | sed -E 's/^db_([0-9]{8})_([0-9]{6})\..*/\1 \2/')"

if ! printf '%s' "$YEDEK_TARIH" | grep -qE '^[0-9]{8} [0-9]{6}$'; then
    fail "Yedek adından tarih okunamadı: $YEDEK_ADI"
fi

YEDEK_EPOK="$(date -d "$(printf '%s' "$YEDEK_TARIH" | sed -E 's/^(....)(..)(..) (..)(..)(..)$/\1-\2-\3 \4:\5:\6/')" +%s 2>/dev/null)"
[ -n "$YEDEK_EPOK" ] || fail "Yedek tarihi çözümlenemedi: $YEDEK_ADI"

YEDEK_YAS_SAAT=$(( ( $(date +%s) - YEDEK_EPOK ) / 3600 ))
if [ "$YEDEK_YAS_SAAT" -gt "$YEDEK_TAVANI_SAAT" ]; then
    fail "YEDEK ESKİ — en yeni yedek ${YEDEK_YAS_SAAT} saatlik ($YEDEK_ADI). Yedekleme başarısız olmuş olabilir; zincir kopuk."
fi

SATIR_DAMGASI="${YEDEK%.dump.gpg}.satirlar.txt"

# ── DAMGA YOKSA KIRMIZI — SESSİZCE GEÇMEZ ───────────────────────────
#
# BOŞ KÜME HER İDDİAYI DOĞRULAR (Kural 48). Damga olmadan
# karşılaştırılacak hiçbir şey yok; "hiçbir fark bulunamadı" demek
# "yedek doğru" demek DEĞİL. Bu yüzden eksik damga bir atlama sebebi
# değil, doğrudan başarısızlıktır.
[ -s "$SATIR_DAMGASI" ] \
    || fail "Satır damgası yok: $(basename "$SATIR_DAMGASI") — karşılaştıracak şey olmadan tatbikat GEÇEMEZ."

# ── DAMGA BU YEDEĞİN Mİ ─────────────────────────────────────────
#
# Dosya adı eşleşmesi tek başına yetmez: adlar eşleşiyor diye içerik
# bu koşudan gelmiş olmaz. Damganın başlığında hangi yedek için
# yazıldığı DURUYOR; onu okuyup karşılaştırıyoruz.
#
# Neden önemli: eski bir damgayla yeni bir yedeği karşılaştırmak SAHTE
# YEŞİL üretir — ya da sahte kırmızı. İkisi de yanlış cevap.
DAMGADAKI_YEDEK="$(grep -m1 '^# yedek' "$SATIR_DAMGASI" | sed -E 's/^# yedek[[:space:]]*:[[:space:]]*//')"
if [ "$DAMGADAKI_YEDEK" != "$YEDEK_ADI" ]; then
    fail "Damga BAŞKA bir yedeğe ait: damga '$DAMGADAKI_YEDEK', sınanan '$YEDEK_ADI'. Eşleşmeyen damgayla karşılaştırma yapılmaz."
fi

log "INFO" "Sınanan yedek: $YEDEK_ADI (${YEDEK_YAS_SAAT} saatlik)  damga: $(basename "$SATIR_DAMGASI")"

# ── KUR VE HEMEN KAPAT (A: KURAN KAPATIR) ───────────────────────────
#
# PostgreSQL'de yeni veritabanı PUBLIC'e CONNECT açık doğar ve bunu
# değiştiren bir ayar yok (ölçüldü 2026-09-06: `template1`'den geri
# almak yeni veritabanını korumuyor). Kuran kapatmazsa, açık kalır.
tatbikat_psql postgres -v ON_ERROR_STOP=1 \
    -c "DROP DATABASE IF EXISTS $PROVA_DB;" \
    -c "CREATE DATABASE $PROVA_DB;" >/dev/null 2>&1 \
    || fail "Tatbikat veritabanı kurulamadı."

tatbikat_psql postgres -v ON_ERROR_STOP=1 \
    -c "REVOKE CONNECT ON DATABASE $PROVA_DB FROM PUBLIC;" >/dev/null 2>&1 \
    || fail "Tatbikat veritabanı PUBLIC'e kapatılamadı — açık bir veritabanı bırakılmıyor."

# Şifreli yedek DOĞRUDAN borudan yükleniyor: düz ara dosya yok.
gpg --batch --quiet --passphrase-file "$BACKUP_KEY_FILE" --decrypt "$YEDEK" 2>/dev/null \
    | PGPASSWORD="$TATBIKAT_DB_PAROLASI" pg_restore -h 127.0.0.1 -p 5432 \
          -U "$TATBIKAT_DB_KULLANICI" --dbname="$PROVA_DB" \
          --no-owner --no-privileges 2>/dev/null
D=("${PIPESTATUS[@]}")
[ "${D[0]}" -eq 0 ] && [ "${D[1]}" -eq 0 ] \
    || fail "Geri yükleme BAŞARISIZ (gpg=${D[0]}, pg_restore=${D[1]})."

# ── KARŞILAŞTIRMA: GERİ YÜKLENEN KOPYA ↔ DAMGA ──────────────────────
GERI_YUKLENEN="$(tatbikat_psql "$PROVA_DB" -F'|' -c "
select t.relname,
       (xpath('/row/cnt/text()', query_to_xml(
            format('select count(*) as cnt from %I.%I', t.schemaname, t.relname),
            false, true, '')))[1]::text::bigint
from pg_stat_user_tables t
where t.schemaname = 'public'
order by t.relname;" 2>/dev/null)"

[ -n "$GERI_YUKLENEN" ] || fail "Geri yüklenen kopyadan satır sayısı okunamadı."

BEKLENEN_SAYI=$(grep -vc '^#' "$SATIR_DAMGASI")
GERCEK_SAYI=$(printf '%s\n' "$GERI_YUKLENEN" | grep -c '|')

[ "$BEKLENEN_SAYI" -gt 0 ] \
    || fail "Damga boş — karşılaştıracak tablo yok."

FARK="$(diff <(grep -v '^#' "$SATIR_DAMGASI" | sort) <(printf '%s\n' "$GERI_YUKLENEN" | sort) || true)"

if [ -n "$FARK" ]; then
    log "ERROR" "Damga ile geri yüklenen kopya UYUŞMUYOR (damga $BEKLENEN_SAYI tablo, kopya $GERCEK_SAYI tablo):"
    printf '%s\n' "$FARK" | head -20 | while IFS= read -r s; do log "ERROR" "  $s"; done
    fail "Tatbikat BAŞARISIZ — yedek, damgasının söylediğini içermiyor."
fi

SURE=$(( $(date +%s) - BASLANGIC ))
BOYUT=$(stat -c %s "$YEDEK")

log "INFO" "TATBİKAT BAŞARILI — $(basename "$YEDEK") gerçek bir veritabanına yüklendi; $BEKLENEN_SAYI tablonun satır sayısı damgayla TAM eşleşti."

# ── POZİTİF DAMGA — YOKLUĞU "KOŞMADI" DEMEKTİR ──────────────────────
#
# Yalnız başarılı koşuda yazılır; yukarıdaki `fail` çağrılarının
# herhangi biri buraya gelmeyi engeller. Damganın YAŞI, "en son ne
# zaman gerçekten geri YÜKLEYEBİLDİK" sorusunun cevabıdır — "en son ne
# zaman denedik" değil (Kural 74).
mkdir -p "$(dirname "$DAMGA_DOSYASI")"
{
    printf 'sonuc=BASARILI\n'
    printf 'zaman=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    printf 'sure_sn=%s\n' "$SURE"
    printf 'yedek=%s\n' "$(basename "$YEDEK")"
    printf 'boyut_bayt=%s\n' "$BOYUT"
    printf 'tablo=%s\n' "$BEKLENEN_SAYI"
} > "$DAMGA_DOSYASI"
chmod 644 "$DAMGA_DOSYASI"
log "INFO" "Başarı damgası yazıldı: $DAMGA_DOSYASI (${SURE}sn, $(numfmt --to=iec "$BOYUT"))"
