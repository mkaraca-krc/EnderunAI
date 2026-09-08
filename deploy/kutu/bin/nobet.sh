#!/usr/bin/env bash
#
# ═══════════════════════════════════════════════════════════════════
# NÖBET/1 — DÜZELDİ BİLDİRİMİ (K8, 2. adım)
# ═══════════════════════════════════════════════════════════════════
#
# ═══ NE YAPIYOR ═══
#
# `enderun-uyari.sh` bir arıza bildirdiğinde
# `/var/lib/enderun-ai/nobet/<birim>.durum` dosyasını `hal=DUSTU`
# olarak bırakıyor. Bu betik açık arıza kayıtlarını gezip birimin
# toparlanıp toparlanmadığına bakıyor; toparlandıysa "düzeldi"
# postasını gönderiyor ve kaydı kapatıyor.
#
# ═══ NEDEN AYRI BİR BETİK ═══
#
# `OnFailure=` yalnız DÜŞÜŞTE ateşlenir. Toparlanmayı görecek kimse
# yoktu: bir arıza bildirildikten sonra sistem kendi kendine düzelse
# bile posta kutusundaki son satır hâlâ "DUSTU" derdi. Nöbetçinin
# tek işi bu boşluğu kapatmak.
#
# TESLİM YOLU BURADA DEĞİL: posta `enderun-uyari.sh` üzerinden
# gidiyor. SMTP, alıcı listesi, posta kapısı ve kuru koşu tek yerde
# duruyor (Kural 79).
#
# ═══ DÜRÜST SINIR ═══
#
# Bu betik izlediği sunucunun İÇİNDE koşuyor. Sunucu ölürse nöbetçi
# de ölür. NÖBET/1 "servis bozuldu"yu yakalar, "sunucu öldü"yü
# yakalamaz — dışarıdan bakan göz AK-7'nin işi.
#
set -uo pipefail

DURUM_DIZINI="/var/lib/enderun-ai/nobet"
UYARI="/usr/local/bin/enderun-uyari.sh"
GUNLUK="/var/log/enderun-uyari.log"
ZAMAN="$(TZ=Europe/Istanbul date '+%Y-%m-%d %H:%M:%S') TR"

[ -d "$DURUM_DIZINI" ] || exit 0

acik=0
duzelen=0

for dosya in "$DURUM_DIZINI"/*.durum; do
    [ -e "$dosya" ] || continue
    acik=$((acik + 1))

    birim="$(sed -n 's/^birim=//p' "$dosya" | head -1)"
    hal="$(sed -n 's/^hal=//p' "$dosya" | head -1)"

    # Birim adı kayıtta yoksa kaydı SİLMİYORUZ: silmek, arızayı
    # görünmez yapardı. Söyleyip geçiyoruz.
    if [ -z "$birim" ]; then
        printf '%s [NOBET] durum dosyasinda birim adi yok: %s\n' \
            "$ZAMAN" "$dosya" >> "$GUNLUK"
        continue
    fi

    [ "$hal" = "DUSTU" ] || continue

    # ── TOPARLANDI MI ─────────────────────────────────────────────
    #
    # İKİ BİRİM TÜRÜ, İKİ AYRI CEVAP:
    #
    #   · Sürekli koşan (backend, frontend) — toparlanma "active".
    #   · oneshot (yedek, tatbikat) — başarıyla bittiğinde "inactive"
    #     olur. Yalnız "active mi" diye sorsaydım bu birimler ASLA
    #     düzelmiş sayılmazdı ve "düzeldi" postası hiç gitmezdi.
    #
    # `is-failed` tek başına da yetmez: elle durdurulmuş sürekli bir
    # servis de "failed" değildir ama ayakta DEĞİLDİR. Onu düzeldi
    # saymak, kapalı bir servisi iyileşmiş göstermek olurdu.
    tur="$(systemctl show "$birim" -p Type --value 2>/dev/null)"
    aktif="$(systemctl is-active "$birim" 2>/dev/null)"
    dusuk="$(systemctl is-failed "$birim" 2>/dev/null)"

    toparlandi=0
    if [ "$tur" = "oneshot" ]; then
        [ "$dusuk" != "failed" ] && toparlandi=1
    else
        [ "$aktif" = "active" ] && [ "$dusuk" != "failed" ] && toparlandi=1
    fi

    if [ "$toparlandi" = "1" ]; then
        duzelen=$((duzelen + 1))
        "$UYARI" "$birim" duzeldi || true
    fi
done

printf '%s [NOBET] acik ariza=%s duzelen=%s\n' "$ZAMAN" "$acik" "$duzelen" >> "$GUNLUK"
