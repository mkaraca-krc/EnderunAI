#!/usr/bin/env bash
#
# EnderunAI güvenli yayın (safe deploy) scripti.
#
# Akış: git pull -> test kapsamı tespiti -> backend testleri (geçmezse
#       DUR) -> frontend testleri (geçmezse DUR) -> dotnet publish ->
#       npm run build -> veritabanı yedeği -> servisleri restart ->
#       30 sn içinde sağlık kontrolü -> sağlıksızsa ÖNCEKİ sürüme otomatik
#       geri dön.
#
# HIZLI YOL: son başarılı yayından bu yana değişen dosyaların TAMAMI
# frontend/enderun-ai/ altındaysa backend xUnit turu atlanır. Frontend
# testleri, build ve sağlık kontrolü her durumda koşar. Kapı
# zayıflamıyor: yalnızca değişmediği KANITLANMIŞ katmanın testi
# atlanıyor. Herhangi bir backend/migration/script/belge dosyası
# değiştiyse ya da tespit belirsizse TAM tur koşar.
#
# Testler geçmeden hiçbir servise dokunulmaz; testler geçmezse repo'daki
# yeni kod bile publish edilmez, canlı sürüm olduğu gibi çalışmaya devam
# eder. NOT: Bu script yeni EF Core migration'larını canlı veritabanına
# OTOMATİK uygulamaz — migration içeren bir değişiklik yayınlanıyorsa,
# yedek aldıktan sonra göçler hâlâ ELLE uygulanmalı (kasıtlı tercih).
#
# İKİ BAĞLAM VAR, --context ZORUNLU. Bayrak olmadan komut
# "More than one DbContext was found" ile durur:
#     dotnet ef database update --project backend/EnderunAI.Api \
#         --context AppDbContext
#     dotnet ef database update --project backend/EnderunAI.Api \
#         --context HrDbContext
#
# Betik UYGULAMAZ ama artık DOĞRULAR: `gocleri_dogrula` kapısı, iki
# bağlamın da güncel olduğunu görmeden yayına devam etmez.

set -uo pipefail

REPO_ROOT="/var/www/enderun-ai"
BACKEND_DIR="${REPO_ROOT}/backend/EnderunAI.Api"
BACKEND_TEST_PROJECT="${REPO_ROOT}/backend/EnderunAI.Api.Tests/EnderunAI.Api.Tests.csproj"
FRONTEND_DIR="${REPO_ROOT}/frontend/enderun-ai"

BACKEND_PUBLISH_DIR="${REPO_ROOT}/publish"
BACKEND_ROLLBACK_DIR="${REPO_ROOT}/publish-rollback"
FRONTEND_NEXT_DIR="${FRONTEND_DIR}/.next"
FRONTEND_NEXT_ROLLBACK_DIR="${REPO_ROOT}/frontend-next-rollback"

# YAYIN DALI SABİT.
#
# Bu betik önce `git rev-parse --abbrev-ref HEAD` ile HANGİ DALDAYSA
# onu yayınlıyordu; dal sabitlemesi yoktu. Yanlış bir `git checkout`
# ya da yarım kalmış bir deneme dalı, hiçbir engel olmadan canlıya
# çıkardı.
#
# Ortam değişkeniyle geçilebilir (`DEPLOY_BRANCH=... safe-deploy.sh`)
# ama geçmek BİLİNÇLİ bir hareket olsun diye varsayılan sabit.
DEPLOY_BRANCH="${DEPLOY_BRANCH:-main}"

ENV_FILE="/etc/enderunai/backend.env"
LOG_FILE="/var/log/enderun-deploy.log"

# Son BAŞARIYLA yayınlanan commit. Hızlı yolun tabanı budur.
#
# NEDEN "pull öncesi HEAD" DEĞİL: bu depoda değişiklik çoğu zaman
# yerelde commit edilip sonra deploy ediliyor, yani `git pull` no-op
# oluyor. Pull öncesi/sonrası farkına bakan bir tespit BOŞ diff görür
# ve backend değişmiş olsa bile "frontend-only" der — kapıyı tam da
# önemli olduğu anda açardı.
#
# Git ağacının DIŞINDA tutuluyor: içeride olsaydı her yayından sonra
# ağaç kirlenir ve require_clean_git_tree bir sonraki yayını
# reddederdi.
DEPLOY_STATE_DIR="/var/lib/enderun-ai"
LAST_DEPLOYED_COMMIT_FILE="${DEPLOY_STATE_DIR}/last-deployed-commit"

# Yalnızca bu önekin altındaki dosyalar backend testlerinden
# provably bağımsız sayılır. Bilerek DAR: her istisna, ileride
# birinin yanlış yere koyduğu bir dosyanın kapıyı sessizce
# atlatacağı bir yer açar. Depo kökündeki .md dosyaları bile tam tur
# tetikler — belge değişikliğini ayrı commit'lemek, listeyi
# genişletmekten ucuzdur.
FRONTEND_PATH_PREFIX="frontend/enderun-ai/"

