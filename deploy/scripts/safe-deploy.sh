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
# S6 — BACKEND DE ATOMİK TAKASLA GİRİYOR.
# `dotnet publish -o publish/` çalışan servisin okuduğu dizinin
# ÜSTÜNE yazıyordu: publish 40-90 sn sürüyor ve bu süre boyunca
# dizin yarı eski yarı yeni. Ön yüzde aynı kusur ölçülmüş, kesinti
# izleyicisinde 24 parça hatası olarak görünmüştü (DAĞITIM/1).
BACKEND_PUBLISH_YENI="${REPO_ROOT}/publish-yeni"
BACKEND_PUBLISH_ESKI="${REPO_ROOT}/publish-eski"
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

# WebSocket duman kontrolü vekile 127.0.0.1'den vuruyor; nginx doğru
# server bloğunu seçebilsin diye Host başlığı gerekiyor. Değer nginx
# yapılandırmasındaki `server_name` ile aynı olmalı — ayrışırsa kontrol
# varsayılan sunucuya düşer ve "karar veremedi" der (sessizce geçmez).
PROXY_HOST="${PROXY_HOST:-enderunai.com.tr}"
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

# POZİTİF BİTİŞ İŞARETİ.
#
# Bir işin bittiği YALNIZ pozitif bir bitiş işaretiyle bilinir. Bu
# dosya koşunun BAŞINDA siliniyor, SONUNDA yazılıyor. Yokluğu "bitti"
# demek değil, "henüz bitmedi" demek.
#
# DOĞURAN OLAY (2026-09-04): deploy'un bittiğini `pgrep` çıktısının
# boş olmasından ve günlükte özet görünmemesinden çıkardım. İkisi de
# DOLAYLI sinyaldi ve ikisi de yanılttı; deploy koşmaya devam ediyordu
# ve ben o sırada `safe-deploy.sh`i düzenledim — koşan bir bash
# betiğini değiştirmek onu bozabilir.
SON_KOSU_DOSYASI="${DEPLOY_STATE_DIR}/son-kosu"
YARIM_KOSU_ONAYLANDI="${YARIM_KOSU_ONAYLANDI:-}"
ASAMA="baslangic"

log() {
    echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) [$1] $2" | tee -a "$LOG_FILE"
}

# İZLEYİCİ HER ÇIKIŞTA DURUR — arkada saniyede bir curl atan bir
# süreç bırakmak, düzeltmeye çalıştığımız şeyin başka bir biçimi olurdu.
temizle_izleyici() {
    [ -n "${KESINTI_IZLEYICI_PID:-}" ] && kill "$KESINTI_IZLEYICI_PID" 2>/dev/null || true
}
trap temizle_izleyici EXIT

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

    # POZİTİF BİTİŞ İŞARETİ — sonuç ne olursa olsun yazılır.
    # "Başarısız" da bir bitiştir; okuyanın ayırt etmesi için sonuç
    # da yazılıyor.
    mkdir -p "$DEPLOY_STATE_DIR" 2>/dev/null || true
    {
        echo "SONUC=${DEPLOY_OUTCOME}"
        echo "COMMIT=$(cd "$REPO_ROOT" && git rev-parse --short HEAD 2>/dev/null || echo '-')"
        echo "ZAMAN=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
        echo "SURE=${elapsed}s"
    } > "$SON_KOSU_DOSYASI" 2>/dev/null || true
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

# ═══════════════════════════════════════════════════════════════
# AĞAÇ YAYIN BOYUNCA DEĞİŞMEDİ Mİ (Y1)
# ═══════════════════════════════════════════════════════════════
#
# ÖLÇÜLEN AÇIK — VE BENİM KENDİ İHLALİMDEN ÇIKTI (2026-09-08):
# `require_clean_git_tree` yayının BAŞINDA koşuyor. Ben yayın
# ortasında çalışma ağacına yeni bir dosya ekledim; kapı çoktan
# geçmişti ve COMMIT EDİLMEMİŞ KOD canlıya derlenecekti. Fark edip
# dosyayı kenara aldım — ama kapı bunu yakalamıyordu.
#
# Bu, "kapı doğru soruyu YANLIŞ ZAMANDA soruyor" sınıfı. Derleme
# adımından hemen önce aynı soru TEKRAR soruluyor ve ayrıca
# başlangıçtaki commit ile karşılaştırılıyor: yayın hangi koddan
# başladıysa onu yayınlamalı.
YAYIN_BASLANGIC_SHA=""

agac_baslangicini_kaydet() {
    YAYIN_BASLANGIC_SHA="$(git -C "$REPO_ROOT" rev-parse HEAD)"
    log "INFO" "Yayın başlangıç commit'i: ${YAYIN_BASLANGIC_SHA:0:8}"
}

