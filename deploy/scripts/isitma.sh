#!/usr/bin/env bash
#
# ISINMA/1 — YAYINDAN SONRA İLK KULLANICININ ÖDEDİĞİ BEDELİ YAYIN BETİĞİ ÖDER.
#
# ═══ NEDEN VAR (ölçüm, 2026-09-13) ═══
#
# Derleme ve canlı ERP aynı makinede. Bir temiz derleme ~6,3 GB yerleşik
# istiyor ve makine marjı dar; çekirdek farkı canlı servislerin sayfalarını
# TAKASA yazarak kapatıyor. Derleme canlıyı DÜŞÜRMÜYOR — ama soğutuyor.
# Ölçülen bedel (canlı, derlemeden hemen sonra):
#
#   | yol                          | ilk istek | ısınmış |
#   |------------------------------|-----------|---------|
#   | arka uç, gerçek giriş yolu   | 6,94 sn   | 0,086 sn|
#   | ön yüz /login                | 2,19 sn   | 0,12 sn |
#
# Yani yayından sonra sisteme giren İLK kullanıcı yedi saniye bekliyordu.
# Bu betik o bedeli yayın betiğine ödetir.
#
# ═══ NEDEN `memory.swap.max` DEĞİL ═══
#
# Servislerin takasa yazılmasını yasaklamak semptomu kökten keserdi, ama
# yanlış kurulursa 7 saniyelik gecikmeyi SERVİS ÖLÜMÜNE çevirir: sıkışma
# anında çekirdeğin geri kazanacak anon sayfası kalmaz ve OOM'a gider.
# Semptom hafif, yeni risk ağır. Mehmet Bey'in kararı: canlıya geçmeden o
# takas yapılmaz (EKSİK/1'de duruyor).
#
# ═══ NEDEN BAŞARISIZ GİRİŞ ÇAĞRISI (ve neden tehlikeli değil) ═══
#
# "Sağlık" ucu gövdesiz ve ucuz; arka ucun asıl ağır yollarını (EF Core
# modeli, Npgsql havuzu, JSON, kimlik boru hattı) HİÇ dokundurmuyor —
# ısıtma için işe yaramaz. Gerçek kimlik yolunu çağırmak gerekiyor, ama
# canlıya kayıt açmadan. Çözüm: VAR OLMAYAN bir kullanıcı adıyla giriş
# denemesi. Ölçülerek doğrulandı (2026-09-13):
#   · `LoginAttemptService` YALNIZ BELLEKTE tutuyor — veritabanına yazmaz.
#   · Sayaç IP başına; IP `X-Forwarded-For`ın ilk değerinden okunuyor
#     (çağırarak kanıtlandı: aynı sentetik IP'den 6. deneme 429, farklı
#     IP ve 127.0.0.1 etkilenmedi).
# Bu yüzden ısıtma, HER KOŞUDA YENİ ve asla yönlendirilemeyen bir
# RFC 5737 TEST-NET-1 adresi (192.0.2.x) kullanır ve 5 hatalık kilit
# eşiğinin ALTINDA kalır. Gerçek kullanıcıların geldiği vekil IP'sinin
# sayacına dokunulmaz — dokunulsaydı bir yayın tüm kullanıcıları 15
# dakika kilitleyebilirdi.
#
# ═══ POZİTİF KONTROL (Kural 48) ═══
#
# Bu betiğin işe yaradığı, ATLANDIĞI bir koşuda sürenin yeniden saniyelere
# çıktığı gösterilerek kanıtlanır. Aksi hâlde "hızlı" sonuç, ısıtmanın
# değil ölçüm aletinin duyarsızlığının kanıtı olabilir.
#
set -uo pipefail

ARKA_UC="${ISITMA_ARKA_UC:-http://127.0.0.1:5155}"
ON_YUZ="${ISITMA_ON_YUZ:-http://127.0.0.1:3000}"
DENEME="${ISITMA_DENEME:-3}"          # 5'lik kilit eşiğinin altında kalmalı
ZAMAN_ASIMI="${ISITMA_ZAMAN_ASIMI:-30}"

# Her koşuda yeni, yönlendirilemeyen adres (RFC 5737). Aynı adresin iki
# yayında üst üste kullanılıp 15 dakikalık pencerede eşiği aşmasını
# engeller.
sentetik_ip() { echo "192.0.2.$(( (RANDOM % 250) + 2 ))"; }