HEALTH_CHECK_TIMEOUT_SECONDS=30
HEALTH_CHECK_INTERVAL_SECONDS=2

START_TIME="$(date +%s)"
DEPLOY_OUTCOME="UNKNOWN"
YARIM_KOSU_DOSYASI="${DEPLOY_STATE_DIR}/yarim-kosu"
YARIM_KOSU_ONAYLANDI="${YARIM_KOSU_ONAYLANDI:-}"
ASAMA="baslangic"

log() {
    echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) [$1] $2" | tee -a "$LOG_FILE"
}

fail() {
    log "ERROR" "$1"
    yarim_kosu_kendi_izini_sil
    print_summary
    exit 1
}

print_summary() {
    local elapsed=$(( $(date +%s) - START_TIME ))
    echo ""
    echo "================= SAFE-DEPLOY ÖZET ================="
    echo "Sonuç       : ${DEPLOY_OUTCOME}"
    echo "Test turu   : ${TEST_SCOPE:-full} (${TEST_SCOPE_REASON:-belirlenmedi})"
    echo "Süre        : ${elapsed}s"
    echo "Git commit  : $(cd "$REPO_ROOT" && git rev-parse --short HEAD 2>/dev/null || echo '-')"
    echo "Log dosyası : ${LOG_FILE}"
    echo "======================================================"
}

# ─────────────────────────────────────────────────────────────────
# YARIM KOŞU TESPİTİ
#
# safe-deploy bu oturumda ÜÇ KEZ dışarıdan öldürüldü ve hiçbiri iz
# bırakmadı. İz bırakmamak asıl sorun değil; asıl sorun şu:
#
#   backup_current_release, publish/ dizinini publish-rollback/
#   üzerine kopyalıyor. Bir koşu YAYINLAMA sırasında ölürse publish/
#   YARIM kalır. Bir sonraki koşu ilk iş olarak o yarım dizini
#   SAĞLAM geri-alma kopyasının üzerine yazar.
#
#   Sonuç: geri dönülecek yer kalmaz. Üstelik sessizce olur — yayın
#   başarılı görünür, eksiklik ancak geri alma gerektiğinde çıkar.
#
# Bu yüzden işaret dosyası TEST aşamasında zararsız, YAYINLAMA
# aşamasından sonra durdurucu.
# ─────────────────────────────────────────────────────────────────

# Bu aşamada ölmüş bir koşu, bir sonraki koşunun geri-alma kopyasını
# bozar mı?
# GÜVENLİ AŞAMALAR SAYILIR, TEHLİKELİLER DEĞİL.
#
# İlk yazılışı tersiydi: tehlikeli aşamalar sayılıyor, gerisi güvenli
# kabul ediliyordu. Test bunu yakaladı — tanınmayan bir aşama adı
# ("bilinmiyor", boş dize, ileride eklenen yeni bir aşama) AÇIK
# tarafa düşüyordu.
#
# Aşama adı bilinmiyorsa koşunun nerede öldüğü de bilinmiyordur;
# orada devam etmek tam da korunmak istenen durumu serbest bırakır.
# Yeni bir aşama eklendiğinde varsayılan artık "dur" — listeye
# yazılmadığı sürece geçmez.
yarim_kosu_tehlikeli_mi() {
    case "$1" in
        baslangic|backend-testleri|on-yuz-testleri|surum-yedegi)
            return 1 ;;
        *)
            return 0 ;;
    esac
}

# SAF KARAR — dosya sistemine, PID'e, ortama bakmaz.
#
# Ayrı tutulmasının sebebi: karar dosya varlığıyla iç içe olsaydı,
# testin onu sürebilmesi için sahte bir işaret dosyası kurması
# gerekirdi; o zaman test kararı değil dosya kurulumunu sınardı.
yarim_kosu_karari() {
    local asama_adi="$1"
    local onay="${2:-}"

    if ! yarim_kosu_tehlikeli_mi "$asama_adi"; then
        echo "devam"
    elif [ "$onay" = "evet" ]; then
        echo "devam-onayli"
    else
        echo "dur"
    fi
}

asama() {
    ASAMA="$1"
    mkdir -p "$DEPLOY_STATE_DIR" 2>/dev/null || true
    printf '%s\n%s\n%s\n' \
        "$$" "$ASAMA" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
        > "$YARIM_KOSU_DOSYASI" 2>/dev/null || true
}

# YALNIZ KENDİ izini siler. Başka bir koşunun işaretini silmek,
# gerçekten paralel çalışan bir yayını görünmez yapardı.
yarim_kosu_kendi_izini_sil() {
    [ -f "$YARIM_KOSU_DOSYASI" ] || return 0
    [ "$(sed -n 1p "$YARIM_KOSU_DOSYASI" 2>/dev/null)" = "$$" ] || return 0
    rm -f "$YARIM_KOSU_DOSYASI"
}

