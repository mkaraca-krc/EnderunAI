#!/usr/bin/env bash
#
# ISINMA/1 — YAYINDAN SONRA İLK KULLANICININ ÖDEDİĞİ BEDELİ YAYIN BETİĞİ ÖDER.
#
# ═══ NEDEN VAR ═══
#
# Derleme ve canlı ERP aynı makinede. Bir temiz derleme ~6,3 GB yerleşik
# istiyor ve makine marjı dar; çekirdek farkı canlı servislerin sayfalarını
# TAKASA yazarak kapatıyor. Derleme canlıyı DÜŞÜRMÜYOR — ama soğutuyor.
# Bedeli yayından sonra giren ilk kullanıcı öder; bu betik o bedeli yayın
# betiğine ödetir.
#
# ═══ ESKİ SAYI GEÇERSİZ — YENİDEN ÖLÇÜLDÜ (2026-09-15) ═══
#
# Bu başlıkta uzun süre "ilk istek 6,94 sn" yazdı. O ölçüm 2026-09-13'te,
# YAYIN SIRASI KURULMADAN ÖNCE, elle alınmıştı ve o günün koşullarında
# doğruydu. BUGÜN GEÇERLİ DEĞİL.
#
# Bugünkü sıra: restart → sağlık yoklaması (12-14 sn, döngüyle) → vekil
# duman testi → websocket duman testi → giriş döngüsü kapısı (10 sn) →
# ISITMA. Yani ısıtma başladığında restart'ın üzerinden ~23 saniye geçmiş
# ve uygulama çoktan istek görmüş oluyor. SOĞUK BAŞLANGICIN BEDELİNİ
# SAĞLIK YOKLAMASI EMİYOR, bu betik değil.
#
# ÖLÇÜLEN (son iki yayının günlüğünden, arka uç ilk çağrısı):
#
#   | yayın             | ilk    | 2.      | 3.      |
#   |-------------------|--------|---------|---------|
#   | 2026-09-15 12:34  | 0,2277 | 0,0290  | 0,0110  |
#   | 2026-09-15 18:28  | 0,2267 | 0,0156  | 0,0104  |
#
# Yani bu betiğin ilk kullanıcıya kazandırdığı süre ~0,22 SANİYEDİR,
# yedi saniye değil.
#
# ═══ O 0,22 SANİYE NEYİN BEDELİ (bileşenine ayrıldı) ═══
#
#   SELECT users+roles ...........   2 ms   (%1)
#   INSERT security_audit_events ..  10 ms  (%4)
#   geri kalan ................... ~215 ms  (%95)
#
# Geri kalan, İLK DENETLEYİCİ İSTEĞİNİN JIT BEDELİDİR: MVC boru hattı,
# model bağlama, JSON, yetki ara katmanı. VERİTABANI DEĞİL.
#
# EF ZATEN SICAK: başlangıç tohumlayıcısı, ısıtma koşmadan 14 saniye önce
# giriş sorgusunun aynı şeklini koşuyor (`users WHERE "Username" = @p` +
# `user_roles` join). Kanıtı ısıtmanın ilk çağrısında SELECT'in 2 ms
# sürmesidir.
#
# PAROLA ÖZETİ HİÇ ISINMIYOR: `user is null || !user.IsActive ||
# !passwordService.Verify(...)` kısa devre yapar ve kullanıcı var
# olmadığı için `Verify` ÇAĞRILMAZ.
#
# ═══ NEDEN `memory.swap.max` DEĞİL ═══
#
# Servislerin takasa yazılmasını yasaklamak semptomu kökten keserdi, ama
# yanlış kurulursa 7 saniyelik gecikmeyi SERVİS ÖLÜMÜNE çevirir: sıkışma
# anında çekirdeğin geri kazanacak anon sayfası kalmaz ve OOM'a gider.
# Semptom hafif, yeni risk ağır. Mehmet Bey'in kararı: canlıya geçmeden o
# takas yapılmaz (EKSİK/1'de duruyor).
#
# ═══ GİRİŞ ÇAĞRISI ÇIKIYOR — İKİ YAYINA YAYILMIŞ GEÇİŞ ═══
#
# `/api/health` ısıtma için işe yaramaz, ama sebebi "ucuz olması" DEĞİL:
# o uç bir `MapGet`tir, MVC DENETLEYİCİ BORU HATTINA HİÇ GİRMEZ. Isınan
# şey tam olarak o boru hattı olduğuna göre, YAZMA YAPMAYAN HERHANGİ BİR
# DENETLEYİCİ ÇAĞRISI aynı işi görür.
#
# Giriş çağrısının bedeli ölçüldü: her yayında 4 SAHTE BAŞARISIZ GİRİŞ
# denetim kaydına yazılıyor (GÜNLÜK/1, 2026-09-15'ten beri). Yılda
# yüzlerce sahte güvenlik olayı demek; gerçek olaylar aralarında
# kaybolur. Mehmet Bey'in kararı (2026-09-15): giriş çağrısı yazma
# yapmayan bir denetleyici çağrısıyla DEĞİŞTİRİLECEK.
#
#   YAYIN N (bu):  önce `GET /api/company-settings/logo` (anonim, MVC
#                  denetleyici eylemi, YALNIZ OKUR), sonra giriş. Giriş
#                  çağrısının İLK süresi ölçülür.
#                  · ~0,03 sn'ye düşerse -> logo JIT'i emmiş, kanıtlandı
#                  · düşmezse            -> logo yetmiyor, ölçüm raporlanır
#   YAYIN N+1:     kanıtlandıysa giriş çağrısı BURADAN ÇIKAR. O yayında
#                  `isitma-` önekli denetim satırı SIFIR olmalı ve ilk
#                  gerçek giriş ~0,22 sn'de kalmalı.
#
# ═══ GİRİŞ ÇAĞRISI DURDUĞU SÜRECE: NEDEN TEHLİKELİ DEĞİL ═══
#
# ESKİ İKİ CÜMLE ARTIK YANLIŞTI, DÜZELTİLDİ (Kural 94, 2026-09-15):
#
#   (yanlış) "`LoginAttemptService` yalnız bellekte tutuyor —
#            veritabanına yazmaz."
#   (doğru)  Sayaç hâlâ yalnız bellekte, AMA GÜNLÜK/1'den beri her
#            başarısız giriş `security_audit_events` tablosuna bir
#            `LoginFailed` SATIRI YAZIYOR. Bu betiğin çıkardığı
#            gürültünün kaynağı da budur.
#
#   (yanlış) "IP `X-Forwarded-For`ın ilk değerinden okunuyor."
#   (doğru)  SON elemandan okunuyor (2026-09-15 akşam yayını). İlk
#            eleman istemcinin uydurabildiği değerdi; atlatma canlıda
#            kanıtlanıp kapatıldı.
#
# Bu betik arka uca DOĞRUDAN (127.0.0.1:5155) vurduğu için gönderdiği
# zincir tek elemanlıdır; son eleman = gönderdiği sentetik adres. Adres
# HER KOŞUDA YENİ ve asla yönlendirilemeyen bir RFC 5737 TEST-NET-1
# adresidir (192.0.2.x) ve 5 hatalık kilit eşiğinin ALTINDA kalınır.
# Gerçek kullanıcıların adres sayacına dokunulmaz — dokunulsaydı bir
# yayın o kullanıcıları 15 dakika kilitleyebilirdi.
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

