#!/usr/bin/env bash
#
# SONDA KOŞUCUSU — KİRLİ AĞAÇTA SONDA KOŞMAZ.
#
# ═══ NEDEN VAR (2026-09-06, bir kaybın üstüne) ═══
#
# Bir sondayı kurmak için `git stash -u` → yeni dal → `stash pop` →
# commit yapıldı. Bu, HENÜZ COMMIT EDİLMEMİŞ bir düzeltmeyi atılacak
# dalın içine gömdü; dal silinince düzeltme de gitti ve aynı kapı
# ikinci kez düştü. Düzeltme reflog'dan tek dosya alınarak kurtarıldı.
#
# Kural 73 bunu ANLATIYOR ("commit edilmemiş iş sağ çıkmaz") ama
# UYGULAMIYOR. Bu betik uygulatıyor. Göç betiğindeki ön koşul
# denetiminin aynısı: yarıda düşen bir sonda, hiç başlamayan sondadan
# pahalıdır.
#
# ═══ NE YAPMAZ ═══
#
# Sondanın kendisini bilmez, yorumlamaz, temizlemez. Tek işi: ağaç
# kirliyse KOMUTU HİÇ ÇALIŞTIRMAMAK.
#
# KULLANIM:
#   sonda-kos.sh <komut> [argüman...]
#   sonda-kos.sh --kirli-kabul <komut>   # bilerek kirli ağaçta koşan
#                                        # sondalar için (ör. çalışma
#                                        # ağacını değiştirip geri alan)

set -uo pipefail

REPO_ROOT="${REPO_ROOT:-/var/www/enderun-ai}"
KIRLI_KABUL=0

if [ "${1:-}" = "--kirli-kabul" ]; then
    KIRLI_KABUL=1
    shift
fi

[ $# -gt 0 ] || { echo "[sonda] KULLANIM: sonda-kos.sh [--kirli-kabul] <komut> ..." >&2; exit 2; }

DEGISIKLIK="$(cd "$REPO_ROOT" && git status --porcelain 2>/dev/null | wc -l)"

if [ "$KIRLI_KABUL" = "0" ] && [ "$DEGISIKLIK" -ne 0 ]; then
    echo "[sonda] DURDU — çalışma ağacında ${DEGISIKLIK} değişiklik var." >&2
    echo "[sonda] Önce commit et ya da temizle. Sonda, commit edilmemiş" >&2
    echo "[sonda] işle bir arada koşmaz: dal/stash hareketi o işi yutar." >&2
    echo "[sonda] Bilerek kirli ağaçta koşan bir sonda ise: --kirli-kabul" >&2
    (cd "$REPO_ROOT" && git status --short | head -10) >&2
    exit 1
fi

if [ "$KIRLI_KABUL" = "1" ]; then
    echo "[sonda] UYARI: --kirli-kabul verildi (${DEGISIKLIK} değişiklik)."
    echo "[sonda] Sondanın kendi temizliğini yapması SENİN sorumluluğunda."
fi

echo "[sonda] ağaç temiz · koşuluyor: $*"
"$@"
SONUC=$?

# SONDA SONRASI AĞAÇ: sondalar iz bırakmamalı. Bırakmışsa söylenir —
# temizlenmez, çünkü neyin kasıtlı olduğunu bu betik bilemez.
SONRA="$(cd "$REPO_ROOT" && git status --porcelain 2>/dev/null | wc -l)"
if [ "$KIRLI_KABUL" = "0" ] && [ "$SONRA" -ne 0 ]; then
    echo "[sonda] DİKKAT: sonda ağaçta ${SONRA} değişiklik BIRAKTI." >&2
    (cd "$REPO_ROOT" && git status --short | head -10) >&2
fi

exit "$SONUC"