yarim_kosu_denetle() {
    [ -f "$YARIM_KOSU_DOSYASI" ] || return 0

    local onceki_pid onceki_asama onceki_zaman
    onceki_pid="$(sed -n 1p "$YARIM_KOSU_DOSYASI" 2>/dev/null)"
    onceki_asama="$(sed -n 2p "$YARIM_KOSU_DOSYASI" 2>/dev/null)"
    onceki_zaman="$(sed -n 3p "$YARIM_KOSU_DOSYASI" 2>/dev/null)"

    if [ -n "$onceki_pid" ] && [ "$onceki_pid" != "$$" ] \
       && kill -0 "$onceki_pid" 2>/dev/null; then
        fail "Başka bir safe-deploy ŞU AN ÇALIŞIYOR (PID ${onceki_pid}, aşama: ${onceki_asama:-bilinmiyor}). İki yayın aynı anda çalışamaz."
    fi

    log "WARN" "ÖNCEKİ KOŞU YARIM KALDI — aşama: ${onceki_asama:-bilinmiyor}, zaman: ${onceki_zaman:-bilinmiyor}"

    case "$(yarim_kosu_karari "${onceki_asama:-bilinmiyor}" "$YARIM_KOSU_ONAYLANDI")" in
        devam)
            log "INFO" "O aşama zararsız (yayınlama başlamamıştı) — devam ediliyor."
            rm -f "$YARIM_KOSU_DOSYASI"
            ;;
        devam-onayli)
            log "WARN" "YARIM_KOSU_ONAYLANDI=evet verildi — durdurulmadı. Geri-alma kopyasının sağlamlığı ARTIK DOĞRULANMIŞ SAYILMIYOR."
            rm -f "$YARIM_KOSU_DOSYASI"
            ;;
        *)
            log "ERROR" "publish-rollback/ şu an güvenilmez olabilir; bu koşu onu YARIM bir publish/ ile üzerine yazardı."
            log "ERROR" "Elle bakın: ls -la ${BACKEND_PUBLISH_DIR} ${BACKEND_ROLLBACK_DIR}"
            log "ERROR" "Sağlam olduğuna karar verirseniz: YARIM_KOSU_ONAYLANDI=evet ile tekrar çalıştırın."
            fail "Önceki yayın '${onceki_asama}' aşamasında yarım kaldı — bu koşu durduruldu."
            ;;
    esac
}

require_clean_git_tree() {
    cd "$REPO_ROOT" || fail "Repo dizinine gidilemedi: $REPO_ROOT"

    if [ -n "$(git status --porcelain)" ]; then
        fail "Repo'da commit edilmemiş değişiklikler var — güvenli yayın için önce commit/stash yapın."
    fi
}

resolve_test_db_connection() {
    if [ -n "${TEST_DB_CONNECTION:-}" ]; then
        return
    fi

    if [ ! -f "$ENV_FILE" ]; then
        fail "Ortam değişkeni dosyası bulunamadı: $ENV_FILE"
    fi

    local live_connection
    live_connection="$(grep -E '^DB_CONNECTION=' "$ENV_FILE" | sed -E "s/^DB_CONNECTION=//" | tr -d \'\")"
    [ -z "$live_connection" ] && fail "DB_CONNECTION okunamadı."

    export TEST_DB_CONNECTION="${live_connection//Database=enderun_ai;/Database=enderun_ai_test;}"
    export DB_CONNECTION="$TEST_DB_CONNECTION"
    export JWT_SECRET="deploy-script-test-jwt-secret-0123456789"
}

#
# Değişen yol listesini sınıflandırır.
#
# stdin: satır başına bir yol. Çıktı: "frontend-only" ya da "full".
#
# BOŞ GİRDİ "full" DÖNER. Boş bir liste "değişiklik yok" da olabilir,
# "tabanı bulamadım" da; ikisini burada ayırt edemeyiz ve belirsizlik
# tam tur demektir.
#
# Ayrı bir fonksiyon çünkü tek başına test edilebilir olması gerekiyor;
# kapıyı gevşeten bir mantığın doğruluğu "okuyunca mantıklı görünüyor"
# ile bırakılamaz.
classify_changed_paths() {
    local path
    local saw_any=0

    # `|| [ -n "$path" ]`: son satırın sonunda yeni satır yoksa `read`
    # hata döner ama değişkeni DOLDURUR. Bu olmadan girdinin son satırı
    # sessizce düşüyordu — testte yakalandı: frontend+backend karışık
    # bir commit'te backend satırı son sıradaysa sonuç "frontend-only"
    # çıkıyor ve backend testleri hiç koşmadan yayın yapılıyordu.
    while IFS= read -r path || [ -n "$path" ]; do
        [ -z "$path" ] && continue
        saw_any=1

        case "$path" in
            "${FRONTEND_PATH_PREFIX}"*) ;;
            *)
                echo "full"
                return 0
                ;;
        esac
    done

    if [ "$saw_any" -eq 0 ]; then
        echo "full"
        return 0
    fi

    echo "frontend-only"
}

