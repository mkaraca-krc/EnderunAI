#!/usr/bin/env bash
#
# ═══════════════════════════════════════════════════════════════════
# YAYIN GERİ ALMA — TEK KOMUT, ÖNCEDEN YAZILMIŞ
# ═══════════════════════════════════════════════════════════════════
#
# ═══ NEDEN VAR (ÖLÇÜLDÜ 2026-09-14) ═══
#
# `safe-deploy.sh` içinde bir `rollback()` var ama YALNIZ sağlık
# kontrolü penceresinde, yayının kendi koşusu sırasında çalışıyor.
# Pencere kapandıktan SONRA — yayın "başarılı" sayılıp yirmi dakika
# geçtikten sonra bir arıza fark edilirse — tek komutluk bir geri alma
# YOKTU. Olayın ortasında komut yazmak, komut yazmamaktır.
#
# ═══ NE YAPAR ═══
#
#   1. Ön koşulları DOĞRULAR (yedek dizinler var mı, tam mı)
#   2. Arka uç ve ön yüz yapısını önceki sürüme geri kor
#   3. Servisleri yeniden başlatır
#   4. Sağlık kontrolü koşar; geçmezse AÇIKÇA söyler
#
# ═══ NE YAPMAZ — SINIR AÇIK ═══
#
# **GÖÇÜ GERİ ALMAZ.** Şema değişikliği içeren bir yayında kod geri
# alınır, şema ileride kalır. Bu, çoğu göç için zararsızdır (yeni
# sütun/tablo eski kodu ilgilendirmez) ama YIKICI bir göç için
# değildir. Göç geri alma SQL'i ayrı üretilir ve AYRI koşulur:
#
#     dotnet ef migrations script <hedef> <onceki> --context AppDbContext
#
# ═══ KULLANIM ═══
#
#   deploy/scripts/geri-al.sh --prova    # hiçbir şeye dokunmaz, sınar
#   deploy/scripts/geri-al.sh --uygula   # geri alır
#
set -uo pipefail

KOK="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ARKA="${KOK}/publish"
ARKA_YEDEK="${KOK}/publish-rollback"
ON="${KOK}/frontend/enderun-ai/.next"
ON_YEDEK="${KOK}/frontend-next-rollback"
SERVISLER=(enderunai-backend enderunai-frontend)

log() { printf '%s [%s] GERİ-AL: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$1" "$2"; }
dur() { log ERROR "$1"; exit 1; }

KIP="${1:-}"
case "$KIP" in
    --prova|--uygula) ;;
    *) echo "kullanım: $0 --prova | --uygula" >&2; exit 64 ;;
esac

# ── ÖN KOŞULLAR ─────────────────────────────────────────────────────
#
# "Yedek dizin var" yetmez: yarım bir publish de dizin olarak vardır.
# Çalıştırılabilir çekirdeği ARANIR.
HATA=0

if [ -f "${ARKA_YEDEK}/EnderunAI.Api.dll" ]; then
    log INFO "arka uç yedeği HAZIR: $(du -sh "$ARKA_YEDEK" | cut -f1), dll $(stat -c %y "${ARKA_YEDEK}/EnderunAI.Api.dll" | cut -d. -f1)"
else
    log ERROR "arka uç yedeği YOK ya da yarım: ${ARKA_YEDEK}/EnderunAI.Api.dll bulunamadı"
    HATA=1
fi

if [ -f "${ON_YEDEK}/BUILD_ID" ]; then
    log INFO "ön yüz yedeği HAZIR: $(du -sh "$ON_YEDEK" | cut -f1), BUILD_ID $(cat "${ON_YEDEK}/BUILD_ID")"
else
    log ERROR "ön yüz yedeği YOK ya da yarım: ${ON_YEDEK}/BUILD_ID bulunamadı"
    HATA=1
fi

# Şu an canlıda ne var — geri almadan önce yazılır ki neye döndüğümüz bilinsin.
if [ -f "${ARKA}/EnderunAI.Api.dll" ]; then
    log INFO "şu anki arka uç: dll $(stat -c %y "${ARKA}/EnderunAI.Api.dll" | cut -d. -f1)"
fi
if [ -f "${ON}/BUILD_ID" ]; then
    log INFO "şu anki ön yüz : BUILD_ID $(cat "${ON}/BUILD_ID")"
fi

SURUM="$(curl -s --max-time 5 http://127.0.0.1:5155/api/health 2>/dev/null \
         | sed -nE 's/.*"surum":"([^"]*)".*/\1/p')"
[ -n "$SURUM" ] && log INFO "çalışan sürüm (uçtan): $SURUM"

if [ "$HATA" -ne 0 ]; then
    dur "ÖN KOŞULLAR TUTMADI — geri alma YAPILMADI. Elle müdahale gerekir."
fi

if [ "$KIP" = "--prova" ]; then
    log INFO "PROVA: ön koşullar tam. Gerçek koşuda yapılacaklar:"
    log INFO "  1) ${ARKA} ← ${ARKA_YEDEK}"
    log INFO "  2) ${ON} ← ${ON_YEDEK}"
    log INFO "  3) systemctl restart ${SERVISLER[*]}"
    log INFO "  4) healthcheck.sh"
    log WARN "  GÖÇ GERİ ALINMAZ — şema değişikliği varsa ayrı SQL koşulmalı."
    exit 0
fi

# ── UYGULA ──────────────────────────────────────────────────────────
log WARN "GERİ ALMA BAŞLIYOR."

rm -rf "${ARKA}.geri-alinan" && mv "$ARKA" "${ARKA}.geri-alinan" \
    && cp -a "$ARKA_YEDEK" "$ARKA" || dur "arka uç geri konamadı"
log INFO "arka uç geri kondu (eski hâl: ${ARKA}.geri-alinan)"

rm -rf "${ON}.geri-alinan" && mv "$ON" "${ON}.geri-alinan" \
    && cp -a "$ON_YEDEK" "$ON" || dur "ön yüz geri konamadı"
log INFO "ön yüz geri kondu (eski hâl: ${ON}.geri-alinan)"

for s in "${SERVISLER[@]}"; do
    systemctl restart "$s" || dur "servis başlatılamadı: $s — ELLE MÜDAHALE"
    log INFO "yeniden başlatıldı: $s"
done

if bash "${KOK}/deploy/scripts/healthcheck.sh"; then
    log INFO "GERİ ALMA TAMAM — sağlık kontrolü geçti."
else
    dur "Geri alma sonrası sağlık kontrolü DÜŞTÜ — elle müdahale gerekiyor."
fi

SURUM_SON="$(curl -s --max-time 5 http://127.0.0.1:5155/api/health 2>/dev/null \
             | sed -nE 's/.*"surum":"([^"]*)".*/\1/p')"
[ -n "$SURUM_SON" ] && log INFO "geri alma sonrası sürüm: $SURUM_SON"
log WARN "GÖÇ GERİ ALINMADI. Şema değişikliği varsa geri alma SQL'ini ayrıca koşun."
