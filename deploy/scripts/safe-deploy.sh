#!/usr/bin/env bash
#
# EnderunAI güvenli yayın (safe deploy) scripti.
#
# Akış: git pull -> backend testleri (geçmezse DUR) -> frontend testleri
#       (geçmezse DUR) -> dotnet publish -> npm run build ->
#       veritabanı yedeği -> servisleri restart ->
#       30 sn içinde sağlık kontrolü -> sağlıksızsa ÖNCEKİ sürüme otomatik
#       geri dön.
#
# Testler geçmeden hiçbir servise dokunulmaz; testler geçmezse repo'daki
# yeni kod bile publish edilmez, canlı sürüm olduğu gibi çalışmaya devam
# eder. NOT: Bu script yeni EF Core migration'larını canlı veritabanına
# OTOMATİK uygulamaz — migration içeren bir değişiklik yayınlanıyorsa,
# yedek aldıktan sonra "dotnet ef database update" hâlâ elle çalıştırılmalı
# (mevcut oturum boyunca izlenen yöntem budur, kasıtlı bir tercihtir).

set -uo pipefail

REPO_ROOT="/var/www/enderun-ai"
BACKEND_DIR="${REPO_ROOT}/backend/EnderunAI.Api"
BACKEND_TEST_PROJECT="${REPO_ROOT}/backend/EnderunAI.Api.Tests/EnderunAI.Api.Tests.csproj"
FRONTEND_DIR="${REPO_ROOT}/frontend/enderun-ai"

BACKEND_PUBLISH_DIR="${REPO_ROOT}/publish"
BACKEND_ROLLBACK_DIR="${REPO_ROOT}/publish-rollback"
FRONTEND_NEXT_DIR="${FRONTEND_DIR}/.next"
FRONTEND_NEXT_ROLLBACK_DIR="${REPO_ROOT}/frontend-next-rollback"

ENV_FILE="/etc/enderunai/backend.env"
LOG_FILE="/var/log/enderun-deploy.log"

HEALTH_CHECK_TIMEOUT_SECONDS=30
HEALTH_CHECK_INTERVAL_SECONDS=2

START_TIME="$(date +%s)"
DEPLOY_OUTCOME="UNKNOWN"

log() {
    echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) [$1] $2" | tee -a "$LOG_FILE"
}

fail() {
    log "ERROR" "$1"
    print_summary
    exit 1
}

print_summary() {
    local elapsed=$(( $(date +%s) - START_TIME ))
    echo ""
    echo "================= SAFE-DEPLOY ÖZET ================="
    echo "Sonuç       : ${DEPLOY_OUTCOME}"
    echo "Süre        : ${elapsed}s"
    echo "Git commit  : $(cd "$REPO_ROOT" && git rev-parse --short HEAD 2>/dev/null || echo '-')"
    echo "Log dosyası : ${LOG_FILE}"
    echo "======================================================"
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

run_backend_tests() {
    log "INFO" "Backend testleri çalıştırılıyor..."
    resolve_test_db_connection

    if dotnet test "$BACKEND_TEST_PROJECT" --configuration Release 2>&1 | tee -a "$LOG_FILE"; then
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

publish_backend() {
    log "INFO" "Backend publish ediliyor: ${BACKEND_PUBLISH_DIR}"
    if ! dotnet publish "$BACKEND_DIR" -c Release -o "$BACKEND_PUBLISH_DIR" 2>&1 | tee -a "$LOG_FILE"; then
        fail "dotnet publish başarısız oldu."
    fi
}

build_frontend() {
    log "INFO" "Frontend build ediliyor..."
    if ! (cd "$FRONTEND_DIR" && npm run build) 2>&1 | tee -a "$LOG_FILE"; then
        fail "npm run build başarısız oldu."
    fi
}

backup_database() {
    log "INFO" "Veritabanı yedeği alınıyor (enderun-backup.sh)..."
    if [ -x /usr/local/bin/enderun-backup.sh ]; then
        /usr/local/bin/enderun-backup.sh
    else
        log "WARN" "/usr/local/bin/enderun-backup.sh bulunamadı, veritabanı yedeği ATLANDI."
    fi
}

restart_services() {
    log "INFO" "Servisler yeniden başlatılıyor..."
    systemctl restart enderunai-backend
    systemctl restart enderunai-frontend
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

main() {
    log "INFO" "===== safe-deploy başladı ====="

    require_clean_git_tree

    log "INFO" "git pull çalıştırılıyor (dal: $(git rev-parse --abbrev-ref HEAD))..."
    if ! git pull 2>&1 | tee -a "$LOG_FILE"; then
        fail "git pull başarısız oldu."
    fi

    run_backend_tests
    run_frontend_tests
    backup_current_release
    publish_backend
    build_frontend
    backup_database
    restart_services

    if wait_for_health; then
        DEPLOY_OUTCOME="SUCCESS"
        log "INFO" "Yayın BAŞARILI."
    else
        rollback
    fi

    print_summary

    if [ "$DEPLOY_OUTCOME" = "SUCCESS" ]; then
        exit 0
    else
        exit 1
    fi
}

main "$@"