#
# Bu yayında backend testlerinin atlanıp atlanamayacağını belirler.
# Sonucu global TEST_SCOPE değişkenine yazar: "full" | "frontend-only".
#
# Her belirsizlikte "full": taban dosyası yok, taban commit'i bu
# depoda tanınmıyor, git komutu hata verdi, ya da diff boş.
resolve_test_scope() {
    TEST_SCOPE="full"
    TEST_SCOPE_REASON="varsayılan: tam tur"

    if [ ! -f "$LAST_DEPLOYED_COMMIT_FILE" ]; then
        TEST_SCOPE_REASON="son yayın kaydı yok (ilk çalıştırma)"
        return
    fi

    local baseline
    baseline="$(tr -d '[:space:]' < "$LAST_DEPLOYED_COMMIT_FILE")"

    if [ -z "$baseline" ]; then
        TEST_SCOPE_REASON="son yayın kaydı boş"
        return
    fi

    # Taban commit bu depoda gerçekten var mı? Force-push ya da
    # rebase sonrası olmayabilir; yoksa diff anlamsızdır.
    if ! git cat-file -e "${baseline}^{commit}" 2>/dev/null; then
        TEST_SCOPE_REASON="son yayın commit'i (${baseline:0:8}) depoda bulunamadı"
        return
    fi

    local changed
    if ! changed="$(git diff --name-only --no-renames "$baseline" HEAD 2>/dev/null)"; then
        TEST_SCOPE_REASON="git diff başarısız"
        return
    fi

    local verdict
    verdict="$(printf '%s\n' "$changed" | classify_changed_paths)"

    local count
    count="$(printf '%s\n' "$changed" | grep -c . || true)"

    if [ "$verdict" = "frontend-only" ]; then
        TEST_SCOPE="frontend-only"
        TEST_SCOPE_REASON="${count} dosyanın tamamı ${FRONTEND_PATH_PREFIX} altında"
    elif [ "$count" -eq 0 ]; then
        # Diff boş: ya gerçekten değişiklik yok ya da taban beklediğimiz
        # yerde değil. İkisini ayırt edemiyoruz, o yüzden tam tur.
        TEST_SCOPE_REASON="taban ile HEAD arasında değişiklik görünmüyor"
    else
        TEST_SCOPE_REASON="${count} değişen dosyadan en az biri frontend dışında"
    fi

    # Karar denetlenebilir olsun: hangi dosyalara bakılarak verildiği
    # günlükte dursun.
    log "INFO" "Değişen dosyalar (${baseline:0:8}..HEAD):"
    printf '%s\n' "$changed" | sed 's/^/    /' | tee -a "$LOG_FILE"
}

record_successful_deploy() {
    mkdir -p "$DEPLOY_STATE_DIR" 2>/dev/null
    if git rev-parse HEAD > "$LAST_DEPLOYED_COMMIT_FILE" 2>/dev/null; then
        log "INFO" "Son yayın kaydı güncellendi: $(git rev-parse --short HEAD)"
    else
        # Kayıt yazılamazsa bir sonraki yayın tam tur koşar; bu
        # güvenli taraf, o yüzden yayını düşürmüyoruz.
        log "WARN" "Son yayın kaydı yazılamadı; sonraki yayın TAM tur koşacak."
    fi
}

run_backend_tests() {
    if [ "${TEST_SCOPE:-full}" = "frontend-only" ]; then
        log "INFO" "Backend testleri ATLANDI — ${TEST_SCOPE_REASON}."
        log "INFO" "Frontend testleri, build ve sağlık kontrolü yine koşuyor."
        return
    fi

    log "INFO" "Backend testleri çalıştırılıyor..."
    resolve_test_db_connection

    # TEST KOŞUCU ÜZERİNDEN (2026-08-26): tek örnek, kendi cgroup'u,
    # bellek tavanı. Doğrudan çağrı, durdurulduğunda ardında 4,5 GB'lık
    # yetim Roslyn süreci bırakıyordu ve ikinci koşu makineyi OOM'a
    # sokuyordu — bir oturumda üç kez. Bkz. scripts/derleme-kos.sh.
    if "${REPO_ROOT}/scripts/derleme-kos.sh" \
            dotnet test "$BACKEND_TEST_PROJECT" --configuration Release 2>&1 | tee -a "$LOG_FILE"; then
        log "INFO" "Backend testleri geçti."
    else
        fail "Backend testleri BAŞARISIZ. Yayın DURDURULDU, hiçbir servise dokunulmadı."
    fi
}

run_frontend_tests() {
    log "INFO" "Frontend testleri çalıştırılıyor..."

    # Test betiği yoksa DUR: harness kurulduktan sonra sessizce
    # kaybolması, kapının açık kaldığını kimseye söylemeden değeri
    # yarıya indirirdi.
    if ! (cd "$FRONTEND_DIR" && npm run --silent test) 2>&1 | tee -a "$LOG_FILE"; then
        fail "Frontend testleri BAŞARISIZ. Yayın DURDURULDU, hiçbir servise dokunulmadı."
    fi

    log "INFO" "Frontend testleri geçti."
}

