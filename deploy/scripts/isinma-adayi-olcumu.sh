#!/usr/bin/env bash
#
# ISINMA ADAYI ÖLÇÜMÜ — GEÇERSİZ GÖVDE, DENETİM SATIRI YAZMADAN ISITIR MI?
#
# ═══ SORU ═══
#
# Isıtma adımı her yayında 4 SAHTE BAŞARISIZ GİRİŞ yazıyor. Yazma
# yapmayan bir denetleyici çağrısı (logo) denendi ve YETMEDİ: giriş ilk
# süresi 0,2376 sn kaldı (beklenen ≤0,05). Teşhis: ısınan şey genel MVC
# boru hattı değil, GİRİŞ YOLUNA ÖZGÜ bir şey.
#
# Mehmet Bey'in ucuz adayı: giriş ucuna GEÇERSİZ GÖVDE gönder. Model
# doğrulaması 400 döndürür, işleyiciye HİÇ GİRMEZ, denetim satırı
# YAZILMAZ — ama AuthController etkinleştirilir ve bağımlılık grafiği
# kurulur. Teşhis doğruysa asıl pahalı kısım orasıdır.
#
# ═══ CANLIYA DOKUNULMUYOR (Kural 81/85) ═══
#
# Ölçüm canlı gerektirmiyor: rig AYNI YAYIM ÇIKTISINI (`publish/`)
# `enderun_ai_test` üzerinde, ayrı portta, kendi disk köküyle koşuyor.
# Soğuk restart canlıda 9 sn kesinti demekti; burada sıfır.
#
# Hazırlık yoklaması `/api/health` ile yapılıyor — o bir `MapGet`tir ve
# MVC DENETLEYİCİ boru hattına girmez, yani ölçümü ısıtmaz. (Bu, aynı
# ölçümün daha önce kanıtladığı şeydir.)
#
# ═══ POZİTİF KONTROL ZORUNLU (Kural 48) ═══
#
# İki kol, ikisi de SOĞUK başlangıçtan:
#   KOL A (kontrol) : hiçbir ön çağrı yok      -> ilk giriş süresi
#   KOL B (aday)    : önce geçersiz gövde(400) -> ilk giriş süresi
# İki sayı yan yana olmadan hüküm yok. A ile B arasında fark yoksa aday
# hiçbir şey ısıtmıyordur.
#
set -uo pipefail
KOK="/var/www/enderun-ai"
PORT="${ISINMA_PORT:-5158}"
URL="http://127.0.0.1:${PORT}"

log() { echo "[isinma-adayi] $*"; }
oldu() { echo "[isinma-adayi] HATA: $*" >&2; exit 1; }

CANLI="$(grep -E '^DB_CONNECTION=' /etc/enderunai/backend.env | sed -E "s/^DB_CONNECTION=//" | tr -d "'\"")"
TEST="${CANLI//Database=enderun_ai;/Database=enderun_ai_test;}"
case "$TEST" in
  *"Database=enderun_ai_test;"*) : ;;
  *) oldu "Hedef enderun_ai_test DEĞİL. Durduruldu." ;;
esac

PID=""; DISK=""
temizle() {
  [ -n "$PID" ] && kill -TERM -- "-${PID}" 2>/dev/null; sleep 1
  [ -n "$PID" ] && kill -KILL -- "-${PID}" 2>/dev/null
  [ -n "$DISK" ] && rm -rf "$DISK"
  true
}
trap temizle EXIT

ac() {
  DISK="$(mktemp -d /tmp/isinma-disk-XXXXXX)"
  DB_CONNECTION="$TEST" \
  JWT_SECRET="isinma-olcum-$(head -c 9 /dev/urandom | base64 | tr -d '/+=')" \
  Uploads__Root="${DISK}/uploads" \
  EInvoice__ArchivePath="${DISK}/e-fatura" \
  Storage__ProjectFilesRoot="${DISK}/project-files" \
  ASPNETCORE_URLS="$URL" \
    setsid dotnet "${KOK}/publish/EnderunAI.Api.dll" > /tmp/isinma-arka.log 2>&1 &
  PID=$!
  for _ in $(seq 1 180); do
    curl -sf -m 2 "${URL}/api/health" >/dev/null 2>&1 && return 0
    sleep 2
  done
  tail -15 /tmp/isinma-arka.log >&2
  oldu "Arka uç ${PORT} açılmadı."
}
kapat() { temizle; PID=""; DISK=""; }