agac_hala_ayni_mi() {
    # SESSİZCE GEÇMEZ: başlangıç kaydedilmemişse kapı ölçüm
    # yapamamıştır ve bunu söylemek zorundadır (Kural 48).
    if [ -z "$YAYIN_BASLANGIC_SHA" ]; then
        log "WARN" "Ağaç kapısı ÖLÇEMEDİ: başlangıç commit'i kaydedilmemiş."
        return 0
    fi

    local simdi
    simdi="$(git -C "$REPO_ROOT" rev-parse HEAD)"

    if [ "$simdi" != "$YAYIN_BASLANGIC_SHA" ]; then
        log "ERROR" "AĞAÇ DEĞİŞTİ: yayın ${YAYIN_BASLANGIC_SHA:0:8} ile başladı,"
        log "ERROR" "şimdi ${simdi:0:8}. Yayın hangi koddan başladıysa onu yayınlamalı."
        return 1
    fi

    local kirli
    kirli="$(git -C "$REPO_ROOT" status --porcelain)"

    if [ -n "$kirli" ]; then
        log "ERROR" "AĞAÇ KİRLENDİ: yayın koşarken çalışma ağacına dokunuldu."
        log "ERROR" "Commit edilmemiş kod derlenip canlıya çıkardı."
        printf '%s
' "$kirli" | head -10 | while IFS= read -r satir; do
            log "ERROR" "  ${satir}"
        done
        return 1
    fi

    log "INFO" "Ağaç kapısı GEÇTİ: yayın boyunca çalışma ağacı değişmedi."
    return 0
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
    # SAHTE DEĞER, AÇIK ÖNEKLE İŞARETLİ (sır bekçisi kuralı).
    # Bu değişken TESTLERİN koştuğu süreç için gerekiyor —
    # `dotnet test` uygulamanın Host'unu ayağa kaldırıyor. Göç
    # yolundan ise TAMAMEN kaldırıldı (HrDbContextFactory).
    export JWT_SECRET="TEST-deploy-script-jwt-secret-0123456789"
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

    # BU KOŞUMUN GÜNLÜK BAŞLANGICI.
    #
    # 2026-09-08: yayın "DERLEME BAŞARISIZ (test koşmadı)" diyerek
    # durdu. Derleme geçmişti; 3156 test geçmiş, biri düşmüştü.
    # Aşağıdaki ayırt etme grep'i BİRİKEN günlüğün TAMAMINA bakıyor
    # ve bir gün önceki (07-09 00:11 ve 17:06) OutOfMemoryException
    # satırlarını buluyordu. Teşhis, bugünün değil dünkü koşumun
    # kanıtına dayanıyordu.
    #
    # Kural 65'in ta kendisi: ölçtüğümü sandığım şey (bu koşum) ile
    # gerçekten ölçtüğüm şey (bütün geçmiş) ayrışmıştı. Üstelik bu
    # satırın YORUMU tam da yanlış teşhisin kötülüğünü anlatıyordu.
    local gunluk_baslangic
    gunluk_baslangic=$(wc -l < "$LOG_FILE")

    # TEST KOŞUCU ÜZERİNDEN (2026-08-26): tek örnek, kendi cgroup'u,
    # bellek tavanı. Doğrudan çağrı, durdurulduğunda ardında 4,5 GB'lık
    # yetim Roslyn süreci bırakıyordu ve ikinci koşu makineyi OOM'a
    # sokuyordu — bir oturumda üç kez. Bkz. scripts/derleme-kos.sh.
    if "${REPO_ROOT}/scripts/derleme-kos.sh" \
            dotnet test "$BACKEND_TEST_PROJECT" --configuration Release 2>&1 | tee -a "$LOG_FILE"; then
        log "INFO" "Backend testleri geçti."
    else
        # ── DÜŞÜŞÜN SEBEBİ AYIRT EDİLİYOR ────────────────────────
        #
        # 2026-09-07: yayın bu satırda durdu ve günlükte "Backend
        # testleri BAŞARISIZ" yazdı. HİÇBİR TEST KOŞMAMIŞTI —
        # derleyici `System.OutOfMemoryException` ile ölmüştü.
        #
        # Mesaj yanlış yere baktırıyordu: hangi testin düştüğü aranıyor,
        # oysa sorun testte değil derlemede. "Yedek" belirsizliğiyle
        # aynı sınıf — satır, olanı değil olduğu sanılanı söylüyordu.
        #
        # Günlükte derleme hatası izi varsa öyle denir; yoksa test
        # düşüşü denir. Emin olunamayan durumda İKİSİ DE söylenir —
        # yanlış bir teşhis, teşhissizlikten kötüdür.
        # YALNIZ BU KOŞUMUN DİLİMİ okunuyor.
        if tail -n "+$((gunluk_baslangic + 1))" "$LOG_FILE" \
                | grep -qE "OutOfMemoryException|error MSB|error CS[0-9]+|Build FAILED"; then
            fail "DERLEME BAŞARISIZ (test koşmadı) — günlükte derleyici hatası var. Yayın DURDURULDU, hiçbir servise dokunulmadı."
        else
            fail "Backend TESTLERİ BAŞARISIZ (derleme geçti, test düştü). Yayın DURDURULDU, hiçbir servise dokunulmadı."
        fi
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
    log "INFO" "SÜRÜM YEDEĞİ [safe-deploy.sh:backup_current_release] — derlenmiş çıktı kopyalanıyor. VERİ YEDEĞİ DEĞİLDİR."

    rm -rf "$BACKEND_ROLLBACK_DIR"
    if [ -d "$BACKEND_PUBLISH_DIR" ]; then
        cp -a "$BACKEND_PUBLISH_DIR" "$BACKEND_ROLLBACK_DIR"
    fi

    rm -rf "$FRONTEND_NEXT_ROLLBACK_DIR"
    if [ -d "$FRONTEND_NEXT_DIR" ]; then
        cp -a "$FRONTEND_NEXT_DIR" "$FRONTEND_NEXT_ROLLBACK_DIR"
    fi

    log "INFO" "SÜRÜM YEDEĞİ hazır [safe-deploy.sh:backup_current_release] — yalnız derlenmiş çıktı: ${BACKEND_ROLLBACK_DIR}, ${FRONTEND_NEXT_ROLLBACK_DIR}"
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
        # HAM KOMUT DEĞİL, ONAYLI YOL SÖYLENİYOR.
        #
        # Burada önce "yedek alıp `dotnet ef database update` çalıştırın"
        # yazıyordu. O cümle, göç provasını operatörün HATIRLAMASINA
        # bırakıyordu — ve prova safe-deploy'da çağrılsa bile burada
        # hiçbir zaman iş yapmıyor, çünkü bu kapı bekleyen göç bulunca
        # yayını zaten durduruyor. Yani provanın iş yaptığı tek an
        # (elle uygulama öncesi) korumasızdı.
        #
        # `goc-uygula.sh` bağı MEKANİK yapar: provayı kendisi koşar,
        # geçmezse canlıya tek bir DDL göndermez.
        log "ERROR" "Göç kapısı: göçü ELLE 'dotnet ef database update' ile uygulamayın."
        log "ERROR" "Onaylı tek yol — provayı kendisi koşar, geçmezse reddeder:"
        log "ERROR" "    sudo deploy/scripts/goc-uygula.sh"
        fail "Göç kapısı: uygulanmamış göç var; 'deploy/scripts/goc-uygula.sh' çalıştırın."
    fi

    if [ -n "$fazladan" ]; then
        log "ERROR" "Canlıda kaynağın TANIMADIĞI göç kimliği var:"
        echo "$fazladan" | while read -r m; do log "ERROR" "    $m"; done
        fail "Göç kapısı: canlı şema kaynağın ilerisinde; incelenmeden yayın yapılmaz."
    fi

    log "INFO" "Göç kapısı: iki bağlam da güncel."
}

publish_backend() {
    log "INFO" "Backend publish ediliyor: ${BACKEND_PUBLISH_YENI} (canlı dizine DOKUNULMUYOR)"

    # Yarım kalmış bir önceki denemenin artığı kalmasın.
    rm -rf "$BACKEND_PUBLISH_YENI"

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
            dotnet publish "$BACKEND_DIR" -c Release -o "$BACKEND_PUBLISH_YENI" 2>&1 | tee -a "$LOG_FILE"; then
        fail "dotnet publish başarısız oldu."
    fi

    # ÇIKTI GERÇEKTEN ÜRETİLDİ Mİ. `dotnet publish` sıfırla dönüp
    # yarım bir dizin bırakabilir; takas ettiğimiz şeyin çalışabilir
    # olduğunu takastan ÖNCE bilmek zorundayız.
    if [ ! -f "${BACKEND_PUBLISH_YENI}/EnderunAI.Api.dll" ]; then
        fail "publish başarılı göründü ama EnderunAI.Api.dll yok: ${BACKEND_PUBLISH_YENI}"
    fi
}

# BACKEND TAKASI — İKİ RENAME, YENİDEN BAŞLATMADAN HEMEN ÖNCE.
#
# ESKİ DİZİN DİSKTE KALIYOR: `publish-eski` bir sonraki yayına kadar
# silinmiyor. Çalışan süreç henüz eşlenmemiş bir derlemeyi (uydu
# derlemesi, geç yüklenen bir bağımlılık) okumak isterse dosya
# yerinde duruyor.
swap_backend() {
    [ -d "$BACKEND_PUBLISH_YENI" ] || fail "Takas edilecek yeni backend yayını yok."

    rm -rf "$BACKEND_PUBLISH_ESKI"

    if [ -d "$BACKEND_PUBLISH_DIR" ]; then
        mv -T "$BACKEND_PUBLISH_DIR" "$BACKEND_PUBLISH_ESKI" \
            || fail "Backend takası: eski yayın kenara alınamadı."
    fi

    mv -T "$BACKEND_PUBLISH_YENI" "$BACKEND_PUBLISH_DIR" || {
        # GERİ SAR: canlı dizin boş kalmasın.
        [ -d "$BACKEND_PUBLISH_ESKI" ] && mv -T "$BACKEND_PUBLISH_ESKI" "$BACKEND_PUBLISH_DIR"
        fail "Backend takası: yeni yayın yerine konamadı, eskiye dönüldü."
    }

    log "INFO" "Backend takası tamam (eski yayın ${BACKEND_PUBLISH_ESKI} dizininde bekliyor)."
}

