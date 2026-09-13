#!/usr/bin/env bash
#
# ═══════════════════════════════════════════════════════════════════
# CANLIYA GERİ YÜKLEME — FELAKET ANININ YAZILI YORDAMI (PAKET C)
# ═══════════════════════════════════════════════════════════════════
#
# ═══ NEDEN VAR ═══
#
# YEDEK/1 (2026-09-04) ölçtü: yedek alınıyor, şifreleniyor, her gece
# geri yükleme tatbikatı koşuyor — ama CANLIYI geri yükleyen bir betik
# YOKTU. Mekanik biliniyordu, adımlar yazılı değildi. Bir taslak
# 2026-09-05'te yazılıp geçici dizinde bırakıldı ve sunucu yeniden
# başlayınca kayboldu (Kural 73). Bu sefer depoya giriyor.
#
# Felaket anında hatırlanacak şey yoktur: elinde ya yazılı yordam
# vardır ya da yoktur.
#
# ═══ EN TEHLİKELİ BETİK BU ═══
#
# Bu betik canlı veritabanını DÜŞÜRÜR. Bu yüzden varsayılanı yoktur,
# tahmin etmez, ve iki bağımsız onay olmadan canlıya dokunmaz.
#
#   · hedef HER ZAMAN açıkça verilir (`--hedef`), varsayılan YOK
#   · hedef canlıysa AYRICA ortam değişkeni onayı şart:
#         KURTARMA_ONAYI="EVET-<hedef>-GERI-YUKLE"
#   · canlıya dokunmadan ÖNCE mevcut hâlin dökümü alınır (geri dönüş
#     kopyası). Seçilen yedek yanlış çıkarsa dönülecek yer budur.
#   · yedek, KENDİ damgasıyla eşleşmiyorsa yüklenmez
#   · yükleme sonrası satır sayıları damgayla TAM karşılaştırılır
#
# ═══ NEDEN `sudo -u postgres` ═══
#
# Gece tatbikatı bilerek süperkullanıcı kullanmıyor (`enderun_tatbikat`
# kimliği, TCP+parola) — çünkü onun canlıya dokunmaya hakkı yok.
# KURTARMA farklı bir iştir: `enderun_ai`nin sahibi `enderun_user` ve
# bir veritabanını DÜŞÜRÜP KURMAK için süperkullanıcı gerekir. Bu
# betik o yetkiyi bilerek kullanır ve bu yüzden yukarıdaki onay
# katmanları var.
#
# ═══ KULLANIM ═══
#
#   # prova (hiçbir servise dokunmaz):
#   scripts/enderun-kurtarma.sh --yedek /var/backups/enderun/db_....dump.gpg \
#                               --hedef enderun_kurtarma_provasi
#
#   # canlı (felaket anı):
#   KURTARMA_ONAYI="EVET-enderun_ai-GERI-YUKLE" \
#   scripts/enderun-kurtarma.sh --yedek <dosya> --hedef enderun_ai
#
set -uo pipefail

CANLI_DB="enderun_ai"
CANLI_SAHIP="enderun_user"
BACKUP_DIR="/var/backups/enderun"
ANAHTAR="/etc/enderunai/backup-key"
SERVISLER=(enderunai-backend enderunai-frontend)
KOK="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GOC_DIZINI="$KOK/backend/EnderunAI.Api/Migrations"
BASLANGIC=$(date +%s)

log()  { printf '%s [%s] KURTARMA: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$1" "$2"; }
bilgi(){ log INFO  "$1"; }
dur()  { log ERROR "$1"; exit 1; }

YEDEK=""; HEDEF=""
while [ $# -gt 0 ]; do
    case "$1" in
        --yedek) YEDEK="${2:?--yedek için dosya gerekli}"; shift 2 ;;
        --hedef) HEDEF="${2:?--hedef için veritabanı adı gerekli}"; shift 2 ;;
        *) dur "bilinmeyen seçenek: $1" ;;
    esac
done

[ -n "$YEDEK" ] || dur "--yedek verilmedi. Bu betiğin varsayılanı YOKTUR."
[ -n "$HEDEF" ] || dur "--hedef verilmedi. Bu betiğin varsayılanı YOKTUR."

# ── KATMAN 0: BAKIM VERİTABANLARI ───────────────────────────────────
case "$HEDEF" in
    postgres|template0|template1) dur "'$HEDEF' bir bakım veritabanı. Dokunulmaz." ;;
esac