gecersiz_govde() {  # zorunlu alanlar eksik -> 400 beklenir
  curl -s -o /dev/null -m 30 -w '%{http_code} %{time_total}' \
    -X POST "${URL}/api/auth/login" -H 'Content-Type: application/json' -d '{}'
}
gercek_giris() {    # biçimi doğru, kullanıcı yok -> 401 beklenir
  curl -s -o /dev/null -m 60 -w '%{http_code} %{time_total}' \
    -X POST "${URL}/api/auth/login" -H 'Content-Type: application/json' \
    -H "X-Forwarded-For: 192.0.2.$(( (RANDOM % 250) + 2 ))" \
    -d "{\"username\":\"isinma-olcum-yok-$RANDOM\",\"password\":\"Gecersiz!$RANDOM\"}"
}

denetim_say() {
  "${KOK}/deploy/scripts/vt-sorgu.sh" --vt enderun_ai_test --sql \
    "select count(*) from security_audit_events where \"Action\"='LoginFailed'" 2>/dev/null \
    | grep -E '^[0-9]+$' | head -1
}

echo "════ KOL A — KONTROL: ön çağrı YOK ════"
ac
a_giris="$(gercek_giris)"
log "A ilk giriş      -> ${a_giris}"
kapat

echo "════ KOL B — ADAY: önce geçersiz gövde ════"
ac
once="$(denetim_say)"
b_gecersiz="$(gecersiz_govde)"
log "B geçersiz gövde -> ${b_gecersiz}   (400 beklenir)"
sonra="$(denetim_say)"
log "B denetim satırı : önce=${once:-?} sonra=${sonra:-?}   (DEĞİŞMEMELİ)"
b_giris="$(gercek_giris)"
log "B ilk giriş      -> ${b_giris}"
kapat

a_sn="${a_giris##* }"; b_sn="${b_giris##* }"
echo "════ SONUÇ ════"
log "A (ön çağrı yok)      ilk giriş: ${a_sn}s"
log "B (geçersiz gövde ön) ilk giriş: ${b_sn}s"
awk -v a="$a_sn" -v b="$b_sn" 'BEGIN{
  printf "[isinma-adayi] fark: %.4fs  (A-B)\n", a-b;
  #
  # HÜKÜM DAR TUTULUYOR — ÖLÇÜM GÜRÜLTÜLÜ (2026-09-16, ölçüldü).
  #
  # Aynı A kolu iki koşuda 0,2718 ve 0,4575 sn verdi: %70 sapma. Makine
  # paylaşımlı ve yüklü; tek atışlık süreler 0,27 ile 0,33 arasını
  # AYIRT EDEMEZ. Ilk yazimda buraya "KISMEN düştü" diyen bir dal
  # koymuştum — o dal GÜRÜLTÜ OKUYORDU ve kaldırıldı.
  #
  # Geriye yalnız gürültüden ETKİLENMEYEN soru kaldı: hedef 0,05 hedefine
  # ulaşıldı mı? B en iyi hâlinde 0,29 çıktı — on kat uzak. Bu hüküm
  # sapmaya rağmen ayakta.
  #
  # "Ne kadar yardım etti" sorusunu cevaplamak için TEKRAR gerekir:
  # her kolu en az 3 kez soğuk başlatıp ortancayı almadan o cümle
  # kurulmaz.
  if (b+0 <= 0.05) {
    print "[isinma-adayi] ADAY İŞE YARIYOR: B <= 0,05s.";
  } else if (b+0 >= 0.20) {
    print "[isinma-adayi] ADAY YETMİYOR: B >= 0,20s — hedefin (0,05) kat kat üstünde.";
    print "[isinma-adayi] Bu hüküm ölçüm sapmasından ETKİLENMEZ.";
  } else {
    print "[isinma-adayi] ÖLÇEMEDİ: B ara bölgede (0,05-0,20). Tek atış bunu ayırt edemez;";
    print "[isinma-adayi] her kolu en az 3 kez soğuk başlatıp ortancayı alın.";
  }
}'