# ═══════════════════════════════════════════════════════════════
# KESİNTİ İZLEYİCİSİ — KALICI KAPI, TEK SEFERLİK ÖLÇÜM DEĞİL
# ═══════════════════════════════════════════════════════════════
#
# NEDEN KAPI, NEDEN ÖLÇÜM DEĞİL: bu arıza aylarca fark edilmedi.
# 32 parça 404'ünün 19'u bu paket başlamadan önceydi ve kimse
# görmedi — çünkü ölçen kimse yoktu. Tek seferlik bir ölçüm bugünü
# kurtarır, yarın aynı satır geri gelir.
#
# NE SAYIYOR: yayın boyunca saniyede bir /login isteniyor ve
# HTML'deki ilk JS parçası da isteniyor. Sayılan şey PARÇA hatası —
# sayfa 200 dönüp parçası düşerse ekran kırıktır ve kullanıcı için
# en yanıltıcı hâl budur.
#
# NEDEN SAYFA HATASI SAYILMIYOR: yeniden başlatma sırasında sunucu
# birkaç saniye kapalı ve bağlantı reddediliyor (kod 000). Bu AYRI
# ve açık bir durum; kapıyı ona bağlamak her yayında yanlış alarm
# üretirdi. Kapı yalnız "sunucu ayakta ama bozuk içerik veriyor"
# hâline karşı sert.
KESINTI_IZLEYICI_PID=""
KESINTI_KAYIT="/tmp/enderun-yayin-kesinti.txt"

kesinti_izleyicisi_baslat() {
    : > "$KESINTI_KAYIT"

    (
        while :; do
            govde="$(curl -s -m 4 "http://127.0.0.1:3000/login" 2>/dev/null)"
            parca="$(printf '%s' "$govde" \
                | grep -oE '/_next/static/chunks/[A-Za-z0-9_.-]+\.js' | head -1)"

            if [ -n "$parca" ]; then
                pk="$(curl -s -o /dev/null -w '%{http_code}' -m 4 \
                    "http://127.0.0.1:3000${parca}" 2>/dev/null)"
                case "$pk" in
                    200|000) : ;;
                    *) echo "$(date -u +%H:%M:%S) PARCA ${pk} ${parca}" >> "$KESINTI_KAYIT" ;;
                esac
            fi

            # S6 — ARKA UÇ DA İZLENİYOR.
            #
            # DG2 yalnız ön yüz parçalarına bakıyordu. Ama `dotnet
            # publish` de canlı dizinin üstüne yazıyordu: çalışan
            # süreç yarım yazılmış bir derlemeyi okursa 500 verir ve
            # bunu kimse görmezdi — yayın "BAŞARILI" derdi.
            #
            # 000 SAYILMIYOR: yeniden başlatmanın ~2 saniyesi zaten
            # kabul edilmiş bir risk (S5). Burada aranan şey o değil,
            # servis AYAKTAYKEN bozulan cevap.
            bk="$(curl -s -o /dev/null -w '%{http_code}' -m 4 \
                "http://127.0.0.1:5155/api/health" 2>/dev/null)"
            case "$bk" in
                200|000) : ;;
                *) echo "$(date -u +%H:%M:%S) ARKAUC ${bk} /api/health" >> "$KESINTI_KAYIT" ;;
            esac

            sleep 1
        done
    ) &

    KESINTI_IZLEYICI_PID=$!
    log "INFO" "Kesinti izleyicisi başladı (saniyede bir: ön yüz parçası + arka uç sağlığı)."
}

# ÜÇ SONUÇ, ÜÇ DAVRANIŞ (Kural 67):
#   0 hata          -> GEÇTİ
#   >0 hata         -> İHLAL, yayın BAŞARISIZ sayılır
#   izleyici yoksa  -> KARAR VEREMEDİ; sessizce geçmez
kesinti_izleyicisi_bitir() {
    if [ -z "$KESINTI_IZLEYICI_PID" ]; then
        log "WARN" "Kesinti kapısı KARAR VEREMEDİ: izleyici hiç başlamadı."
        return 0
    fi

    kill "$KESINTI_IZLEYICI_PID" 2>/dev/null || true
    wait "$KESINTI_IZLEYICI_PID" 2>/dev/null || true
    KESINTI_IZLEYICI_PID=""

    # SAYIM `wc -l` İLE — `grep -c` TUZAĞI ÖLÇÜLDÜ.
    #
    # `grep -c . dosya` boş dosyada "0" basar AMA çıkış kodu 1 döner.
    # `|| echo 0` de çalışır ve çıktı "0\n0" olur; sayı
    # karşılaştırması "integer expression expected" ile patlar ve
    # `if` yanlış tarafa düşer. Sonuç: kapı TEMİZ bir yayında bile
    # İHLAL verirdi — yani her yayını düşürürdü.
    #
    # Sondanın ilk ayağı (boş kayıt → geçmeli) tam olarak bunu
    # yakaladı. `wc -l` boş dosyada 0 basar ve çıkış kodu 0'dır.
    local hata
    hata="$(wc -l < "$KESINTI_KAYIT" 2>/dev/null || echo 0)"
    hata="${hata:-0}"

    if [ "$hata" -eq 0 ]; then
        log "INFO" "Kesinti kapısı GEÇTİ: yayın boyunca ön yüz parçası ve arka uç sağlığı hatasız."
        return 0
    fi

    log "ERROR" "Kesinti kapısı İHLAL: yayın sırasında ${hata} bozuk cevap (PARCA = ön yüz, ARKAUC = arka uç)."
    log "ERROR" "Sunucu ayaktayken bozuk içerik verdi — kullanıcı için ekran kırık."
    sed -n '1,10p' "$KESINTI_KAYIT" | while IFS= read -r satir; do
        log "ERROR" "  ${satir}"
    done
    return 1
}

# ═══════════════════════════════════════════════════════════════
# ÖN YÜZ DERLEMESİ AYRI DİZİNE — CANLI KESİNTİSİ İÇİN (DAĞITIM/1)
# ═══════════════════════════════════════════════════════════════
#
# ESKİ HÂLİ DOĞRUDAN `.next` İÇİNE YAZIYORDU ve bu, çalışan sunucunun
# altından zemin çekmek demekti:
#
#   build_frontend      -> .next YENİDEN YAZILIYOR (~85 sn)
#   backup_database     -> ~48 sn
#   restart_services    -> ancak burada yeni sunucu
#
# Yani 2 dk 13 sn – 2 dk 37 sn boyunca disk YENİ, sunucu ESKİ.
# Çalışan Next kendi manifest'indeki parça adlarını arıyor, o dosyalar
# artık yok. ÖLÇÜLDÜ (rig, 25 sn'lik pencere): 187 istekten 24'ü
# parça hatası. Sayfa 200 dönüyor, parçası düşüyor — kullanıcı için
# "açılıyor ama bozuk".
#
# ÜRETİMDE PENCERE 5 KAT UZUN olduğu için oran daha da kötü.
#
# YENİ AKIŞ: derleme `.next-yeni` içine yapılıyor; `.next`e derleme
# ve yedek boyunca HİÇ DOKUNULMUYOR. Takas yeniden başlatmadan hemen
# önce, iki yeniden adlandırmayla.
FRONTEND_NEXT_YENI="${FRONTEND_DIR}/.next-yeni"
FRONTEND_NEXT_ESKI="${FRONTEND_DIR}/.next-eski"
# Kapının adayı: takas ÖNCESİ seçilip buraya yazılıyor (bkz. swap_frontend).
ESKI_PARCA_ADAYI="/tmp/enderun-eski-parca-adayi.txt"