CANLIYA=0
if [ "$HEDEF" = "$CANLI_DB" ]; then
    CANLIYA=1
    BEKLENEN_ONAY="EVET-${CANLI_DB}-GERI-YUKLE"
    [ "${KURTARMA_ONAYI:-}" = "$BEKLENEN_ONAY" ] \
        || dur "CANLI hedef ('$HEDEF') ama onay yok. Gereken: KURTARMA_ONAYI=\"$BEKLENEN_ONAY\""
    log WARN "CANLI GERİ YÜKLEME ONAYLANDI — '$HEDEF' DÜŞÜRÜLECEK."
else
    bilgi "PROVA kipi: hedef '$HEDEF' canlı değil; servislere DOKUNULMAYACAK."
fi

# ── ÖN DENETİM ──────────────────────────────────────────────────────
[ -s "$YEDEK" ]    || dur "yedek dosyası yok ya da boş: $YEDEK"
[ -s "$ANAHTAR" ]  || dur "şifreleme anahtarı yok: $ANAHTAR"

YEDEK_ADI="$(basename "$YEDEK")"
DAMGA="${YEDEK%.dump.gpg}.satirlar.txt"
[ -s "$DAMGA" ] || dur "satır damgası yok: $(basename "$DAMGA") — karşılaştırılamayan yedek YÜKLENMEZ."

DAMGADAKI="$(grep -m1 '^# yedek' "$DAMGA" | sed -E 's/^# yedek[[:space:]]*:[[:space:]]*//')"
[ "$DAMGADAKI" = "$YEDEK_ADI" ] \
    || dur "damga BAŞKA yedeğe ait (damga '$DAMGADAKI', verilen '$YEDEK_ADI'). Eşleşmeyen çiftle yükleme yapılmaz."

# Açılıyor mu — yüklemeye başlamadan önce. Yarım açılan bir dosyayla
# canlıyı düşürmüş olmak, felaketin üstüne felakettir.
# `head -c 5 | grep` KULLANILMAZ: `head` boruyu kapatınca `gpg` SIGPIPE
# ile ölür ve `pipefail` açıkken boru, grep eşleşse bile başarısız sayılır
# (2026-09-13'te bu betiğin ilk provasında YANLIŞ KIRMIZI üretti).
# Bayt dizgesi önce değişkene alınır, karar ondan sonra verilir.
BAS="$(gpg --batch --quiet --passphrase-file "$ANAHTAR" --decrypt "$YEDEK" 2>/dev/null | head -c 5 || true)"
[ "$BAS" = "PGDMP" ] \
    || dur "yedek AÇILAMADI ya da PGDMP başlığı yok: $YEDEK_ADI"

BOYUT=$(stat -c %s "$YEDEK")
BOS=$(df --output=avail -B1 "$BACKUP_DIR" | tail -1)
[ "$BOS" -gt $(( BOYUT * 20 )) ] \
    || dur "disk dar: yedek $(numfmt --to=iec "$BOYUT"), boş $(numfmt --to=iec "$BOS")."
bilgi "ön denetim geçti: $YEDEK_ADI ($(numfmt --to=iec "$BOYUT")), damga $(grep -vc '^#' "$DAMGA") tablo"

# ── GERİ DÖNÜŞ KOPYASI — CANLIYI DÜŞÜRMEDEN ÖNCE ────────────────────
#
# Seçilen yedek yanlışsa (yanlış gün, eksik tablo) dönülecek tek yer
# budur. Alınamazsa DEVAM EDİLMEZ: geri dönüşü olmayan bir yıkım,
# kurtarma değildir.
if [ "$CANLIYA" = "1" ]; then
    DONUS="${BACKUP_DIR}/kurtarma-oncesi_$(date -u +%Y%m%d_%H%M%S).dump.gpg"
    bilgi "geri dönüş kopyası alınıyor: $(basename "$DONUS")"
    sudo -u postgres pg_dump -d "$CANLI_DB" -F c 2>/dev/null \
        | gpg --batch --quiet --symmetric --cipher-algo AES256 \
              --passphrase-file "$ANAHTAR" --output "$DONUS" 2>/dev/null
    D=("${PIPESTATUS[@]}")
    { [ "${D[0]}" -eq 0 ] && [ "${D[1]}" -eq 0 ]; } \
        || { rm -f "$DONUS"; dur "geri dönüş kopyası ALINAMADI (pg_dump=${D[0]}, gpg=${D[1]}) — canlıya DOKUNULMADI."; }
    DBAS="$(gpg --batch --quiet --passphrase-file "$ANAHTAR" --decrypt "$DONUS" 2>/dev/null | head -c 5 || true)"
    [ "$DBAS" = "PGDMP" ] \
        || { rm -f "$DONUS"; dur "geri dönüş kopyası açılamadı — canlıya DOKUNULMADI."; }
    chmod 600 "$DONUS"
    bilgi "geri dönüş kopyası hazır ve açıldığı doğrulandı: $(basename "$DONUS")"
