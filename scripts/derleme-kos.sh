#!/usr/bin/env bash
#
# DERLEME/TEST KOŞUCUSU — TEK ÖRNEK, TEK CGROUP, BELLEK TAVANLI.
#
# ═══════════════════════════════════════════════════════════════════
# NEDEN VAR (2026-08-26, üç OOM'dan sonra)
# ═══════════════════════════════════════════════════════════════════
#
# Bir arka plan görevini durdurmak SARMALAYICI kabuğu öldürür, altındaki
# süreç ağacını ÖLDÜRMEZ. Ölçüldü: durdurulan bir `dotnet build`in
# ardında PPID=1 olan bir `csc.dll` (Roslyn derleyicisi) 3,9 GB tutarak
# yaşamaya devam etti. Ayrıca `VBCSCompiler` (Roslyn'in KALICI derleyici
# sunucusu) tasarımı gereği derleme bitince de ayakta kalıyor ve 2,9 GB
# tutuyordu.
#
# İkinci bir derleme başlatılınca ikisi aynı obj/ kilidinde buluştu,
# 8 GB'lık makine tükendi ve çekirdek OOM killer'ı çağırdı — bir
# oturumda ÜÇ KEZ (21:39, 22:17, 22:37).
#
# ÜÇ KAPI:
#   1. TEK ÖRNEK — sabit adlı systemd scope. İkincisi systemd
#      tarafından reddedilir; kendi kilit dosyamı yazmıyorum, çünkü
#      kilidi tutan süreç OOM ile ölürse kilit dosyası yalan söyler.
#   2. SÜREÇ AĞACI — her şey scope'un cgroup'unda. `systemctl stop`
#      cgroup'un TAMAMINI sonlandırır; sarmalayıcı SIGKILL yese bile
#      cgroup ortada kalmaz.
#   3. BELLEK TAVANI — MemoryMax. Bellek dolarsa çekirdek TESTİ öldürür,
#      canlı API'yi değil.
#
# AYRICA KALICI DERLEYİCİ SUNUCUSU KAPATILIYOR: node reuse ve paylaşımlı
# derleme kapalı. Derleme biraz yavaşlar; karşılığında geride hiçbir
# süreç kalmaz. Bu makinede canlı uygulama ile test koşusu AYNI yerde,
# o yüzden hız değil sınır önceliklidir.
#
# KULLANIM:  scripts/derleme-kos.sh dotnet test <proje> ...
#
set -uo pipefail

BIRIM="${DERLEME_BIRIMI:-enderun-derleme}"
TAVAN="${DERLEME_BELLEK_TAVANI:-3G}"

if [[ $# -eq 0 ]]; then
    echo "kullanım: $0 <komut> [argümanlar...]" >&2
    exit 64
fi

# ── KAPI 1: zaten koşan var mı ────────────────────────────────────
if systemctl is-active --quiet "${BIRIM}.scope" 2>/dev/null; then
    echo "ZATEN KOŞAN BİR DERLEME VAR (${BIRIM}.scope) — yenisi BAŞLATILMADI." >&2
    echo "Bitmesini bekleyin ya da durdurun: systemctl stop ${BIRIM}.scope" >&2
    systemd-cgls "/system.slice/${BIRIM}.scope" 2>/dev/null | head -10 >&2
    exit 75   # EX_TEMPFAIL — geçici engel, hata değil
fi

# Önceki koşudan kalmış ölü scope varsa temizle (yoksa ad çakışır).
systemctl reset-failed "${BIRIM}.scope" 2>/dev/null || true

# ── KAPI 3 + kalıcı derleyici sunucusunu kapat ────────────────────
exec systemd-run \
    --scope \
    --unit="${BIRIM}" \
    --quiet \
    --collect \
    --property=MemoryMax="${TAVAN}" \
    --property=MemorySwapMax=2G \
    --setenv=MSBUILDDISABLENODEREUSE=1 \
    --setenv=DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    --setenv=UseSharedCompilation=false \
    -- "$@"