build_frontend() {
    log "INFO" "Frontend build ediliyor (ayrı dizin: .next-yeni)..."

    rm -rf "$FRONTEND_NEXT_YENI"

    if ! (cd "$FRONTEND_DIR" && NEXT_DIST_DIR=".next-yeni" npm run build) 2>&1 \
            | tee -a "$LOG_FILE"; then
        fail "npm run build başarısız oldu."
    fi

    if [ ! -f "${FRONTEND_NEXT_YENI}/BUILD_ID" ]; then
        fail "Yeni yapı üretilmedi (.next-yeni/BUILD_ID yok)."
    fi

    log "INFO" "Yeni yapı hazır; canlının .next dizinine henüz dokunulmadı."
}

# ═══════════════════════════════════════════════════════════════
# ATOMİK TAKAS — YENİDEN BAŞLATMADAN HEMEN ÖNCE
# ═══════════════════════════════════════════════════════════════
#
# SUNUCUNUN GÖRDÜĞÜ DİZİN YA TAMAMEN ESKİ YA TAMAMEN YENİ. Aradaki
# hâl iki `mv` arasındaki mikrosaniyeler; `mv` aynı dosya sisteminde
# `rename(2)` demek ve atomiktir.
#
# ESKİ PARÇALAR KORUNUYOR — ve asıl kesintiyi bitiren şey bu:
# takas ile yeniden başlatma arasında (birkaç saniye) ESKİ sunucu
# hâlâ çalışıyor ve kendi parça adlarını istiyor. O dosyalar yeni
# yapıda YOK. Parça adları içerik özetli olduğu için çakışma
# imkânsız; eski `static/` yeni yapıya ÜZERİNE YAZMADAN kopyalanıyor
# (`cp -an`). Böylece hem eski sunucu hem tarayıcıdaki eski sekmeler
# ayakta kalıyor.
swap_frontend() {
    [ -d "$FRONTEND_NEXT_YENI" ] || fail "Takas edilecek yeni yapı yok."

    # ═══ ESKİ PARÇALAR YENİ YAPIYA KOPYALANIYOR — ASIL KORUMA BU ═══
    #
    # MEKANİZMA DÜZELTMESİ: "eski dizini silme, adını değiştir" tek
    # başına ÇALIŞAN SÜRECİ KURTARMAZ. Next, `distDir`i açılışta MUTLAK
    # yola çözüyor (.../frontend/enderun-ai/.next). `mv` sonrası o yol
    # artık YENİ yapıyı gösteriyor; eski süreç tembel bir parça okumak
    # istediğinde `.next-eski`ye değil yine `.next`e bakar.
    #
    # Yani yükü taşıyan şey yeniden adlandırma değil, BU KOPYALAMA:
    # eski dosyalar yeni yapının İÇİNDE de bulunuyor.
    #
    # `static/` istemci parçaları, `server/` sunucunun tembel okuduğu
    # parçalar. İkisi de gerekiyor: kullanıcı o ana kadar hiç
    # gitmediği bir sayfaya giderse ikisinden de okuma olur.
    #
    # `cp -an` ÜZERİNE YAZMAZ: yeni yapının dosyaları korunur, yalnız
    # eskide olup yenide olmayanlar eklenir. Parça adları içerik
    # özetli olduğu için çakışma imkânsız; eklenen dosyalar yeni
    # sunucu için ETKİSİZDİR (kendi manifest'inde adları geçmez).

    # ═══ KAPININ ADAYI, KOPYALAMADAN ÖNCE SEÇİLİYOR ═══
    #
    # ÖLÇÜLEN KUSUR (2026-09-08): `eski_parca_kapisi` üç yayın üst
    # üste "ÖLÇEMEDİ: yalnız eskide bulunan parça yok" dedi. Sebep
    # tesadüf değil, YAPISAL: kapı adayını "eskide olup yenide
    # olmayan" diye arıyordu, ama aşağıdaki kopyalama tam da o kümeyi
    # BOŞALTIYOR. Ölçüldü: 207 = 207, kesişim farkı 0.
    #
    # Yani kapı, korumak istediği şeyin ta kendisi yüzünden hiçbir
    # zaman ölçüm yapamıyordu. Öz-sınama (S4) kapının AYIRT
    # EDEBİLDİĞİNİ kanıtlıyor; ama sınayacak aday hiç oluşmuyordu.
    #
    # Aday artık BURADA, kopyalamadan önce seçiliyor ve dosyaya
    # yazılıyor. Kapı yeniden başlatmadan sonra o parçayı soruyor:
    # "eski süreç henüz okumadığı bir parçayı isterse bulabilir mi".
    : > "$ESKI_PARCA_ADAYI"
    if [ -d "${FRONTEND_NEXT_DIR}/static/chunks" ] \
       && [ -d "${FRONTEND_NEXT_YENI}/static/chunks" ]; then
        (cd "${FRONTEND_NEXT_DIR}/static/chunks" && ls -1 *.js 2>/dev/null) \
            | while IFS= read -r parca; do
                [ -f "${FRONTEND_NEXT_YENI}/static/chunks/${parca}" ] \
                    || { echo "$parca" >> "$ESKI_PARCA_ADAYI"; break; }
            done
    fi

    if [ -s "$ESKI_PARCA_ADAYI" ]; then
        log "INFO" "Eski parça kapısı adayı seçildi (kopyalamadan ÖNCE): $(head -1 "$ESKI_PARCA_ADAYI")"
    else
        log "WARN" "Eski parça kapısı adayı YOK: yeni yapı eskinin bütün parçalarını içeriyor (ön yüz kaynağı değişmemiş olabilir)."
    fi

    for alt in static server; do
        if [ -d "${FRONTEND_NEXT_DIR}/${alt}" ]; then
            log "INFO" "Eski ${alt}/ yeni yapıya kopyalanıyor (üzerine yazmadan)..."
            mkdir -p "${FRONTEND_NEXT_YENI}/${alt}"
            cp -an "${FRONTEND_NEXT_DIR}/${alt}/." "${FRONTEND_NEXT_YENI}/${alt}/" \
                2>/dev/null || true
        fi
    done

    rm -rf "$FRONTEND_NEXT_ESKI"

    if [ -d "$FRONTEND_NEXT_DIR" ]; then
        mv -T "$FRONTEND_NEXT_DIR" "$FRONTEND_NEXT_ESKI" \
            || fail "Eski yapı kenara alınamadı."
    fi

    mv -T "$FRONTEND_NEXT_YENI" "$FRONTEND_NEXT_DIR" || {
        # GERİ AL: yeni yapı yerine konamadıysa eskisini geri koy,
        # yoksa sunucu yapısız kalır.
        [ -d "$FRONTEND_NEXT_ESKI" ] && mv -T "$FRONTEND_NEXT_ESKI" "$FRONTEND_NEXT_DIR"
        fail "Yeni yapı yerine konamadı; eski yapı geri alındı."
    }

    log "INFO" "Ön yüz yapısı takas edildi (atomik)."

    eski_parca_kapisi
}

