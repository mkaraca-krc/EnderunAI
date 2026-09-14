#!/usr/bin/env bash
#
# ═══════════════════════════════════════════════════════════════════
# GECE TAM TAKIM — YAYIN SABAHI ÖĞRENİLEN HER ŞEY BİR GECE ÖNCE
# ÖĞRENİLEBİLİRDİ
# ═══════════════════════════════════════════════════════════════════
#
# ═══ DOĞURAN OLAY (2026-09-14) ═══
#
# `PsqlCizgisiTests` **iki gündür kırmızıydı** (paket C'nin kurtarma
# betiği 5 doğrudan psql çağrısı ekledi, çizgi 0'dı) ve kimse görmedi:
# bütün koşular SÜZGEÇLİYDİ. Tam takım ilk kez yayın hazırlığında
# koşuldu ve kırmızıyı orada bulduk.
#
# `safe-deploy` tam takımı koşuyor (süzgeç YOK) — yani bu kırmızı
# **yayın sabahı 04:30'da**, yayın penceresinin içinde görünecekti.
# İyi bir kapı, ama pahalı bir saat.
#
# ═══ SESSİZLİK YEŞİL DEĞİLDİR ═══
#
# Damga dosyası sonucun KENDİSİNİ ve YAŞINI yazar. Koşu hiç olmadıysa
# damga eskir ve bu görünür. Damganın yokluğu "sorun yok" değil,
# "ÖLÇÜLMEDİ" demektir (Kural 48 / Kural 74).
#
set -uo pipefail
KOK=/var/www/enderun-ai
DAMGA=/var/lib/enderun-ai/tam-takim-son.txt
BASLANGIC=$(date +%s)

log() { printf '%s [%s] TAM-TAKIM: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$1" "$2"; }

yaz() {  # $1 sonuc · $2 gecti · $3 dusen · $4 toplam · $5 not
    mkdir -p "$(dirname "$DAMGA")"
    {
        printf 'sonuc=%s\n' "$1"
        printf 'zaman=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
        printf 'sure_sn=%s\n' "$(( $(date +%s) - BASLANGIC ))"
        printf 'gecti=%s\n' "$2"
        printf 'dusen=%s\n' "$3"
        printf 'toplam=%s\n' "$4"
        printf 'dal=%s\n' "$(git -C "$KOK" rev-parse --abbrev-ref HEAD)"
        printf 'commit=%s\n' "$(git -C "$KOK" rev-parse --short HEAD)"
        [ -n "${5:-}" ] && printf 'not=%s\n' "$5"
    } > "$DAMGA"
    chmod 644 "$DAMGA"
}

# Ortam: testler uygulamanın Host'unu ayağa kaldırıyor.
CANLI="$(grep -E '^DB_CONNECTION=' /etc/enderunai/backend.env 2>/dev/null | sed -E 's/^DB_CONNECTION=//' | tr -d "'\"")"
if [ -z "$CANLI" ]; then
    log ERROR "DB_CONNECTION okunamadı — koşu YAPILMADI."
    yaz OLCEMEDI 0 0 0 "ortam okunamadı"
    exit 3
fi
export TEST_DB_CONNECTION="${CANLI//Database=enderun_ai;/Database=enderun_ai_test;}"
export DB_CONNECTION="$TEST_DB_CONNECTION"
export JWT_SECRET="TEST-deploy-script-jwt-secret-0123456789"
export DOTNET_CLI_HOME=/root HOME=/root

case "$TEST_DB_CONNECTION" in
    *Database=enderun_ai_test\;*) ;;
    *) log ERROR "HEDEF TEST DEĞİL — koşu YAPILMADI."; yaz OLCEMEDI 0 0 0 "hedef test değil"; exit 9 ;;
esac

CIKTI=$(mktemp)
cd "$KOK/backend" || { yaz OLCEMEDI 0 0 0 "dizin yok"; exit 3; }

log INFO "tam takım başlıyor (dal: $(git -C "$KOK" rev-parse --abbrev-ref HEAD))"
"$KOK/scripts/derleme-kos.sh" dotnet test EnderunAI.Api.Tests/EnderunAI.Api.Tests.csproj \
    -c Release --nologo -v q > "$CIKTI" 2>&1
KOD=$?

# ÇIKIŞ 75 = başka bir derleme koşuyor. Bu bir BAŞARISIZLIK DEĞİL,
# ÖLÇEMEDİ'dir — "yeşil" demek en tehlikeli yanlış olurdu.
if [ "$KOD" -eq 75 ]; then
    log WARN "derleme kilidi meşgul — koşu YAPILMADI (ÖLÇEMEDİ)."
    yaz OLCEMEDI 0 0 0 "derleme kilidi meşgul (çıkış 75)"
    rm -f "$CIKTI"; exit 3
fi

OZET=$(grep -aE "^(Passed!|Failed!)" "$CIKTI" | tail -1)
GECTI=$(printf '%s' "$OZET" | sed -nE 's/.*Passed:[[:space:]]*([0-9]+).*/\1/p')
DUSEN=$(printf '%s' "$OZET" | sed -nE 's/.*Failed:[[:space:]]*([0-9]+).*/\1/p')
TOPLAM=$(printf '%s' "$OZET" | sed -nE 's/.*Total:[[:space:]]*([0-9]+).*/\1/p')

if [ -z "$TOPLAM" ]; then
    log ERROR "özet satırı okunamadı — koşu sonucu bilinmiyor."
    yaz OLCEMEDI 0 0 0 "özet satırı yok"
    rm -f "$CIKTI"; exit 3
fi

if [ "${DUSEN:-1}" -eq 0 ]; then
    log INFO "TAM TAKIM YEŞİL — ${GECTI}/${TOPLAM}, $(( $(date +%s) - BASLANGIC ))sn"
    yaz YESIL "$GECTI" "$DUSEN" "$TOPLAM" ""
else
    log ERROR "TAM TAKIM KIRMIZI — ${DUSEN} düştü / ${TOPLAM}"
    grep -a "\[FAIL\]" "$CIKTI" | head -20 | while IFS= read -r s; do log ERROR "  $s"; done
    yaz KIRMIZI "$GECTI" "$DUSEN" "$TOPLAM" "$(grep -ac '\[FAIL\]' "$CIKTI") düşen test"
fi
rm -f "$CIKTI"