backup_current_release() {
    log "INFO" "Mevcut sürüm rollback için yedekleniyor..."

    rm -rf "$BACKEND_ROLLBACK_DIR"
    if [ -d "$BACKEND_PUBLISH_DIR" ]; then
        cp -a "$BACKEND_PUBLISH_DIR" "$BACKEND_ROLLBACK_DIR"
    fi

    rm -rf "$FRONTEND_NEXT_ROLLBACK_DIR"
    if [ -d "$FRONTEND_NEXT_DIR" ]; then
        cp -a "$FRONTEND_NEXT_DIR" "$FRONTEND_NEXT_ROLLBACK_DIR"
    fi

    log "INFO" "Rollback yedeği hazır: ${BACKEND_ROLLBACK_DIR}, ${FRONTEND_NEXT_ROLLBACK_DIR}"
}

#
# GÖÇ KAPISI — İKİ BAĞLAM DA GÜNCEL Mİ (KURULUM/1 · 1b).
#
# BU BETİK GÖÇ UYGULAMAZ, kasıtlı bir tercih (bkz. başlıktaki not).
# Ama uygulanmadığını DOĞRULAMIYORDU da: kaynağa yeni bir göç girip
# canlıya uygulanmadan yayın yapılabilirdi ve "Yayın BAŞARILI" satırı
# görünürdü. Kod yeni sütunu bekler, veritabanında yoktur, hata
# kullanıcıya çıkar.
#
# ÖLÇÜLDÜ (2026-08-27): kaynakta 202 göç, canlı geçmişinde 202 kayıt,
# iki yönde de fark 0. Yani bugüne kadar elle uygulama disiplini
# tutmuş. Kapı, o disiplinin unutulduğu ilk günü yakalamak için.
#
# İKİ BAĞLAM: AppDbContext (Migrations/) ve HrDbContext
# (Migrations/HumanResources/). İkisi aynı __EFMigrationsHistory
# tablosunu paylaşıyor, o yüzden tek sorgu ikisini de kapsıyor.
# `dotnet ef` ÇAĞRILMIYOR: derleme gerektirir, yavaştır ve iki bağlam
# yüzünden --context olmadan zaten hata verir.
#
gocleri_dogrula() {
    log "INFO" "Göç kapısı: iki bağlamın da güncel olduğu doğrulanıyor..."

    local canli_baglanti
    canli_baglanti="$(grep -E '^DB_CONNECTION=' "$ENV_FILE" | sed -E "s/^DB_CONNECTION=//" | tr -d \'\")"
    [ -z "$canli_baglanti" ] && fail "Göç kapısı: DB_CONNECTION okunamadı."

    local h u d
    h=$(sed -n 's/.*Host=\([^;]*\).*/\1/p' <<<"$canli_baglanti"); [ -z "$h" ] && h=localhost
    u=$(sed -n 's/.*Username=\([^;]*\).*/\1/p' <<<"$canli_baglanti")
    d=$(sed -n 's/.*Database=\([^;]*\).*/\1/p' <<<"$canli_baglanti")
    PGPASSWORD=$(sed -n 's/.*Password=\([^;]*\).*/\1/p' <<<"$canli_baglanti")
    export PGPASSWORD

    local kaynak gecmis
    kaynak="$(mktemp)"; gecmis="$(mktemp)"

    # Kaynaktaki TÜM göçler — her iki bağlam.
    { ls "${REPO_ROOT}"/backend/EnderunAI.Api/Migrations/*.cs 2>/dev/null
      ls "${REPO_ROOT}"/backend/EnderunAI.Api/Migrations/HumanResources/*.cs 2>/dev/null; } \
      | grep -v Designer | grep -v Snapshot \
      | sed 's|.*/||; s|\.cs$||' | sort > "$kaynak"

    if ! psql -h "$h" -U "$u" -d "$d" -tAc \
            'select "MigrationId" from "__EFMigrationsHistory"' 2>/dev/null | sort > "$gecmis"; then
        unset PGPASSWORD
        rm -f "$kaynak" "$gecmis"
        fail "Göç kapısı: canlı veritabanına bağlanılamadı."
    fi
    unset PGPASSWORD

    # BOŞ SONUÇ YOKLUĞUN KANITI DEĞİLDİR (Kural 48): geçmiş tablosu boş
    # dönüyorsa sorgu çalışmamış demektir, "hiç göç yok" demek değil.
    if [ ! -s "$gecmis" ]; then
        rm -f "$kaynak" "$gecmis"
        fail "Göç kapısı: geçmiş tablosu BOŞ okundu; sorgu çalışmamış olabilir."
    fi

    local bekleyen fazladan
    bekleyen="$(comm -23 "$kaynak" "$gecmis")"
    fazladan="$(comm -13 "$kaynak" "$gecmis")"
    rm -f "$kaynak" "$gecmis"

    if [ -n "$bekleyen" ]; then
        log "ERROR" "Canlıya UYGULANMAMIŞ göç(ler) var:"
        echo "$bekleyen" | while read -r m; do log "ERROR" "    $m"; done
        fail "Göç kapısı: önce yedek alıp 'dotnet ef database update --context <bağlam>' çalıştırın."
    fi

    if [ -n "$fazladan" ]; then
        log "ERROR" "Canlıda kaynağın TANIMADIĞI göç kimliği var:"
        echo "$fazladan" | while read -r m; do log "ERROR" "    $m"; done
        fail "Göç kapısı: canlı şema kaynağın ilerisinde; incelenmeden yayın yapılmaz."
    fi

    log "INFO" "Göç kapısı: iki bağlam da güncel."
}

publish_backend() {
    log "INFO" "Backend publish ediliyor: ${BACKEND_PUBLISH_DIR}"

    # KOŞUCU ÜZERİNDEN — publish YAYININ EN AĞIR DERLEMESİDİR.
    #
    # 2026-08-27'de fark edildi: test çağrısı koşucuya bağlanmıştı ama
    # publish DOĞRUDAN çağrılıyordu. Yani yayının Release derlemesi
    # tavansız, tek-örnek kapısız ve kalıcı derleyici sunucusu açık
    # koşuyordu — korumanın en çok gerektiği adım korumasızdı.
    #
    # Nöbetçi test de yalnız `dotnet test` arıyordu; artık `dotnet`
    # ile başlayan HER derleme çağrısını arıyor (Kural 31: komuta bak,
    # tek bir kelimeye değil).
    if ! "${REPO_ROOT}/scripts/derleme-kos.sh" \
            dotnet publish "$BACKEND_DIR" -c Release -o "$BACKEND_PUBLISH_DIR" 2>&1 | tee -a "$LOG_FILE"; then
        fail "dotnet publish başarısız oldu."
    fi
}

build_frontend() {
    log "INFO" "Frontend build ediliyor..."
    if ! (cd "$FRONTEND_DIR" && npm run build) 2>&1 | tee -a "$LOG_FILE"; then
        fail "npm run build başarısız oldu."
    fi
}

# YEDEK ALINAMAZSA YAYIN DURUR.
#
# Bu adım eskiden çıkış kodunu HİÇ okumuyordu: yedek betiği düşse de
# yayın devam ediyordu. Yedeğin amacı "yayın bozarsa geri dön"; yedek
# yoksa o güvence de yok.
#
# 2026-08-25'te yedek betiği, şifreleme anahtarı yoksa DURACAK şekilde
# değişti (şifresiz dump diske düşmesin diye). O değişiklik bu kontrol
# olmadan sessiz bir yedeksiz-yayın kapısı açardı.
# YEDEK BETİĞİNİN TEK KAYNAĞI REPO.
#
# /usr/local/bin altındaki çalışan kopya her yayında repodan yeniden
# kuruluyor. Sürüklenme böyle TESTLE DEĞİL, İNŞA YOLUYLA imkânsız:
# canlıda elle yapılmış bir değişiklik bir sonraki yayında geri alınır
# ve repo dışında yaşayamaz.
install_backup_script() {
    local kaynak="${REPO_ROOT}/scripts/enderun-backup.sh"

    if [ ! -f "$kaynak" ]; then
        fail "Yedek betiği repoda bulunamadı: $kaynak"
    fi

    if ! bash -n "$kaynak"; then
        fail "Yedek betiğinde sözdizimi hatası — kurulmadı: $kaynak"
    fi

    if ! cmp -s "$kaynak" /usr/local/bin/enderun-backup.sh; then
        log "INFO" "Yedek betiği repodan güncelleniyor (canlı kopya farklıydı)."
    fi

    install -m 700 -o root -g root "$kaynak" /usr/local/bin/enderun-backup.sh \
        || fail "Yedek betiği kurulamadı: /usr/local/bin/enderun-backup.sh"
}

backup_database() {
    install_backup_script
    log "INFO" "Veritabanı yedeği alınıyor (enderun-backup.sh)..."
    if [ -x /usr/local/bin/enderun-backup.sh ]; then
        if ! /usr/local/bin/enderun-backup.sh; then
            fail "Yedekleme BAŞARISIZ — yayın durduruldu. Yedeksiz yayın yapılmaz."
        fi
    else
        fail "/usr/local/bin/enderun-backup.sh bulunamadı — yayın durduruldu. Yedeksiz yayın yapılmaz."
    fi
}

restart_services() {
    log "INFO" "Servisler yeniden başlatılıyor..."
    systemctl restart enderunai-backend
    systemctl restart enderunai-frontend
}

# ═══════════════════════════════════════════════════════════════
# PROXY DUMAN KONTROLÜ — GÖVDESİZ DURUM KODU
# ═══════════════════════════════════════════════════════════════
#
# NEDEN VAR: sağlık kontrolü `/api/health`e bakıyor ve o GÖVDELİ
# cevap dönüyor. Bu yüzden 18 Temmuz'dan 30 Ağustos'a kadar süren
# bir arızayı HİÇ görmedi:
#
#   Proxy her yanıtı gövde olarak geçiriyordu; Web standardına göre
#   `new Response(gövde, { status: 204 })` FIRLATIR. Fırlatan yapıcı
#   catch'e düşüyor ve proxy 502 döndürüyordu. Arka uçta 11
#   kontrolcüde 21 uç 204 dönüyor — ödeme planı satır işlemleri,
#   İK kayıtları, şirket ayarları dahil. ON PAKETİN yazma uçları
#   altı hafta boyunca canlıda 502 verdi ve 2865 test yeşildi.
#
# TESTLER BU KATMANI GÖRMEZ: hepsi servisi DOĞRUDAN çağırıyor,
# proxy'den geçmiyor. Proxy'nin tek gözü tarayıcı doğrulamasıydı.
#
# BU KONTROL PROXY ÜZERİNDEN ve 204 DÖNEN bir uca gider. Cevap 204
# ya da 401 olmalı (401 = kimlik yok ama PROXY ÇALIŞIYOR). 502
# gelirse proxy gövdesiz durumu yine kıramıyor demektir ve yayın
# BAŞARISIZ sayılır.
#
# ANONİM VE GÖVDESİZ BİR UÇ KULLANILIYOR (`/api/health/govdesiz`).
# İlk deneme kimlik gerektiren bir uçtaydı ve 401 döndü — yani 204
# yoluna HİÇ ULAŞMADI; kontrol yeşil verdi, proxy kırıktı. Kabul
# edilen TEK cevap 204'tür; 401 dahil her şey başarısızlıktır.
proxy_duman_kontrolu() {
    local kod
    kod="$(curl -s -o /dev/null -w "%{http_code}" -m 5 \
        "http://127.0.0.1:3000/api/backend/health/govdesiz" 2>/dev/null)"

    #
    # ÜÇ SONUÇ, ÜÇ DAVRANIŞ (Kural 67):
    #
    #   204 → GEÇTİ.  Gövdesiz durum proxy'den sağ geçti.
    #   502 → İHLAL.  Aradığımız kusur geri gelmiş; YAYIN DURUR.
    #   ??? → KARAR VEREMEDİ. Kontrolün HEDEFİ yanlış (uç taşınmış,
    #         adı değişmiş, kimlik istemeye başlamış). Bu bir YAYIN
    #         sorunu değil, KONTROL sorunudur — uyarır, düşürmez.
    #
    # NEDEN "BİLİNMEYEN" YAYINI DÜŞÜRMÜYOR: bu kapı ilk kez bu
    # yayında koşuyor ve hedef ucun canlıda 204 döndüğü HENÜZ
    # ÖLÇÜLEMEDİ (uç bu yayınla geliyor). Yeni bir kapının ilk işi
    # sağlıklı bir yayını düşürmek olmamalı. Kapı yalnız ARADIĞI
    # KUSURA (502) karşı serttir; hedefini bulamadığında sessizce
    # geçmez ama yayını da kesmez — üçüncü durumu AYRI bildirir.
    case "$kod" in
        204)
            log "INFO" "Proxy duman kontrolü GEÇTİ (204 proxy'den geçti)."
            return 0
            ;;
        502)
            log "ERROR" "Proxy duman kontrolü İHLAL: 204 dönen uç 502 döndü."
            log "ERROR" "Proxy gövdesiz durum kodlarını kıramıyor; 21 yazma ucu ÖLÜ."
            log "ERROR" "Bu, 18 Tem–30 Ağu arasında altı hafta süren arızanın aynısı."
            return 1
            ;;
        *)
            log "WARN" "Proxy duman kontrolü KARAR VEREMEDİ: HTTP ${kod} (204 da 502 de değil)."
            log "WARN" "Hedef uç /api/health/govdesiz bulunamadı ya da kimlik istiyor."
            log "WARN" "Yayın DURDURULMADI — bu kontrolün sorunu, yayının değil."
            return 0
            ;;
    esac
}