fi

# ── SERVİSLERİ DURDUR ───────────────────────────────────────────────
if [ "$CANLIYA" = "1" ]; then
    for s in "${SERVISLER[@]}"; do
        systemctl stop "$s" || dur "servis durdurulamadı: $s"
        bilgi "durduruldu: $s"
    done
else
    bilgi "(prova) atlanan adım: systemctl stop ${SERVISLER[*]}"
fi

# ── BAĞLANTILARI KES, DÜŞÜR, KUR ────────────────────────────────────
sudo -u postgres psql -v ON_ERROR_STOP=1 -q -d postgres \
    -c "select pg_terminate_backend(pid) from pg_stat_activity where datname = '$HEDEF' and pid <> pg_backend_pid();" \
    >/dev/null 2>&1 || bilgi "uyarı: bağlantı kesme sorgusu sonuç vermedi (bağlantı olmayabilir)"

sudo -u postgres psql -v ON_ERROR_STOP=1 -q -d postgres \
    -c "DROP DATABASE IF EXISTS \"$HEDEF\";" \
    -c "CREATE DATABASE \"$HEDEF\" OWNER \"$CANLI_SAHIP\";" >/dev/null 2>&1 \
    || dur "veritabanı düşürülüp kurulamadı: $HEDEF"
bilgi "veritabanı yeniden kuruldu: $HEDEF (sahip $CANLI_SAHIP)"

# ── YÜKLE — DÜZ ARA DOSYA YOK ───────────────────────────────────────
# `--role` ŞART, SONRADAN DEVİR DEĞİL (2026-09-13 provasında ölçüldü).
# `--no-owner` ile yüklenen nesneler BAĞLANAN KULLANICIYA ait olur; burada
# o `postgres`tur ve 242 tablo `enderun_user` yerine `postgres`a geçmişti.
# `reassign owned by current_user` ise çare DEĞİL — PostgreSQL süperkullanıcı
# nesnelerinin devrini reddediyor ("required by the database system").
# `--role` yükleme boyunca `SET ROLE` yapar; sahiplik baştan doğru olur.
gpg --batch --quiet --passphrase-file "$ANAHTAR" --decrypt "$YEDEK" 2>/dev/null \
    | sudo -u postgres pg_restore --dbname="$HEDEF" --role="$CANLI_SAHIP" \
          --no-owner --no-privileges 2>/dev/null
D=("${PIPESTATUS[@]}")
{ [ "${D[0]}" -eq 0 ] && [ "${D[1]}" -eq 0 ]; } \
    || dur "GERİ YÜKLEME BAŞARISIZ (gpg=${D[0]}, pg_restore=${D[1]})."

bilgi "yükleme bitti"

# ── DOĞRULAMA 1: SATIR SAYILARI DAMGAYLA ────────────────────────────
GERI="$(sudo -u postgres psql -Aqt -F'|' -d "$HEDEF" -c "
select t.relname,
       (xpath('/row/cnt/text()', query_to_xml(
            format('select count(*) as cnt from %I.%I', t.schemaname, t.relname),
            false, true, '')))[1]::text::bigint
from pg_stat_user_tables t
where t.schemaname = 'public'
order by t.relname;" 2>/dev/null)"
[ -n "$GERI" ] || dur "yüklenen kopyadan satır sayısı okunamadı."

FARK="$(diff <(grep -v '^#' "$DAMGA" | sort) <(printf '%s\n' "$GERI" | sort) || true)"
if [ -n "$FARK" ]; then
    log ERROR "damga ile yüklenen kopya UYUŞMUYOR:"
    printf '%s\n' "$FARK" | head -20 | while IFS= read -r s; do log ERROR "  $s"; done
    [ "$CANLIYA" = "1" ] && log ERROR "GERİ DÖNÜŞ KOPYASI: $DONUS"
    dur "yedek, damgasının söylediğini içermiyor — SERVİSLER KALDIRILMADI."
fi
bilgi "doğrulama 1 geçti: $(grep -vc '^#' "$DAMGA") tablonun satır sayısı damgayla TAM eşleşti"

