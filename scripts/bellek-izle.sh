#!/usr/bin/env bash
#
# Backend sürecinin bellek kullanımını zaman serisine yazar.
#
# NEDEN AYRI BİR İZLEME: sysstat/sar sistem geneli belleği tutuyor
# ama SÜREÇ BAŞINA RSS'i tutmuyor. Aranan şey tam olarak o: dotnet
# süreci 7 Ağu 18:00'de 4,6 GB, 10 Ağu 21:00'de 3,96 GB RSS'e
# ulaşıp OOM killer tarafından öldürüldü. Sunucu 7,7 GB RAM'e sahip;
# tek bir sürecin yarısını tüketmesi normal değil. Sızıntı mı yoksa
# yalnızca yoğun anlık yük mü olduğunu ancak zaman içindeki eğri
# söyler.
#
# 4 GB swap (2026-08-12) çöküşü engeller ama sızıntıyı gizler —
# süreç ölmek yerine sessizce yavaşlar. Bu yüzden izleme swap ile
# BİRLİKTE gerekli.
#
# Çıktı: tarih, RSS (MB), VSZ (MB), sürecin başlangıcından beri
# geçen süre. Yeniden başlatmalar "elapsed" sıfırlanmasından
# anlaşılır — bir sızıntı arıyorsak ölçüm ancak aynı süreç
# içinde karşılaştırılabilir.

set -uo pipefail

LOG_FILE="${LOG_FILE:-/var/log/enderun-bellek.log}"
SERVICE="${SERVICE:-enderunai-backend}"

main_pid=$(systemctl show "$SERVICE" -p MainPID --value 2>/dev/null)

if [[ -z "$main_pid" || "$main_pid" == "0" ]]; then
    printf '%s\t%s\tKAPALI\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" "$SERVICE" \
        >> "$LOG_FILE"
    exit 0
fi

read -r rss_kb vsz_kb elapsed < <(
    ps -o rss=,vsz=,etime= -p "$main_pid" 2>/dev/null | awk '{print $1, $2, $3}'
)

if [[ -z "${rss_kb:-}" ]]; then
    exit 0
fi

# Sistem geneli de yazılıyor: sürecin büyümesi mi yoksa genel
# baskı mı olduğu tek satırda görünsün.
mem_available_mb=$(awk '/MemAvailable/ {print int($2/1024)}' /proc/meminfo)
swap_used_mb=$(awk '/SwapTotal/ {t=$2} /SwapFree/ {f=$2} END {print int((t-f)/1024)}' /proc/meminfo)

printf '%s\tpid=%s\trss_mb=%s\tvsz_mb=%s\telapsed=%s\tmem_avail_mb=%s\tswap_used_mb=%s\n' \
    "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" \
    "$main_pid" \
    "$((rss_kb / 1024))" \
    "$((vsz_kb / 1024))" \
    "$elapsed" \
    "$mem_available_mb" \
    "$swap_used_mb" \
    >> "$LOG_FILE"