wait_for_health() {
    log "INFO" "Sağlık kontrolü başlıyor (en fazla ${HEALTH_CHECK_TIMEOUT_SECONDS}s)..."
    local elapsed=0

    while [ "$elapsed" -lt "$HEALTH_CHECK_TIMEOUT_SECONDS" ]; do
        local backend_ok=0
        local frontend_ok=0

        curl -sf -m 3 "http://127.0.0.1:5155/api/health" > /dev/null 2>&1 && backend_ok=1
        curl -sf -m 3 -o /dev/null "http://127.0.0.1:3000/login" 2>&1 && frontend_ok=1

        if [ "$backend_ok" -eq 1 ] && [ "$frontend_ok" -eq 1 ]; then
            log "INFO" "Sağlık kontrolü BAŞARILI (backend + frontend, ${elapsed}s içinde)."
            proxy_duman_kontrolu || return 1
            return 0
        fi

        sleep "$HEALTH_CHECK_INTERVAL_SECONDS"
        elapsed=$((elapsed + HEALTH_CHECK_INTERVAL_SECONDS))
    done

    log "ERROR" "Sağlık kontrolü BAŞARISIZ (${HEALTH_CHECK_TIMEOUT_SECONDS}s içinde sağlıklı olmadı)."
    return 1
}

