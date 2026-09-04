#!/usr/bin/env bash
#
# DEPLOY KOŞUYOR MU — TEK KAYNAK, KAPALI TARAFA DÜŞER.
#
# ═══ KURAL ═══
#
# Bir işin bittiği YALNIZ pozitif bir bitiş işaretiyle bilinir.
# İşaretin yokluğu "bitti" demek DEĞİLDİR. Belirsizlik, kuralı SIKAN
# yöne okunur — yani "koşuyor" kabul edilir.
#
# ═══ DOĞURAN OLAY (2026-09-04) ═══
#
# Deploy'un bittiğini iki DOLAYLI sinyalden çıkardım: `pgrep -af
# safe-deploy` boş döndü ve günlükte özet yoktu. İkisi de yanılttı —
# `pgrep` desenim `sudo setsid bash -c` sarmalayıcısını yakalamıyordu,
# günlük ise henüz özet satırını yazmamıştı.
#
# Elimde AKSİNİ söyleyen bir sinyal vardı ve onu yanlış okudum:
# bitiş satırı YOKTU. Sonuç: safe-deploy koşarken `safe-deploy.sh`i
# düzenledim. Bash betiği artımlı okur; ortasına satır eklemek sonraki
# bayt konumlarını kaydırır ve süreç bozuk bir parça çalıştırabilir.
# Bozulmadı — ama bu şanstı.
#
# ═══ NEDEN pgrep DEĞİL ═══
#
# `pgrep` bir DOLAYLI sinyal: desen tutmazsa "yok" der ve bu "bitti"
# ile aynı görünür. Bitiş işareti ise betiğin KENDİ beyanı.
#
# ═══ ÇIKIŞ KODU ═══
#
#   0 → KOŞUYOR (ya da durumu bilinmiyor — kapalı taraf)
#   1 → BİTTİ   (sonuç ekrana basılır)
#
# Kullanım:
#   if deploy-kosuyor-mu.sh >/dev/null; then echo "bekle"; fi

set -uo pipefail

DEPLOY_STATE_DIR="${DEPLOY_STATE_DIR:-/var/lib/enderun-ai}"
SON_KOSU_DOSYASI="${SON_KOSU_DOSYASI:-${DEPLOY_STATE_DIR}/son-kosu}"
YARIM_KOSU_DOSYASI="${YARIM_KOSU_DOSYASI:-${DEPLOY_STATE_DIR}/yarim-kosu}"

if [ ! -f "$SON_KOSU_DOSYASI" ]; then
    echo "KOŞUYOR — pozitif bitiş işareti yok (${SON_KOSU_DOSYASI})."
    echo "İşaretin YOKLUĞU 'bitti' demek değildir."
    if [ -f "$YARIM_KOSU_DOSYASI" ]; then
        echo "  aşama: $(sed -n 2p "$YARIM_KOSU_DOSYASI" 2>/dev/null || echo bilinmiyor)"
    fi
    exit 0
fi

# İŞARET VAR AMA YARIM-KOŞU İZİ DE VARSA: yeni bir koşu başlamış ve
# henüz bitmemiş olabilir. Kapalı tarafa düşülür.
if [ -f "$YARIM_KOSU_DOSYASI" ]; then
    echo "KOŞUYOR — bitiş işareti eski, yarım-koşu izi var."
    echo "  aşama: $(sed -n 2p "$YARIM_KOSU_DOSYASI" 2>/dev/null || echo bilinmiyor)"
    exit 0
fi

echo "BİTTİ"
sed 's/^/  /' "$SON_KOSU_DOSYASI"
exit 1