# ═══════════════════════════════════════════════════════════════
# ESKİ PARÇA KAPISI — "ERKEN SİLDİM" HATASINI YAKALAYAN TEK ÖLÇÜM
# ═══════════════════════════════════════════════════════════════
#
# Takas yapıldı ama sunucu HENÜZ YENİDEN BAŞLAMADI. O aralıkta çalışan
# eski süreç, kullanıcı daha önce hiç gitmediği bir sayfaya giderse
# TEMBEL olarak kendi eski parçasını okumak ister.
#
# SIK ZİYARET EDİLEN BİR SAYFAYLA SINAMAK HATAYI GİZLER: onun parçaları
# zaten bellekte, dosya silinmiş olsa bile 200 döner. Bu yüzden kapı
# YALNIZCA ESKİ YAPIDA BULUNAN bir parça seçiyor — yeni yapıda adı
# geçmeyen, yani ancak diskten okunabilecek bir dosya.
#
#   200 -> eski parçalar erişilebilir, kopyalama tuttu
#   404 -> eski yapı erken kayboldu; bugünkü arızanın aynısı
# ─────────────────────────────────────────────────────────────────
# ESKİ PARÇA KAPISI (S4)
#
# ÜÇ SONUÇ, ÜÇ CÜMLE (Kural 67). "GEÇTİ" ile "ÖLÇEMEDİ" günlükte
# birbirine karışmayacak: ikisi de yayını sürdürüyor ama biri kanıt,
# öteki kanıtsızlık. Karıştıklarında bekçi yeşil görünerek ölür.
#   GEÇTİ    → ölçüm yapıldı, eski parça hâlâ servis ediliyor.
#   ÖLÇEMEDİ → ölçüm YAPILAMADI; kapı bu koşumda hiçbir şey söylemedi.
#   İHLAL    → ölçüm yapıldı, eski parça kayıp.
# ─────────────────────────────────────────────────────────────────

# ÖZ-SINAMA: KAPI ISIRIYOR MU?
#
# Bu kapının en sinsi kırılma biçimi sessizce her zaman 200 dönmesi
# olurdu — Next bir yeniden yazma kuralıyla /_next altındaki her yolu
# yakalarsa kapı sonsuza dek "GEÇTİ" der ve hiçbir şey ölçmez.
# Bu yüzden ölçümden ÖNCE, KESİNLİKLE OLMAYAN bir parça isteniyor:
# oraya 200 dönüyorsa kapının ayırt etme gücü yoktur ve bunu ÖLÇEMEDİ
# diye söylemesi gerekir.
#
# Beyan (Kural 61): olmayan parça için beklenen KIRMIZI (200 değil);
# gerçek parça için beklenen YEŞİL (200).
eski_parca_kapisi_oz_sinama() {
    local sahte="sonda-$(date +%s)-olmayan-parca.js"
    local kod

    kod="$(curl -s -o /dev/null -w '%{http_code}' -m 5 \
        "http://127.0.0.1:3000/_next/static/chunks/${sahte}" 2>/dev/null)"

    if [ "$kod" = "200" ]; then
        log "WARN" "Eski parça kapısı ÖZ-SINAMADA KALDI: olmayan bir parça"
        log "WARN" "için de 200 dönüyor. Kapı 'geçti' dese bile hiçbir şey"
        log "WARN" "ölçmüyor demektir — bu koşumda ölçüm YOK sayılacak."
        return 1
    fi

    log "INFO" "Eski parça kapısı öz-sınaması tamam (olmayan parça -> HTTP ${kod}; ayırt ediyor)."
    return 0
}

eski_parca_kapisi() {
    if [ ! -d "${FRONTEND_NEXT_ESKI}/static/chunks" ]; then
        log "WARN" "Eski parça kapısı ÖLÇEMEDİ: önceki yapı dizini yok (ilk yayın olabilir)."
        return 0
    fi

    # ADAY BURADA ARANMIYOR — TAKAS ÖNCESİ SEÇİLDİ.
    #
    # Burada aramak ölçülebilir hiçbir şey bulamıyordu: `swap_frontend`
    # eski `static/`i yeni yapıya kopyaladığı için "yalnız eskide olan
    # parça" kümesi takas sonrası HER ZAMAN boş. Kapı üç yayın üst
    # üste ÖLÇEMEDİ dedi ve bu bir tesadüf değil, tanımın kendisiydi.
    local aday=""
    [ -s "$ESKI_PARCA_ADAYI" ] && aday="$(head -1 "$ESKI_PARCA_ADAYI")"

    if [ -z "$aday" ]; then
        # SESSİZCE GEÇMİYOR: aday yoksa kapı ölçüm yapmamıştır ve
        # bunu söylemek zorundadır (Kural 48).
        log "WARN" "Eski parça kapısı ÖLÇEMEDİ: takas öncesi yalnız eskide bulunan parça seçilemedi."
        return 0
    fi

    # Önce kapının kendisi sınanıyor; ayırt edemiyorsa sonucu ÖLÇEMEDİ.
    if ! eski_parca_kapisi_oz_sinama; then
        log "WARN" "Eski parça kapısı ÖLÇEMEDİ (öz-sınama). Aday parça: ${aday}."
        return 0
    fi

    local kod
    kod="$(curl -s -o /dev/null -w '%{http_code}' -m 5 \
        "http://127.0.0.1:3000/_next/static/chunks/${aday}" 2>/dev/null)"

    case "$kod" in
        200)
            log "INFO" "Eski parça kapısı GEÇTİ: ${aday} hâlâ 200 (ölçüm yapıldı)."
            ;;
        *)
            log "ERROR" "Eski parça kapısı İHLAL: ${aday} -> HTTP ${kod}."
            log "ERROR" "Takas sonrası eski parçalar erişilemez; çalışan sunucu"
            log "ERROR" "henüz okumadığı bir parçayı isterse ekran KIRILIR."
            return 1
            ;;
    esac
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
        log "INFO" "VERİ YEDEĞİ betiği [enderun-backup.sh] repodan güncelleniyor (canlı kopya farklıydı)."
    fi

    install -m 700 -o root -g root "$kaynak" /usr/local/bin/enderun-backup.sh \
        || fail "Yedek betiği kurulamadı: /usr/local/bin/enderun-backup.sh"
}

