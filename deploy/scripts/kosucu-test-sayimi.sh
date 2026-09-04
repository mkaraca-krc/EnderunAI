#!/usr/bin/env bash
#
# KOŞUCUNUN TEST SAYIMI — ÇIRANIN İKİNCİ, BAĞIMSIZ KAYNAĞI.
#
# ═══ NEDEN VAR (2026-09-04) ═══
#
# Çıra `[Fact]` ve `[InlineData]` satırlarını sayıyordu.
# `[SkippableFact]` o desene UYMUYORDU: depoda 4 tane vardı ve DÖRDÜ
# DE GÖRÜNMÜYORDU — silinseler çıra ötmezdi.
#
# Cırcırın var oluş sebebi olan hata, cırcırın KENDİ KÖR NOKTASINDAYDI.
# Bulunuşu tesadüfe yakındı: bir pakette 11 test eklendi, gevşeklik 10
# çıktı; aradaki 1 kovalanınca ortaya çıktı.
#
# `[SkippableFact]`i desene eklemek yetmez — yarın başka bir öznitelik
# gelir ve aynı sessizlik tekrarlanır.
#
# ═══ ÇÖZÜM: İKİ BAĞIMSIZ SAYIM ═══
#
#   1. ÇIRA   : kaynak dosyalardan öznitelik sayarak (statik)
#   2. KOŞUCU : `dotnet test --list-tests` ile keşfederek (dinamik)
#
# İkisi AYNI sayıyı vermeli. Vermiyorsa çıra bir şeyi TANIMIYOR
# demektir — ve hangi şey olduğunu bilmesine gerek yok, uyuşmazlığın
# kendisi yeterli.
#
# ÖLÇÜLDÜ: kaynakta 2533 metot ([Fact] 2364 + [SkippableFact] 4 +
# [Theory] 165), koşucuda 2533 farklı metot. Birebir.
#
# ═══ NEDEN METOT, NEDEN DURUM DEĞİL ═══
#
# Koşucu 3059 DURUM buluyor (teori satırları ayrı ayrı). Çıra ise
# BİLDİRİM sayıyor. İkisi farklı eksen ve karşılaştırılamaz.
# Karşılaştırılabilir tek eksen METOT sayısı: bir `[Theory]` kaç durum
# üretirse üretsin tek metottur.
#
# ═══ NEREYE YAZILIYOR ═══
#
# Depo DIŞINA (`/var/lib/enderun-ai`). Depoya yazsaydı her yayın
# çalışma ağacını kirletirdi ve `require_clean_git_tree` düşerdi.
set -uo pipefail

KOK="${REPO_ROOT:-/var/www/enderun-ai}"
DURUM_DIZINI="${DEPLOY_STATE_DIR:-/var/lib/enderun-ai}"
HEDEF="${DURUM_DIZINI}/kosucu-test-sayisi.txt"
PROJE="${KOK}/backend/EnderunAI.Api.Tests/EnderunAI.Api.Tests.csproj"

if [ -z "${TEST_DB_CONNECTION:-}" ] && [ -r /etc/enderunai/backend.env ]; then
    canli="$(sudo grep -E '^DB_CONNECTION=' /etc/enderunai/backend.env 2>/dev/null \
             | sed -E 's/^DB_CONNECTION=//' | tr -d "'\"")"
    if [ -n "$canli" ]; then
        export TEST_DB_CONNECTION="${canli//Database=enderun_ai;/Database=enderun_ai_test;}"
        export DB_CONNECTION="$TEST_DB_CONNECTION"
    fi
fi

ham="$(mktemp)"

# `--no-build` VARSAYILAN DEĞİL, İSTEĞE BAĞLI: yayın turunda derleme
# zaten sıcak; tek başına koşulduğunda derlemesi gerekir.
if ! dotnet test "$PROJE" --list-tests ${1:-} -v q --nologo > "$ham" 2>&1; then
    echo "[kosucu-sayim] HATA: test keşfi başarısız." >&2
    tail -5 "$ham" >&2
    rm -f "$ham"
    exit 1
fi

# Teori argümanları soyulup FARKLI METOT sayılıyor.
sayi="$(grep "^    " "$ham" | sed 's/^ *//; s/(.*//' | sort -u | wc -l)"
rm -f "$ham"

if [ "$sayi" -lt 100 ]; then
    echo "[kosucu-sayim] HATA: yalnız ${sayi} metot bulundu; keşif boşa düşmüş." >&2
    exit 1
fi

mkdir -p "$DURUM_DIZINI" 2>/dev/null || true
{
    echo "# Koşucunun bulduğu FARKLI TEST METODU sayısı."
    echo "# Çıra bunu kendi statik sayımıyla karşılaştırır."
    echo "# Bu dosya depo DIŞINDA: yayın çalışma ağacını kirletmesin."
    echo "COMMIT=$(cd "$KOK" && git rev-parse HEAD 2>/dev/null || echo '-')"
    echo "ZAMAN=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "METOT=${sayi}"
} > "$HEDEF"

echo "[kosucu-sayim] ${sayi} farklı test metodu → ${HEDEF}"
