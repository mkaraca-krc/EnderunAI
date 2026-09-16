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
bugunku_isitma() {  # KOL D: bugün ısıtmanın yaptığı çağrı — olmayan kullanıcı
  curl -s -o /dev/null -m 60 -w '%{http_code} %{time_total}' \
    -X POST "${URL}/api/auth/login" -H 'Content-Type: application/json' \
    -H "X-Forwarded-For: 192.0.2.$(( (RANDOM % 250) + 2 ))" \
    -d "{\"username\":\"isitma-yok-$RANDOM\",\"password\":\"IsitmaGecersiz!$RANDOM\"}"
}
var_olan_yanlis_parola() {  # KOL E: kullanıcı VAR -> passwordService.Verify ÇALIŞIR
  # Parola BİLİNMİYOR ve bilinmesi gerekmiyor: yanlış parola da Verify
  # adımını koşturur. Canlı parola kullanılmıyor, test veritabanındaki
  # mevcut bir kullanıcı kullanılıyor.
  curl -s -o /dev/null -m 60 -w '%{http_code} %{time_total}' \
    -X POST "${URL}/api/auth/login" -H 'Content-Type: application/json' \
    -H "X-Forwarded-For: 192.0.2.$(( (RANDOM % 250) + 2 ))" \
    -d "{\"username\":\"${RIG_KULLANICI:-test.admin}\",\"password\":\"kesinlikle-yanlis-$RANDOM\"}"
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

TEKRAR="${ISINMA_TEKRAR:-3}"

# ÜÇER KEZ, EN İYİ VE ORTANCA (2026-09-16).
#
# Tek atış %70 sapma verdi (A: 0,2718 ve 0,4575). Tek koşuyla hüküm
# kurulamaz. Her kol TEKRAR kez soğuk başlatılıyor; hem EN İYİ hem
# ORTANCA yazılıyor — en iyi "en uygun koşulda ne oluyor", ortanca
# "tipik olarak ne oluyor" sorusunu yanıtlar. İkisi birden yazılmazsa
# okuyan hangisini gördüğünü bilemez.
kol_kos() {  # $1 ad, $2 ön-çağrı fonksiyonu ("-" ise yok)
    local ad="$1" on="$2" i sure liste=""
    echo "════ KOL ${ad} ════"
    for i in $(seq 1 "$TEKRAR"); do
        ac
        if [ "$on" != "-" ]; then
            local o; o="$($on)"
            log "  ${ad}.${i} ön-çağrı   -> ${o}"
        fi
        sure="$(gercek_giris)"
        log "  ${ad}.${i} ilk giriş  -> ${sure}"
        liste="${liste} ${sure##* }"
        kapat
    done
    printf '%s|%s\n' "$ad" "$(echo $liste)" >> "$SONUC"
}

SONUC="$(mktemp)"; trap 'rm -f "$SONUC"' EXIT

kol_kos "A (ön çağrı YOK)"              -
kol_kos "B (geçersiz gövde 400)"        gecersiz_govde
kol_kos "D (bugünkü ısıtma çağrısı)"    bugunku_isitma
kol_kos "E (var olan kullanıcı, yanlış parola)" var_olan_yanlis_parola

echo "════ SONUÇ — ${TEKRAR} koşu/kol ════"
awk -F'|' '{
  n=split($2, v, " ");
  for (i=1;i<=n;i++) for (j=i+1;j<=n;j++) if (v[j]+0 < v[i]+0) { t=v[i]; v[i]=v[j]; v[j]=t }
  eniyi=v[1]+0; ortanca=v[int((n+1)/2)]+0;
  printf "[isinma-adayi] %-46s en iyi %.4fs  ortanca %.4fs   (%s)\n", $1, eniyi, ortanca, $2;
}' "$SONUC"

a_sn="$(awk -F'|' '/^A /{n=split($2,v," "); for(i=1;i<=n;i++) for(j=i+1;j<=n;j++) if(v[j]+0<v[i]+0){t=v[i];v[i]=v[j];v[j]=t} print v[int((n+1)/2)]+0}' "$SONUC")"
b_sn="$(awk -F'|' '/^B /{n=split($2,v," "); for(i=1;i<=n;i++) for(j=i+1;j<=n;j++) if(v[j]+0<v[i]+0){t=v[i];v[i]=v[j];v[j]=t} print v[int((n+1)/2)]+0}' "$SONUC")"
d_sn="$(awk -F'|' '/^D /{n=split($2,v," "); for(i=1;i<=n;i++) for(j=i+1;j<=n;j++) if(v[j]+0<v[i]+0){t=v[i];v[i]=v[j];v[j]=t} print v[int((n+1)/2)]+0}' "$SONUC")"
e_sn="$(awk -F'|' '/^E /{n=split($2,v," "); for(i=1;i<=n;i++) for(j=i+1;j<=n;j++) if(v[j]+0<v[i]+0){t=v[i];v[i]=v[j];v[j]=t} print v[int((n+1)/2)]+0}' "$SONUC")"
echo
log "ortancalar:  A=${a_sn}  B=${b_sn}  D=${d_sn}  E=${e_sn}"

awk -v a="$a_sn" -v b="$b_sn" -v d="$d_sn" -v e="$e_sn" 'BEGIN{
  #
  # HUKUM DAR TUTULUYOR — OLCUM GURULTULU (2026-09-16, olculdu).
  # Tek atis %70 sapma verdi; bu yuzden her kol 3 kez kosuluyor ve
  # ORTANCA karsilastiriliyor. "KISMEN dustu" diyen dal gurultu
  # okuyordu, kaldirildi.
  #
  print "";
  if (d+0 > 0 && d+0 < a+0 * 0.5)
    print "[isinma-adayi] D << A: BUGUNKU ISITMA CAGRISI ISE YARIYOR.";
  else
    print "[isinma-adayi] D ~ A: BUGUNKU ISITMA CAGRISI DA ISITMIYOR -> secenek 1 olcumle hakli.";
  if (e+0 > 0 && e+0 < a+0 * 0.5)
    print "[isinma-adayi] E << A: MALIYETIN YERI PAROLA OZET DOGRULAMASI (hipotez DOGRULANDI).";
  else
    print "[isinma-adayi] E ~ A: parola ozeti degil (hipotez CURUDU).";
  printf "[isinma-adayi] hedef 0,05 -> A=%.4f B=%.4f D=%.4f E=%.4f\n", a, b, d, e;
}'
