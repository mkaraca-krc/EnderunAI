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
# ═══════════════════════════════════════════════════════════════════
# TAVAN NEDEN 6G VE NEDEN YANINDA GCHeapHardLimitPercent VAR
# ═══════════════════════════════════════════════════════════════════
#
# MemoryMax koymak .NET'in davranışını DEĞİŞTİRİYOR: cgroup sınırını
# gören çalışma zamanı kendi GC yığın tavanını sınırın %75'ine çekiyor
# ve sınıra varmadan `OutOfMemoryException` atıyor. Yani derleme
# cgroup'a çarpmadan, kendi kendine ölüyor.
#
# ÖLÇÜLDÜ (ikisi de aynı hatayla düştü):
#   MemoryMax=4G -> yığın tavanı ~3,0 GB -> zirve 3,46 GB'da OOM
#   MemoryMax=6G -> yığın tavanı ~4,6 GB -> zirve 4,80 GB'da OOM
#
# TEMİZ DERLEME ÖLÇÜLDÜ (obj/ ve bin/ silinmiş hâlde, tam tur):
#   cgroup zirvesi        5,72 GB
#   tek csc.dll süreci    5,63 GB  (zirvenin %98'i TEK süreç)
#   test fazının katkısı  0        (zirveye derlemede çıkılıyor)
#
# Tavan bu ölçümün üstüne, makinenin izin verdiği payla kondu:
# 6500M. Boştaki taban 1,6 GB (canlı arka uç 115 MB, ön yüz 100 MB,
# postgres 75 MB); geriye ~1,4 GB kalıyor ve test turunda büyüyen
# postgres bu payın içinde.
#
# 6G ile tur ZATEN GEÇMİŞTİ; tavan payı %5'ten ~%13'e çıksın diye
# yükseltildi. Tavanı yükseltmek geçen bir turu bozamaz, o yüzden
# yeniden ölçüm gerekmedi.
#
# Yığın yüzdesi 0x5A (=%90, hex) olmasaydı bu tavan da yetmezdi:
# .NET yığını sınırın %75'ine çeker (bkz. yukarıdaki ölçüm tablosu).
#
# İLK YAZIMDA TAVAN 3G'Yİ DENEMİŞTİM. Öyle kalsaydı safe-deploy'un
# HER yayını test aşamasında düşerdi — koruma, korumaya çalıştığı şeyi
# kırardı. Sayı tahminle değil ölçümle konuldu.
#
# DERLEMENİN 5 GB İSTEMESİ NORMAL DEĞİL: derlenen 88,4 MB kaynağın
# 81,5 MB'ı (%92) EF migration anlık görüntüsü — 195 dosya, her biri
# şemanın tam kopyası. Uygulama kodu yalnız 5,0 MB. Asıl çözüm
# migration'ları birleştirmek; o ayrı bir karar (Mehmet).
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
TAVAN="${DERLEME_BELLEK_TAVANI:-6500M}"

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
#
# R1 — İKİ KATMAN, ÇÜNKÜ ORTAM DEĞİŞKENİ TEK BAŞINA GARANTİ DEĞİL:
#
#   KUŞAK: `UseSharedCompilation` hem ortam değişkeni olarak hem de
#          `dotnet` çağrılarında AÇIK MSBuild özelliği olarak veriliyor.
#          Yalnız ortam değişkeni bırakılsaydı, proje dosyasında ya da
#          Directory.Build.props içinde tanımlı açık bir özellik onu
#          EZERDİ ve kimse fark etmezdi.
#
#   ASKI:  koşu bitince `dotnet build-server shutdown`. Sunucu yine de
#          doğduysa burada ölür.
#
# Gerekçe: `VBCSCompiler` KALICI bir derleyici sunucusudur, derleme
# bitince ÖLMEZ ve PPID=1'e bağlanır — süreç ağacını öldürmek onu
# temizlemez. Ölçüldü: 2,9 GB tutuyordu.
DOTNET_ARGS=()

if [[ "${1}" == "dotnet" ]] && [[ "${2:-}" =~ ^(build|test|publish|pack|msbuild)$ ]]; then
    DOTNET_ARGS=(-p:UseSharedCompilation=false)
fi

systemd-run \
    --scope \
    --unit="${BIRIM}" \
    --quiet \
    --collect \
    --property=MemoryMax="${TAVAN}" \
    --property=MemorySwapMax=2G \
    --setenv=DOTNET_GCHeapHardLimitPercent=5A \
    --setenv=MSBUILDDISABLENODEREUSE=1 \
    --setenv=DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    --setenv=UseSharedCompilation=false \
    -- "$@" "${DOTNET_ARGS[@]}"

cikis=$?

# ── ASKI: geride derleyici sunucusu bırakma ───────────────────────
# Çıkış kodu KORUNUYOR: kapatma başarısız olsa bile derlemenin
# sonucunu değiştirmemeli.
dotnet build-server shutdown >/dev/null 2>&1 || true

exit "$cikis"
