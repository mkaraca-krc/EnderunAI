#!/usr/bin/env bash
#
# ═══════════════════════════════════════════════════════════════════
# OKUNMAMIŞ KIRMIZI DEFTERİ — KIRMIZININ GİDECEĞİ YER
# ═══════════════════════════════════════════════════════════════════
#
# ═══ NEDEN VAR (ölçüldü, 2026-09-15) ═══
#
# `enderun-sorgu-cirasi` 15 Eylül 00:20'de KIRMIZI yandı ve **15 saat
# boyunca kimse görmedi**. Kapı doğru çalıştı, doğru yandı, doğru şeyi
# söyledi — ve kimseye ulaşmadı. Ben `Result=exit-code` taramasında
# tesadüfen rastladım.
#
# Mehmet Bey'in kuralı (Kural 97): "Kimseye ulaşmayan bir kırmızı,
# yanmamış kırmızıyla aynı sınıftır. Her kapının İKİ sorusu vardır:
# ısırıyor mu, ve ısırdığında KİM GÖRÜYOR?"
#
# ═══ NEDEN YENİ BİR TESLİM KANALI KURULMADI ═══
#
# E-posta bir SMTP sırrı ister, sır bir yerde durur, ve o kanal bir gün
# SESSİZCE düşer — yani gürültüsüz bir kanal, gürültüsüz bir kırmızı
# üretir. Aynı tuzağın ikinci hâli olurdu.
#
# Bunun yerine kırmızı, ZATEN OKUNAN iki yere bağlanıyor:
#   1. sabah raporu — dosyayı okur, içeriğini EN BAŞA basar
#   2. yayın uçuş öncesi — okunmamış kırmızı varsa YAYIN DURUR
#
# İkincisi üst sınırı koyar: bir kırmızı en geç BİR SONRAKİ YAYINDA
# görülür. Kimsenin bakmadığı bir dosya değil, yayını durduran bir kapı.
#
# ═══ KULLANIM ═══
#
#   kirmizi-kaydet.sh <birim-adi> [sebep]
#
# systemd'den: her zamanlayıcı biriminde
#   OnFailure=enderun-kirmizi-kaydet@%n.service
#
# Sebep verilmezse birimin kendi günlüğünden son anlamlı satır okunur.
#
set -uo pipefail

DEFTER="${KIRMIZI_DEFTERI:-/var/lib/enderun-ai/okunmamis-kirmizilar.txt}"
BIRIM="${1:?birim adı gerekli}"
SEBEP="${2:-}"

mkdir -p "$(dirname "$DEFTER")" 2>/dev/null || true

if [ -z "$SEBEP" ]; then
    # Birimin son koşusundan TEK satır sebep. Uzun yığın izleri defteri
    # okunmaz yapar; defterin işi haber vermek, teşhis değil.
    SEBEP="$(journalctl -u "$BIRIM" -n 200 --no-pager -o cat 2>/dev/null \
        | grep -iE 'KIRMIZI|HATA|ERROR|BAŞARISIZ|FAIL' \
        | grep -viE 'hatasız|error\.log' \
        | tail -1 | cut -c1-200)"
fi

[ -n "$SEBEP" ] || SEBEP="(sebep okunamadı — journalctl'e bakın: journalctl -u $BIRIM)"

# `|` ayraç; sebepteki ayraçlar temizleniyor ki satır ayrıştırılabilsin.
SEBEP="$(printf '%s' "$SEBEP" | tr '|' '/' | tr -d '\r\n')"

printf '%s | %s | %s\n' \
    "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" "$BIRIM" "$SEBEP" >> "$DEFTER"

echo "[kirmizi-kaydet] deftere yazıldı: $BIRIM" >&2