rollback() {
    log "WARN" "OTOMATİK GERİ DÖNÜŞ başlatılıyor..."

    if [ -d "$BACKEND_ROLLBACK_DIR" ]; then
        rm -rf "$BACKEND_PUBLISH_DIR"
        cp -a "$BACKEND_ROLLBACK_DIR" "$BACKEND_PUBLISH_DIR"
        log "INFO" "Backend önceki sürüme geri alındı."
    else
        log "WARN" "Backend rollback yedeği yok, geri alınamadı."
    fi

    if [ -d "$FRONTEND_NEXT_ROLLBACK_DIR" ]; then
        rm -rf "$FRONTEND_NEXT_DIR"
        cp -a "$FRONTEND_NEXT_ROLLBACK_DIR" "$FRONTEND_NEXT_DIR"
        log "INFO" "Frontend önceki sürüme geri alındı."
    else
        log "WARN" "Frontend rollback yedeği yok, geri alınamadı."
    fi

    restart_services

    if wait_for_health; then
        log "INFO" "Geri dönüş sonrası servisler sağlıklı."
        DEPLOY_OUTCOME="FAILED_ROLLED_BACK_OK"
    else
        log "ERROR" "Geri dönüş sonrasında bile servisler sağlıksız — elle müdahale gerekiyor!"
        DEPLOY_OUTCOME="FAILED_ROLLBACK_ALSO_UNHEALTHY"
    fi
}

