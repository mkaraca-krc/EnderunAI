#!/usr/bin/env bash
#
# VEKİL/1 ÖLÇÜMÜ — VEKİLDEN GEÇEN İSTEĞİN IP'Sİ DENETİM KAYDINA DOĞRU
# DÜŞÜYOR MU?
#
# ═══ NE ÖLÇER (Mehmet Bey'in tarif ettiği ölçüm, 2026-09-15) ═══
#
# "Vekilden geçen bir istekte denetim kaydına düşen IP, isteği atan
#  gerçek adres mi? Pozitif kontrol: iki farklı adresten iki istek,
#  iki farklı IP kaydı."
#
# KATMAN (Kural 91): **Next.js vekili → .NET arka ucu →
# `security_audit_events`.** nginx ayağı BİLEREK ATLANIYOR ve sebebi
# şu: sonda bu sunucudan koşuyor, nginx `$proxy_add_x_forwarded_for`
# ile gerçek eşi (127.0.0.1) zincirin SONUNA ekliyor, arka uç da son
# elemanı okuyor. Yani nginx üzerinden giden iki sondanın ikisi de
# 127.0.0.1 kaydeder ve ölçüm DÜZELTME ÇALIŞSA DA kırmızı görünürdü —
# alet, ölçmek istediği şeyi göremezdi.
#
# nginx ayağı AYRICA doğrulandı (2026-09-15): yapılandırmada vekil yapan
# 8 bloğun 8'i de `X-Forwarded-For $proxy_add_x_forwarded_for` kuruyor.
# Kopma orada değildi; Next.js rotalarındaydı ve ölçüm oraya bakıyor.
#
# ═══ NEDEN `/api/backend/auth/login` (ve `/api/auth/login` DEĞİL) ═══
#
# `/api/auth/login` rotası adresi ZATEN taşıyordu — kusur orada hiç
# yoktu. Sondayı oraya kurarsam düzeltmeden ÖNCE de yeşil yanar ve
# hiçbir şey ölçmez. İlk yazımımda tam bunu yapmıştım; Kural 93'ün
# doğduğu hata aynı oturumda ikinci kez karşıma çıktı.
#
# `/api/backend/auth/login` ise kusurlu `[...path]` vekilinden geçer ve
# aynı denetim satırını yazar. Ölçüm oraya bakar.
#
# ═══ NEDEN BAŞARISIZ GİRİŞ KULLANILIYOR ═══
#
# Gözlenebilir tek yazma yolu bu: `LoginFailed` satırı denetim kaydına
# kullanıcı adı + IP yazıyor ve CANLI MUHASEBEYE HİÇBİR ŞEY EKLEMİYOR.
# Kullanıcı adları `sonda-vekil-` önekli, var olmayan adlar; gerçek bir
# hesabın sayacına dokunulmaz.
#
# ═══ POZİTİF KONTROL ZORUNLU (Kural 48 + Kural 93) ═══
#
# İki İSTEK iki FARKLI adresle gönderilir. Kayda iki FARKLI IP düşmek
# zorunda. Aynı düşerlerse kusur sürüyor demektir. Ayrıca sonda kendi
# ısırganlığını da kanıtlar: kusurlu davranışın imzası "iki istek, TEK
# ve aynı IP" olduğu için, iki farklı IP görmek ısırabilen bir sondanın
# yeşilidir — Kural 93'ün istediği budur.
#
# ÇIKIŞ: 0 YEŞİL · 1 KIRMIZI · 3 ÖLÇEMEDİ
#
set -uo pipefail

KOK_DIZIN="${KOK_DIZIN:-/var/www/enderun-ai}"
UC="${UC:-http://127.0.0.1:3000/api/backend/auth/login}"
VT="${VT:-enderun_ai}"
DAMGA="sonda-vekil-$(date -u +%s)"
A="203.0.113.41"
B="198.51.100.62"

log() { echo "[vekil-olcum] $*"; }

vur() {
    local ip="$1" kul="$2"
    curl -s -o /dev/null -w '%{http_code}' -X POST "$UC" \
        -H 'Content-Type: application/json' \
        -H "X-Forwarded-For: $ip" \
        -d "{\"username\":\"$kul\",\"password\":\"onemsiz\"}" \
        --max-time 15
}

# Vekile DOĞRUDAN vurulduğu için gönderilen zincir aynen o rotaya
# ulaşır. Düzeltme varsa vekil zinciri arka uca AYNEN geçirir ve kayda
# A ile B ayrı ayrı düşer; düzeltme yoksa başlık yolda düşer ve ikisi
# de 127.0.0.1 olur. Aradaki fark ölçümün kendisidir.
kod_a="$(vur "$A" "${DAMGA}-a")"
kod_b="$(vur "$B" "${DAMGA}-b")"
log "istek A (XFF=$A) -> HTTP $kod_a"
log "istek B (XFF=$B) -> HTTP $kod_b"

if [ "$kod_a" = "429" ] || [ "$kod_b" = "429" ]; then
    log "ÖLÇEMEDİ: istek hız sınırına takıldı; kayıt oluşmadı."
    log "Bu bir onay DEĞİLDİR. Kilit düşünce tekrar koşturun."
    exit 3
fi

# ÖLÇÜM KANONİK ARAÇTAN GEÇER — DOĞRUDAN psql DEĞİL.
#
# İlk yazımımda burada `sudo -u postgres psql` vardı ve `PsqlCizgisiTests`
# bunu yakalayıp 2026-09-15 gece YAYINI DURDURDU (çizgi 0, ben 1 yaptım).
# Çıra haklıydı: `vt-sorgu.sh` veritabanı adını ZORUNLU kılar, bakım
# veritabanlarını reddeder ve her çıktının başına `current_database()`
# basar — yani "yanlış veritabanını ölçtüm" hatası yapılamaz. Bu betik
# tam da bir ölçüm aleti olduğu için o güvenceye en çok ihtiyacı olan
# yer burası. Çizgiyi yükseltmek gerekmedi.
satirlar="$("${KOK_DIZIN}/deploy/scripts/vt-sorgu.sh" --vt "$VT" --sql "
    select \"ActorUsername\" || '|' || coalesce(\"IpAddress\", '')
    from security_audit_events
    where \"Action\" = 'LoginFailed'
      and \"ActorUsername\" like '${DAMGA}%'
    order by \"OccurredAtUtc\";" 2>/dev/null | grep -E '^sonda-vekil-')"

adet="$(printf '%s\n' "$satirlar" | grep -c . || true)"
if [ "$adet" != "2" ]; then
    log "ÖLÇEMEDİ: beklenen 2 kayıt, bulunan $adet."
    log "Kayıt oluşmadıysa ölçülecek bir şey yoktur — onay değildir."
    exit 3
fi

printf '%s\n' "$satirlar" | while IFS='|' read -r k i; do
    log "  kayıt: $k -> $i"
done

farkli="$(printf '%s\n' "$satirlar" | cut -d'|' -f2 | sort -u | grep -c . || true)"

if [ "$farkli" -lt 2 ]; then
    log "KIRMIZI: iki farklı adresten gelen iki istek, kayda AYNI IP olarak düştü."
    log "Vekil istemci adresini taşımıyor (VEKİL/1)."
    exit 1
fi

log "YEŞİL: iki istek, iki FARKLI IP kaydı (karşılaştırılan kayıt: 2)."
exit 0