# Var olmayan kullanıcı adı: çakışma olasılığını sıfıra yakın tutmak için
# koşuya özgü son ek. Gerçek bir kullanıcıya denk gelirse o kullanıcının
# parolası denenmiş olmaz (parola zaten yanlış) ama sayaç yine sentetik
# IP'de tutulduğu için kimse kilitlenmez.
KULLANICI="isitma-yok-$(date +%s)-$RANDOM"
PAROLA="IsitmaGecersiz!$RANDOM"

sure() {  # yöntem yol [xff] → "kod süre_sn"
  local yontem="$1" url="$2" xff="${3:-}"
  local -a ek=()
  [ -n "$xff" ] && ek+=(-H "X-Forwarded-For: $xff")
  if [ "$yontem" = "POST" ]; then
    ek+=(-X POST -H "Content-Type: application/json"
         -d "{\"username\":\"${KULLANICI}\",\"password\":\"${PAROLA}\"}")
  fi
  curl -s -o /dev/null -m "$ZAMAN_ASIMI" -w '%{http_code} %{time_total}' "${ek[@]}" "$url" 2>/dev/null || echo "000 0"
}

hata=0
echo "ISITMA başlıyor (arka uç=${ARKA_UC} ön yüz=${ON_YUZ} deneme=${DENEME})"

# ── Arka uç: gerçek kimlik doğrulama yolu ────────────────────────────────
ilk_arka=""
for i in $(seq 1 "$DENEME"); do
  okuma="$(sure POST "${ARKA_UC}/api/auth/login" "$(sentetik_ip)")"
  kod="${okuma%% *}"; sn="${okuma##* }"
  [ -z "$ilk_arka" ] && ilk_arka="$sn"
  echo "ISITMA arka-uc deneme=${i} kod=${kod} sure=${sn}s"
  # 401 beklenen cevap (kullanıcı yok). 429 = kilit; ısıtma o çağrıda
  # gerçek yolu koşturmamış demektir, sessiz geçilmez.
  case "$kod" in
    401) ;;
    429) echo "ISITMA UYARI: kilit cevabı (429) — o çağrı gerçek yolu ısıtmadı."; hata=1 ;;
    000) echo "ISITMA UYARI: arka uca ulaşılamadı (zaman aşımı/bağlantı)."; hata=1 ;;
    *)   echo "ISITMA UYARI: beklenmeyen kod ${kod}."; hata=1 ;;
  esac
done

# ── Ön yüz: giriş sayfası ve panel ───────────────────────────────────────
ilk_on=""
for yol in /login /dashboard /login; do
  okuma="$(sure GET "${ON_YUZ}${yol}")"
  kod="${okuma%% *}"; sn="${okuma##* }"
  [ -z "$ilk_on" ] && ilk_on="$sn"
  echo "ISITMA on-yuz yol=${yol} kod=${kod} sure=${sn}s"
  case "$kod" in
    200|307|302) ;;
    000) echo "ISITMA UYARI: ön yüze ulaşılamadı."; hata=1 ;;
    *)   echo "ISITMA UYARI: beklenmeyen kod ${kod} (${yol})."; hata=1 ;;
  esac
done

# ── Isıtmadan SONRA gelen ilk gerçek isteğin süresi ──────────────────────
# Ölçüm, ısıtma çağrılarından AYRI ve temiz bir sentetik IP'den yapılır;
# rapor edilen sayı "ilk kullanıcının göreceği" süredir.
son_arka="$(sure POST "${ARKA_UC}/api/auth/login" "$(sentetik_ip)")"
son_on="$(sure GET "${ON_YUZ}/login")"
echo "ISITMA SONUÇ arka-uc ilk=${ilk_arka}s sonra=${son_arka##* }s | on-yuz ilk=${ilk_on}s sonra=${son_on##* }s"

if [ "$hata" -ne 0 ]; then
  echo "ISITMA TAMAMLANDI (uyarılı) — yayın bu yüzden başarısız SAYILMAZ,"
  echo "ISITMA çünkü sağlık kontrolü zaten geçti; ısıtma bir hız tedbiridir."
  exit 3
fi
echo "ISITMA TAMAM"
exit 0