# ── ÖNCE: YAZMA YAPMAYAN DENETLEYİCİ ÇAĞRISI ────────────────────────────
#
# Isınan şey MVC denetleyici boru hattının JIT bedeli (ölçüldü: ilk
# çağrının 227 ms'inin ~215'i). `GET /api/company-settings/logo` anonim
# bir denetleyici eylemidir ve YALNIZ OKUR — tek satır yazmaz.
#
# 200 de 404 de GEÇERLİ: şirketin logosu tanımlı değilse uç 404 döner,
# ama istek MVC boru hattından TAM OLARAK aynı şekilde geçmiştir ve
# ısınma gerçekleşmiştir. 404'ü hata saymak, ısınmayı ölçmeyip
# yapılandırmayı ölçmek olurdu.
okuma="$(sure GET "${ARKA_UC}/api/company-settings/logo")"
kod_logo="${okuma%% *}"; sn_logo="${okuma##* }"
echo "ISITMA yazmasiz-okuma uc=/api/company-settings/logo kod=${kod_logo} sure=${sn_logo}s"
case "$kod_logo" in
  200|404) ;;
  000) echo "ISITMA UYARI: yazmasız okuma ucuna ulaşılamadı."; hata=1 ;;
  *)   echo "ISITMA UYARI: yazmasız okumada beklenmeyen kod ${kod_logo}."; hata=1 ;;