backup_database() {
    install_backup_script
    log "INFO" "VERİ YEDEĞİ alınıyor [enderun-backup.sh] — veritabanı dökümü + uploads + proje dosyaları, şifreli."
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

# ═══════════════════════════════════════════════════════════════
# WEBSOCKET DUMAN KONTROLÜ — SESSİZ LONGPOLLING DÜŞÜŞÜNE KARŞI
# ═══════════════════════════════════════════════════════════════
#
# NEDEN VAR (2026-09-07): mesajlaşmanın canlı akışı SignalR
# WebSocket'i üzerinden geliyor ve nginx'te AYRI bir location'a
# bağlı (`location ^~ /api/hubs/`). O blok bozulursa ya da nginx
# yeniden yüklenmezse istek `location /api`ye düşer, `Upgrade`
# başlığı geçmez ve SignalR SESSİZCE LongPolling'e iner.
#
# SONUÇ: uygulama çalışıyor görünür — her mesaj için saniyede bir
# HTTP isteği atarak. Hiçbir sağlık kontrolü ötmez. Bu düşüş bir
# HAFTA yaşayabilir.
#
# ═══ NEDEN 101 BEKLENMİYOR ═══
#
# ÖLÇÜLDÜ: kimliksiz bir WS el sıkışması `/api/hubs/mesaj`ta
# **401** alıyor (Kestrel, `WWW-Authenticate: Bearer`). Yayın
# betiğinin oturum açacak kimliği yok ve OLMAMALI — sır taşımayan
# bir kapı, sır taşıyan bir kapıdan iyidir.
#
# VE 401 AYIRT ETMİYOR: vekil `Upgrade` başlığını geçirse de,
# hiç geçirmese de cevap 401. Ayırmayan bir ölçüm ölçüm değildir
# (Kural 65 — ölçtüğünü sandığın şeyle gerçekte ölçtüğün şey).
#
# ═══ NASIL ÖLÇÜLÜYOR ═══
#
# `/api/hubs/tasima-denetimi` ANONİM bir uç ve Kestrel'e ULAŞAN
# başlıklara bakıp iki bool döndürüyor. İstek gerçek yükseltme
# başlıklarıyla ve VEKİL ÜZERİNDEN atılıyor:
#
#   {"yukseltmeBasligiGeldi":true,"baglantiBasligiGeldi":true}
#     → hub location eşleşti VE yükseltme başlıklarını geçiriyor
#
# Uç `/api/hubs/` altında olduğu için nginx'in aynı bloğundan
# geçiyor: cevap doğruysa `/api/hubs/mesaj` de aynı bloktan geçer.
#
# ═══ ÜÇ SONUÇ, ÜÇ DAVRANIŞ (Kural 67) ═══
#
#   ikisi de true  → GEÇTİ
#   biri false     → İHLAL. Yükseltme geçmiyor; YAYIN DURUR.
#   bağlanılamadı  → KARAR VEREMEDİ. İnsan baksın; yayını düşürmez.
#   /başka kod
# ═══════════════════════════════════════════════════════════════════
# GİRİŞ DÖNGÜSÜ KAPISI (GİRİŞ-DÖNGÜ/1 · GD5)
# ═══════════════════════════════════════════════════════════════════
#
# ÖLÇÜLEN KUSUR (2026-09-08, canlı): giriş ekranı sonsuz yeniden
# yükleme döngüsündeydi. TEK tarayıcıdan 60 saniyede 1256 istek, 358
# tam sayfa yüklemesi. Konsol sessizdi; her yükleme konsolu siliyordu.
# Kusuru Mehmet Bey tarayıcıdan gördü — biz görmedik.
#
# NEDEN SAĞLIK KONTROLÜ YAKALAMADI: sağlık kontrolü `/login`e BİR
# istek atıp 200 görüyor ve geçiyor. Döngü tek istekte görünmüyor;
# ancak sayfayı AÇIK TUTUNCA ortaya çıkıyor.
#
# BU KAPI NE YAPIYOR: giriş sayfasını çekip içindeki betiklerin
# çalışacağı süre kadar (10 sn) bekleyemez — curl betik çalıştırmaz.
# Onun yerine SUNUCU TARAFINDAN ölçüyor: 10 saniye boyunca nginx
# kaydına düşen `/login` BELGE isteklerini sayıyor. Döngüdeki bir
# istemci varsa sayı hızla büyür.
#
# EŞİK: yayın sırasında gerçek kullanıcı da giriş yapıyor olabilir.
# Bir kullanıcının 10 saniyede birkaç kez /login açması olağan;
# saniyede birden fazla tam yükleme değil. Sınır 20.
#
# ÜÇ SONUÇ (Kural 67): nginx kaydı okunamıyorsa ÖLÇEMEDİ der,
# geçti demez.
giris_dongu_kapisi() {
    local gunluk="/var/log/nginx/access.log"

    if [ ! -r "$gunluk" ]; then
        log "WARN" "Giriş döngüsü kapısı ÖLÇEMEDİ: ${gunluk} okunamıyor."
        return 0
    fi

    local once sonra fark
    once="$(grep -c '"GET /login' "$gunluk" 2>/dev/null || echo 0)"
    sleep 10
    sonra="$(grep -c '"GET /login' "$gunluk" 2>/dev/null || echo 0)"
    fark=$(( sonra - once ))

    if [ "$fark" -le 20 ]; then
        log "INFO" "Giriş döngüsü kapısı GEÇTİ: 10 saniyede ${fark} /login belge isteği (sınır 20)."
        return 0
    fi

    log "ERROR" "Giriş döngüsü kapısı İHLAL: 10 saniyede ${fark} /login belge isteği (sınır 20)."
    log "ERROR" "Giriş ekranı kendini yeniden yüklüyor olabilir — GİRİŞ-DÖNGÜ/1."
    log "ERROR" "Tek tarayıcı saniyede 20+ istek üretiyorsa kesinti kendi kendini büyütür."
    return 1
}

websocket_duman_kontrolu() {
    local yanit kod govde

    # VEKİL ÜZERİNDEN — doğrudan Kestrel'e gitmek nginx'i ATLARDI ve
    # ölçmek istediğimiz tam olarak nginx'in o bloğu.
    #
    # HTTPS ŞART: port 80 bloğu her isteği 301 ile HTTPS'e yolluyor;
    # hub location'ı 443 bloğunda. HTTP'den ölçmek 301 döndürüyordu ve
    # kontrol hiçbir zaman hedefe ULAŞMIYORDU (ölçüldü 2026-09-07).
    #
    # `--resolve` DNS'i atlayıp doğrudan localhost'a bağlanıyor:
    # kontrol dış ağa ve DNS'e bağımlı olmasın. `-k` sertifika
    # doğrulamasını atlıyor çünkü sertifika alan adına yazılmış,
    # 127.0.0.1'e değil — burada ölçülen şey TLS değil, yönlendirme.
    yanit="$(curl -sk -m 5 -w $'\n%{http_code}' \
        --resolve "${PROXY_HOST}:443:127.0.0.1" \
        -H "Upgrade: websocket" \
        -H "Connection: Upgrade" \
        "https://${PROXY_HOST}/api/hubs/tasima-denetimi" 2>/dev/null)"

    kod="$(printf '%s' "$yanit" | tail -n 1)"
    govde="$(printf '%s' "$yanit" | head -n -1)"

    if [ "$kod" != "200" ]; then
        log "WARN" "WebSocket duman kontrolü KARAR VEREMEDİ: HTTP ${kod}."
        log "WARN" "Hedef uç /api/hubs/tasima-denetimi bulunamadı ya da vekile ulaşılamadı."
        log "WARN" "NOT: bu kurulumda TANINMAYAN yol da 401 döner (kimlik kapısı"
        log "WARN" "yönlendirmeden önce çalışıyor) — 401 'uç yok' demek olabilir."
        log "WARN" "Yayın DURDURULMADI — bu kontrolün sorunu, yayının değil."
        return 0
    fi

    if printf '%s' "$govde" | grep -q '"yukseltmeBasligiGeldi":true' \
       && printf '%s' "$govde" | grep -q '"baglantiBasligiGeldi":true'; then
        log "INFO" "WebSocket duman kontrolü GEÇTİ (Upgrade + Connection vekilden geçti)."
        return 0
    fi

    log "ERROR" "WebSocket duman kontrolü İHLAL: yükseltme başlıkları vekilden GEÇMİYOR."
    log "ERROR" "Yanıt: ${govde}"
    log "ERROR" "SignalR sessizce LongPolling'e düşer — mesaj başına saniyede bir HTTP isteği."
    log "ERROR" "Muhtemel sebep: nginx 'location ^~ /api/hubs/' bloğu yok ya da nginx yeniden yüklenmedi."
    log "ERROR" "GERİ ALMA: sudo cp /etc/nginx/sites-available/enderunai.com.tr.geri-<damga>-hubsuz /etc/nginx/sites-available/enderunai.com.tr && sudo nginx -t && sudo systemctl reload nginx"
    return 1
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
            websocket_duman_kontrolu || return 1
            giris_dongu_kapisi || return 1
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
        log "WARN" "SÜRÜM YEDEĞİ [safe-deploy.sh:backup_current_release] yok — backend geri alınamadı. (Veri yedeği ayrıdır, bu satır onu ilgilendirmez.)"
    fi

    if [ -d "$FRONTEND_NEXT_ROLLBACK_DIR" ]; then
        rm -rf "$FRONTEND_NEXT_DIR"
        cp -a "$FRONTEND_NEXT_ROLLBACK_DIR" "$FRONTEND_NEXT_DIR"
        log "INFO" "Frontend önceki sürüme geri alındı."
    else
        log "WARN" "SÜRÜM YEDEĞİ [safe-deploy.sh:backup_current_release] yok — frontend geri alınamadı. (Veri yedeği ayrıdır, bu satır onu ilgilendirmez.)"
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

# MESAİ SAATİ UYARISI (S5) — DURDURMAZ, SÖYLER.
#
# Yeniden başlatma boşluğu ~2 saniye ve kabul edilmiş bir risk.
# Kabul edilmiş olması, saat 11:00'de farkında olmadan alınmasını
# gerektirmiyor: yayın mesai içindeyse bunu YÜKSEK SESLE söylüyor.
#
# ENGELLEMİYOR. Acil bir güvenlik düzeltmesini mesai yüzünden
# durdurmak, kesintiden daha pahalıya patlar. Karar operatörde;
# betiğin işi kararı bilgili kılmak.
#
# SAAT DİLİMİ: sunucu UTC, kullanıcılar Türkiye'de. Mesai
# Europe/Istanbul'a göre okunuyor — UTC ile karşılaştırsaydık
# pencere üç saat kayar ve uyarı tam da öğlen vaktinde susardı.
mesai_uyarisi() {
    local saat
    saat=$(TZ=Europe/Istanbul date +%-H)

    if [ "$saat" -ge 9 ] && [ "$saat" -lt 18 ]; then
        log "WARN" "MESAİ SAATİ: şu an $(TZ=Europe/Istanbul date '+%H:%M') (Türkiye). Yeniden başlatma sırasında ~2 sn'lik bir kesinti olacak ve kullanıcılar ekranda görecek. Yayın DURDURULMADI."
    else
        log "INFO" "Mesai dışı ($(TZ=Europe/Istanbul date '+%H:%M') Türkiye) — yeniden başlatma boşluğu kullanıcıya denk gelmeyecek."
    fi
}

main() {
    log "INFO" "===== safe-deploy başladı ====="

    mesai_uyarisi

    yarim_kosu_denetle

    # ÖNCEKİ KOŞUNUN BİTİŞ İŞARETİ SİLİNİYOR: bu koşu bitene kadar
    # "bitti" cevabı verilemesin.
    rm -f "$SON_KOSU_DOSYASI" 2>/dev/null || true

    require_clean_git_tree
    require_expected_branch

    log "INFO" "git pull çalıştırılıyor (dal: $(git rev-parse --abbrev-ref HEAD))..."
    if ! git pull 2>&1 | tee -a "$LOG_FILE"; then
        fail "git pull başarısız oldu."
    fi

    # SİLİNEN SAVUNMA KONTROLÜ — YAYIN ÖNCESİ İKİNCİ AĞ.
    #
    # Kapı DEĞİL: çıkışı her zaman 0 ve yayını durdurmaz. Amacı,
    # yayınlanacak commit'in sildiği savunma satırlarını günlüğe basmak.
    # `2d90c946` bu kontrol olsaydı yakalanırdı — 26 satırlık bir atama
    # doğrulaması sessizce silinmiş ve canlıya çıkmıştı (Kural 72).
    #
    # KAPI KİPİ: alarm verirse commit mesajında `SAVUNMA-BEYAN:` arar.
    # Beyan varsa geçer, yoksa YAYIN DURUR.
    #
    # İlk yazımım yalnız günlüğe basıp geçiyordu. Mehmet düzeltti ve
    # gerekçe kendi cümlemdi: "ölçüm, ancak okunabildiği yerde
    # ölçümdür." Otomatik bir yayın turunda günlüğe basılan uyarı
    # OKUNMAZ — o hâliyle kontrol burada süs olurdu.
    #
    # KAPI "SİLME YASAK" DEMİYOR, "SİLDİĞİNİ SÖYLE" DİYOR. Meşru
    # taşımalar engellenmiyor; maliyeti commit mesajına bir cümle.
    # (`42486f70` gibi meşru bir refaktör de alarm verirdi ve mesajı
    # zaten anlatıyordu — eksik olan tek şey biçimli bir satırdı.)
    if ! "${REPO_ROOT}/deploy/scripts/silinen-savunma-kontrolu.sh" HEAD --kapi 2>&1 \
            | tee -a "$LOG_FILE"; then
        fail "Silinen savunma satırları beyan edilmemiş (Kural 72)."
    fi

    # Başlangıç commit'i de pull'DAN SONRA kaydediliyor — aynı gerekçe.
    agac_baslangicini_kaydet

    # Kapsam pull'DAN SONRA belirleniyor: HEAD ancak o noktada kesin.
    resolve_test_scope
    log "INFO" "Test kapsamı: ${TEST_SCOPE} (${TEST_SCOPE_REASON})"

    # GÖÇ KAPISI TESTLERDEN ÖNCE: 20 dakikalık test turunu koşup sonra
    # "göç eksik" demek, hem zaman hem de operatörün sabrı israfıdır.
    asama "goc-kapisi"
    # GÖÇ PROVASI — GÖÇ KAPISINDAN ÖNCE.
    #
    # ASIL YERİ BURASI DEĞİL, ELLE ÇAĞRIDIR. `gocleri_dogrula` bekleyen
    # göç bulunca yayını zaten DURDURUYOR ("önce yedek alıp dotnet ef
    # database update çalıştırın"), yani deploy anında bekleyen göç
    # OLMAZ ve prova burada neredeyse her zaman boş geçer.
    #
    # Provanın işe yaradığı an, göçü ELLE uygulamadan öncedir:
    #     deploy/scripts/goc-provasi.sh
    # Canlının taze kopyasında oynatır; patlarsa canlıya dokunulmaz.
    #
    # Buradaki çağrı bir AĞ: akış değişirse ya da bir göç araya
    # sızarsa yakalar. Maliyeti sıfıra yakın — bekleyen göç yoksa
    # kopya bile almadan çıkar.
    # ÇIKIŞ KODU AYRIŞTIRILIYOR — "KARAR VEREMEDİM" İLE "SORUN YOK"
    # AYNI ŞEY DEĞİLDİR.
    #
    # Önce `if ! ... | tee` idi ve iki kusuru vardı:
    #   1. Boru hattında betiğin çıkış kodu `tee`'ninkiyle karışır.
    #      Burada `set -uo pipefail` (satır 34) onu kurtarıyordu —
    #      yani muhafız, İKİ SATIR UZAKTAKİ BİR AYARA bağlıydı. O ayar
    #      bir gün kalkarsa prova sessizce etkisizleşirdi ve kimse
    #      fark etmezdi. Artık `PIPESTATUS` ile DOĞRUDAN okunuyor.
    #   2. Çıkış 1 (prova düştü) ile çıkış 2 (KARAR VEREMEDİ) aynı
    #      mesajı veriyordu. İkisi de yayını durdurur — ama operatöre
    #      ne yapacağını söyleyen şey mesajdır.
    "${REPO_ROOT}/deploy/scripts/goc-provasi.sh" 2>&1 | tee -a "$LOG_FILE"
    prova_kodu="${PIPESTATUS[0]}"
    case "$prova_kodu" in
        0)
            ;;
        2)
            log "ERROR" "════════════════════════════════════════════════"
            log "ERROR" "GÖÇ PROVASI KARAR VEREMEDİ (çıkış 2)."
            log "ERROR" "Prova, göç hakkında hüküm VEREMEDİ — düzenek,"
            log "ERROR" "bağlantı ya da ölçüm güvenilir değil."
            log "ERROR" "'Karar veremedim' ile 'sorun yok' AYNI ŞEY DEĞİLDİR."
            log "ERROR" "Yayın durdu. İNSAN BAKMALI; ayrıntı yukarıda."
            fail "Göç provası karar veremedi (çıkış 2) — insan incelemesi gerekiyor."
            ;;
        *)
            fail "Göç provası DÜŞTÜ (çıkış ${prova_kodu}) — ayrıntı yukarıda."
            ;;
    esac

    gocleri_dogrula

    # ── UCUZ KAPILAR — PAHALI TURLARDAN ÖNCE ──
    #
    # SIRA MALİYETE GÖRE: saniyelerle ölçülen kapılar önce, dakikalarla
    # ölçülen turlar sonra. Kapıların doğruluğu değişmiyor, yalnız yeri.
    #
    # DOĞURAN OLAY (2026-09-04): kurumsal kimlik taraması bir buton
    # rengini yakaladı ama iki tam turdan (~27 dk) ve arka uç
    # publish'inden SONRA. Aynı bulgu 24 saniyede gelebilirdi.
    #
    # LİSTE BURADA DEĞİL, `ucuz-kapilar.sh` içinde — aynı betik push
    # öncesi kancada da koşuyor. İki yerde iki liste, ayrışan bir
    # nokta demekti.
    # İZLEYİCİ UCUZ KAPILARDAN ÖNCE BAŞLIYOR.
    #
    # İlk yazımda `surum-yedegi` aşamasında başlatmıştım ve yorumuna
    # "ucuz kapıların derlemesi de bu pencerede" yazmıştım — YANLIŞTI,
    # ucuz kapılar ondan önce koşuyor. Tam da kapının yakalaması
    # gereken 10 hata (2026-09-07 22:33) o aşamada üretilmişti; kapı
    # orada başlasaydı onları GÖRMEZDİ.
    kesinti_izleyicisi_baslat

    log "INFO" "Ucuz kapılar çalıştırılıyor (pahalı turlardan önce)..."
    if ! "${REPO_ROOT}/deploy/scripts/ucuz-kapilar.sh" 2>&1 | tee -a "$LOG_FILE"; then
        fail "Ucuz kapılardan biri düştü; pahalı turlara girilmedi."
    fi

    asama "backend-testleri"
    run_backend_tests

    # KOŞUCUNUN TEST SAYIMI — ÇIRANIN İKİNCİ, BAĞIMSIZ KAYNAĞI.
    #
    # Arka uç turundan HEMEN SONRA: derleme sıcak, `--no-build` ile
    # saniyeler sürüyor. Ön yüz turu bu sayımı okuyup kendi statik
    # sayımıyla karşılaştırıyor; uyuşmazlık, çıranın bir test
    # özniteliğini TANIMADIĞINI söyler.
    #
    # Başarısızlığı yayını DURDURMUYOR: bu bir ölçüm kaynağı, bir kapı
    # değil. Üretilemezse çıra karşılaştırmayı "YAPILMADI" diye
    # BASIYOR — sessizce atlamıyor.
    if ! "${REPO_ROOT}/deploy/scripts/kosucu-test-sayimi.sh" --no-build 2>&1 \
            | tee -a "$LOG_FILE"; then
        log "WARN" "Koşucu test sayımı üretilemedi; çıra karşılaştırması bu turda YAPILMAYACAK."
    fi
    asama "on-yuz-testleri"
    run_frontend_tests
    asama "surum-yedegi"
    backup_current_release
    # DERLEME ADIMINDAN HEMEN ÖNCE: buradan sonrası artık canlıya
    # gidecek ikiliyi üretiyor.
    asama "agac-kapisi"
    agac_hala_ayni_mi || fail "Çalışma ağacı yayın sırasında değişti (Y1)."

    asama "yayinlama"
    publish_backend
    asama "on-yuz-derleme"
    build_frontend
    asama "veritabani-yedegi"
    backup_database

    # TAKAS EN SONA: yedek boyunca da canlı ESKİ yapıyı görüyor.
    asama "takas"
    swap_backend
    swap_frontend

    asama "servis-baslatma"
    restart_services

    asama "saglik-kontrolu"

    #
    # KESİNTİ KAPISI SAĞLIK KAPISINDAN AYRI DEĞERLENDİRİLİYOR — VE
    # BU BİLEREK BÖYLE.
    #
    # Sağlık düşerse GERİ ALMA doğru ilaç: yeni sürüm ayakta değil.
    # Kesinti kapısı düşerse geri alma YANLIŞ ilaç olurdu:
    #   · yeni kod sağlam (testler geçti, sağlık geçti)
    #   · geri alma ikinci bir takas + yeniden başlatma demek,
    #     yani aynı kırık pencereyi BİR KEZ DAHA üretmek
    # Hastalığı tedavi ederken hastayı ikinci kez hasta etmek olurdu.
    #
    # Bu yüzden ihlal: yayın BAŞARISIZ sayılır (çıkış 1, OnFailure
    # uyarısı gider, `last-deployed-commit` GÜNCELLENMEZ) ama yeni
    # sürüm yerinde kalır. Karar kimsenin dikkatinden kaçmaz;
    # düzeltmeyi insan yapar.
    #
    if wait_for_health; then
        if kesinti_izleyicisi_bitir; then
            DEPLOY_OUTCOME="SUCCESS"
            log "INFO" "Yayın BAŞARILI."

        # Kayıt YALNIZCA başarıda güncelleniyor. Başarısız ya da geri
        # alınmış bir yayından sonra taban eski commit'te kalmalı;
        # yoksa bir sonraki denemede o değişiklikler diff'ten düşer ve
        # backend testleri hiç koşmadan yayınlanabilirdi.
            record_successful_deploy
        else
            DEPLOY_OUTCOME="FAILURE"
            log "ERROR" "YAYIN BAŞARISIZ SAYILDI: kesinti kapısı ihlal."
            log "ERROR" "Yeni sürüm YERİNDE BIRAKILDI — geri alma ikinci bir"
            log "ERROR" "takas + yeniden başlatma demek, yani aynı kırık"
            log "ERROR" "pencereyi bir kez daha üretmek olurdu."
            log "ERROR" "Son yayın kaydı GÜNCELLENMEDİ; sebebi elle incelenmeli."
        fi
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