# ── DOĞRULAMA 2: GÖÇ GEÇMİŞİ ────────────────────────────────────────
#
# KAPSAM UYARISI (Kural 84): göç dosyaları TEK klasörde değil —
# `Migrations/` ve `Migrations/HumanResources/` ikisi de sayılır.
# `Migrations/*.cs` diye aramak 7 İK göçünü görmez ve "canlıda fazla
# satır var" diye YANLIŞ alarm üretir (2026-09-13'te üretti).
KOD_GOC="$(find "$GOC_DIZINI" -name '*.cs' ! -name '*.Designer.cs' ! -name '*ModelSnapshot.cs' -printf '%f\n' 2>/dev/null | sed 's/\.cs$//' | sort)"
VT_GOC="$(sudo -u postgres psql -Aqt -d "$HEDEF" -c 'select "MigrationId" from "__EFMigrationsHistory" order by 1' 2>/dev/null)"

if [ -z "$KOD_GOC" ] || [ -z "$VT_GOC" ]; then
    log WARN "göç geçmişi karşılaştırılamadı (kod ya da veritabanı listesi boş) — ÖLÇEMEDİ."
else
    YETIM="$(comm -23 <(printf '%s\n' "$VT_GOC") <(printf '%s\n' "$KOD_GOC"))"
    BEKLEYEN="$(comm -13 <(printf '%s\n' "$VT_GOC") <(printf '%s\n' "$KOD_GOC"))"
    [ -n "$YETIM" ] && { log WARN "veritabanında olup kodda OLMAYAN göç:"; printf '%s\n' "$YETIM" | while read -r g; do log WARN "  $g"; done; }
    if [ -n "$BEKLEYEN" ]; then
        log WARN "kodda olup veritabanında olmayan göç (yedek koddan ESKİ):"
        printf '%s\n' "$BEKLEYEN" | while read -r g; do log WARN "  $g"; done
        log WARN "SONRAKİ ADIM: deploy/scripts/goc-uygula.sh ile göçleri uygulayın."
    fi
    [ -z "$YETIM" ] && [ -z "$BEKLEYEN" ] && bilgi "doğrulama 2 geçti: göç geçmişi kodla birebir ($(printf '%s\n' "$VT_GOC" | wc -l) göç)"
fi

# ── DOĞRULAMA 3: SAHİPLİK ───────────────────────────────────────────
#
# "Yüklendi ve satırlar doğru" yetmiyor: nesneler yanlış role aitse
# uygulama kendi tablosuna yazamaz. Felaketin üstüne felaket bu olurdu.
# Fail-closed: sahiplik yanlışsa servisler KALDIRILMAZ.
YABANCI="$(sudo -u postgres psql -Aqt -d "$HEDEF" -c "
    select tableowner||' ('||count(*)||' tablo)'
    from pg_tables where schemaname='public' and tableowner <> '$CANLI_SAHIP'
    group by tableowner;" 2>/dev/null)"
if [ -n "$YABANCI" ]; then
    log ERROR "sahiplik YANLIŞ — beklenen sahip '$CANLI_SAHIP', bulunan:"
    printf '%s\n' "$YABANCI" | while IFS= read -r s; do log ERROR "  $s"; done
    [ "$CANLIYA" = "1" ] && log ERROR "GERİ DÖNÜŞ KOPYASI: $DONUS"
    dur "yanlış sahiplikle servisler KALDIRILMADI."
fi
bilgi "doğrulama 3 geçti: public şemadaki tüm tablolar '$CANLI_SAHIP' sahipliğinde"

# ── SERVİSLERİ KALDIR + SAĞLIK ──────────────────────────────────────
if [ "$CANLIYA" = "1" ]; then
    for s in "${SERVISLER[@]}"; do
        systemctl start "$s" || dur "servis kaldırılamadı: $s — GERİ DÖNÜŞ KOPYASI: $DONUS"
        bilgi "kaldırıldı: $s"
    done
    if [ -x "$KOK/deploy/scripts/healthcheck.sh" ]; then
        bash "$KOK/deploy/scripts/healthcheck.sh" || dur "SAĞLIK KONTROLÜ DÜŞTÜ — GERİ DÖNÜŞ KOPYASI: $DONUS"
        bilgi "sağlık kontrolü geçti"
    else
        log WARN "healthcheck.sh bulunamadı — sağlık ÖLÇEMEDİ."
    fi
else
    bilgi "(prova) atlanan adım: systemctl start ${SERVISLER[*]} + healthcheck.sh"
fi

bilgi "KURTARMA TAMAM — hedef '$HEDEF', yedek '$YEDEK_ADI', süre $(( $(date +%s) - BASLANGIC ))sn"
[ "$CANLIYA" = "1" ] && bilgi "geri dönüş kopyası duruyor: $DONUS (işler yolundaysa siz silin)"
exit 0