esac

# ── Arka uç: gerçek kimlik doğrulama yolu ────────────────────────────────
#
# ÖLÇÜM (YAYIN N, 2026-09-15 kararı): aşağıdaki İLK giriş süresi artık
# bir ısıtma değil, bir SINAMADIR. Yukarıdaki yazmasız okuma boru
# hattını ısıttıysa bu sayı ~0,03 sn olmalı (ısıtmasız ölçülen değer
# 0,227 sn idi). Düşerse giriş çağrısı BİR SONRAKİ YAYINDA çıkar.
# Düşmezse logo yetmiyor demektir ve ölçüm raporlanır.
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

# ── YAYIN N SINAMASI: yazmasız okuma boru hattını ısıttı mı? ────────────
# Karar eşiği 0,05 sn: ısıtmasız ölçüm 0,227 sn, ısınmış ölçüm 0,008-0,029
# sn. İkisinin arası geniş; 0,05 hangi tarafta olduğumuzu ayırmaya yeter.
# BU BİR KAPI DEĞİL: hüküm vermez, yayını durdurmaz, yalnız ölçümü
# okunabilir yazar. Karar insanda.
#
# ⚠ SINAMA YALNIZ YAYIN SIRASINDA ANLAMLIDIR — YENİDEN BAŞLATILMIŞ,
# SOĞUK BİR UYGULAMADA. Betiği elle, saatlerdir koşan bir uygulamaya
# karşı çalıştırırsanız HER ZAMAN "ISITTI" yazar: boru hattı zaten
# sıcaktır, yazmasız okumanın bir katkısı ölçülmez. O yeşil, ısınmanın
# değil sondanın ısırabileceği koşulda olmadığının kanıtıdır (Kural 93).
# Elle koşumda bu satır GÖRMEZDEN GELİNİR; hüküm, yayın günlüğündeki
# koşumdan okunur.
#
# TEK CÜMLEYLE: BU ALETİN TEK GEÇERLİ KOŞUM ANI, YENİDEN BAŞLATMADAN
# HEMEN SONRASIDIR. Başka her an "ISITTI" der ve hiçbir şey ölçmez.
esik_ms=50
ilk_ms="$(awk -v v="$ilk_arka" 'BEGIN{printf "%d", v*1000}' 2>/dev/null || echo 9999)"
if [ "$ilk_ms" -le "$esik_ms" ]; then
  echo "ISITMA SINAMA: yazmasız okuma boru hattını ISITTI (giriş ilk=${ilk_arka}s <= 0,05s)."
  echo "ISITMA SINAMA: giriş çağrısı bir sonraki yayında ısıtmadan çıkarılabilir."
else
  echo "ISITMA SINAMA: yazmasız okuma YETMEDİ (giriş ilk=${ilk_arka}s > 0,05s)."
  echo "ISITMA SINAMA: giriş çağrısı ÇIKARILMASIN; ölçüm raporlanmalı."
fi

if [ "$hata" -ne 0 ]; then
  echo "ISITMA TAMAMLANDI (uyarılı) — yayın bu yüzden başarısız SAYILMAZ,"
  echo "ISITMA çünkü sağlık kontrolü zaten geçti; ısıtma bir hız tedbiridir."
  exit 3
fi
echo "ISITMA TAMAM"
exit 0