# Yayın yalnız beklenen daldan yapılır.
require_expected_branch() {
    local current
    current="$(git -C "$REPO_ROOT" rev-parse --abbrev-ref HEAD)"

    if [ "$current" != "$DEPLOY_BRANCH" ]; then
        fail "Yayın dalı '${DEPLOY_BRANCH}' ama çalışma ağacı '${current}' dalında. \
Yanlış dalı canlıya çıkarmamak için durduruldu. Bilerek başka bir dal \
yayınlanacaksa: DEPLOY_BRANCH=${current} $0"
    fi

    log "INFO" "Yayın dalı doğrulandı: ${current}"
}

main() {
    log "INFO" "===== safe-deploy başladı ====="

    yarim_kosu_denetle

    require_clean_git_tree
    require_expected_branch

    log "INFO" "git pull çalıştırılıyor (dal: $(git rev-parse --abbrev-ref HEAD))..."
    if ! git pull 2>&1 | tee -a "$LOG_FILE"; then
        fail "git pull başarısız oldu."
    fi

    # Kapsam pull'DAN SONRA belirleniyor: HEAD ancak o noktada kesin.
    resolve_test_scope
    log "INFO" "Test kapsamı: ${TEST_SCOPE} (${TEST_SCOPE_REASON})"

    # GÖÇ KAPISI TESTLERDEN ÖNCE: 20 dakikalık test turunu koşup sonra
    # "göç eksik" demek, hem zaman hem de operatörün sabrı israfıdır.
    asama "goc-kapisi"
    gocleri_dogrula

    asama "backend-testleri"
    run_backend_tests
    asama "on-yuz-testleri"
    run_frontend_tests
    asama "surum-yedegi"
    backup_current_release
    asama "yayinlama"
    publish_backend
    asama "on-yuz-derleme"
    build_frontend
    asama "veritabani-yedegi"
    backup_database
    asama "servis-baslatma"
    restart_services

    asama "saglik-kontrolu"
    if wait_for_health; then
        DEPLOY_OUTCOME="SUCCESS"
        log "INFO" "Yayın BAŞARILI."

        # Kayıt YALNIZCA başarıda güncelleniyor. Başarısız ya da geri
        # alınmış bir yayından sonra taban eski commit'te kalmalı;
        # yoksa bir sonraki denemede o değişiklikler diff'ten düşer ve
        # backend testleri hiç koşmadan yayınlanabilirdi.
        record_successful_deploy
    else
        asama "geri-alma"
        rollback
    fi

    yarim_kosu_kendi_izini_sil
    print_summary

    if [ "$DEPLOY_OUTCOME" = "SUCCESS" ]; then
        exit 0
    else
        exit 1
    fi
}

# Doğrudan çalıştırıldığında yayın yapar; source edildiğinde yalnızca
# fonksiyonları tanımlar. Sınıflandırma mantığının testten koşulabilmesi
# için gerekli — yoksa test betiği gerçek bir yayın tetiklerdi.
if [ "${BASH_SOURCE[0]}" = "$0" ]; then
    main "$@"
fi
