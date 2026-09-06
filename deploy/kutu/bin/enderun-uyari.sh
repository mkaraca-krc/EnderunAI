#!/usr/bin/env bash
#
# NÖBET UYARISI — DÜŞEN BİR BİRİM HABER VERİR.
#
# NEDEN AYRI BİR İŞ (Mehmet, 2026-09-06): *"Zaman aşımı koşuyu
# düşürüyor; düşen koşunun haber vermesi ayrı iştir."* Zaman aşımı
# takılmayı önler ama sessizce önler — kimse haberdar olmaz.
#
# İKİ KANAL, BİRİ HER ZAMAN (AK-2 kararı):
#   · DOSYA  — koşulsuz. E-posta çalışsa bile yazılır. Tek kanala
#              bağlanmak, kanal ölünce sessizliği "yolunda" gösterir.
#   · E-POSTA— birincil. Mevcut SMTP sırrıyla, YENİ SIR EKLENMEDEN.
#
# SIR DİSİPLİNİ: bu betik hiçbir sır DEĞERİNİ basmaz. Eksik yapılandırma
# yalnız ANAHTAR ADIYLA raporlanır. Parola `curl -K -` ile STANDART
# GİRİŞTEN geçirilir; komut satırına yazılmaz (`ps` ile okunurdu).
#
# KURU KOŞU: UYARI_KURU=1 ile e-posta OLUŞTURULUR ama GÖNDERİLMEZ.
# İlk gerçek gönderimi Mehmet yapacak — kendi başıma test e-postası
# göndermiyorum.

set -uo pipefail

BIRIM="${1:-bilinmeyen-birim}"
GUNLUK="/var/log/enderun-uyari.log"
SON="/var/lib/enderun-ai/uyari-son.txt"
ALICI_DOSYASI="/etc/enderunai/uyari-alicilar.txt"
ORTAM="/etc/enderunai/backend.env"

ZAMAN="$(TZ=Europe/Istanbul date '+%Y-%m-%d %H:%M:%S') TR"

# systemd'den durum özeti — sır içermez, birim adı ve sonuç kodu.
DURUM="$(systemctl show "$BIRIM" -p Result -p ExecMainStatus -p ActiveState --value 2>/dev/null | tr '\n' ' ')"
SON_SATIRLAR="$(journalctl -u "$BIRIM" -n 8 --no-pager -o cat 2>/dev/null)"

GOVDE="ENDERUN AI — BIRIM DUSTU

birim  : ${BIRIM}
zaman  : ${ZAMAN}
durum  : ${DURUM}

son gunluk satirlari:
${SON_SATIRLAR}
"

# ── KANAL 1: DOSYA — KOŞULSUZ ───────────────────────────────────
mkdir -p "$(dirname "$SON")"
printf '%s\n' "$GOVDE" > "$SON"
printf '%s [UYARI] birim=%s durum=%s\n' "$ZAMAN" "$BIRIM" "$DURUM" >> "$GUNLUK"

# ── KANAL 2: E-POSTA — EKSİK OLAN NE İSE ADIYLA SÖYLENİR ────────
eksik() { printf '%s [POSTA-YOK] %s\n' "$ZAMAN" "$1" >> "$GUNLUK"; exit 0; }

[ -r "$ORTAM" ]         || eksik "ortam dosyasi okunamadi: ${ORTAM}"
[ -r "$ALICI_DOSYASI" ] || eksik "alici listesi yok: ${ALICI_DOSYASI}"

# YALNIZ SMTP anahtarları alınıyor — bu dosyada veritabanı bağlantısı
# ve JWT sırrı da var, onlar bu sürecin ortamına GİRMEMELİ.
while IFS='=' read -r anahtar deger; do
    case "$anahtar" in
        SMTP_HOST|SMTP_PORT|SMTP_USER|SMTP_PASS|SMTP_FROM|SMTP_FROM_NAME)
            # TEK VE ÇİFT TIRNAK — ÖLÇÜLDÜ: backend.env TEK tırnak
            # kullanıyor. İlk sürüm yalnız çift tırnak soyuyordu ve
            # curl'e tırnaklı bir konak adı gidiyordu: "curl: (3) URL
            # using bad/illegal format". Sonda bunu ilk koşuda buldu.
            deger="${deger%\'}"; deger="${deger#\'}"
            deger="${deger%\"}"; deger="${deger#\"}"
            printf -v "$anahtar" '%s' "$deger"
            ;;
    esac
done < "$ORTAM"

: "${SMTP_HOST:=}" "${SMTP_PORT:=465}" "${SMTP_USER:=}" "${SMTP_PASS:=}" "${SMTP_FROM:=}"

for ad in SMTP_HOST SMTP_USER SMTP_PASS SMTP_FROM; do
    [ -n "${!ad}" ] || eksik "ayar bos: ${ad}"
done

ALICI="$(grep -vE '^\s*(#|$)' "$ALICI_DOSYASI" | head -1)"
[ -n "$ALICI" ] || eksik "alici listesi bos: ${ALICI_DOSYASI}"

MESAJ="$(mktemp)"
trap 'rm -f "$MESAJ"' EXIT
{
    printf 'From: %s\n' "$SMTP_FROM"
    printf 'To: %s\n' "$ALICI"
    printf 'Subject: [ENDERUN] birim dustu: %s\n' "$BIRIM"
    printf 'Content-Type: text/plain; charset=UTF-8\n\n'
    printf '%s\n' "$GOVDE"
} > "$MESAJ"

# ── GÖNDERİM KAPISI — FAIL-CLOSED ───────────────────────────────
#
# İLK GERÇEK GÖNDERİM MEHMET'TE (duran kural). Kendi başıma test
# e-postası göndermiyorum ve bir sondanın yanlışlıkla posta atmasını
# ŞANSA BIRAKMIYORUM.
#
# NEDEN BAYRAK DEĞİL DOSYA: sonda birimine `Environment=UYARI_KURU=1`
# yazmıştım; systemd bunu OnFailure ile tetiklenen AYRI birime
# GEÇİRMEDİ ve betik gerçek gönderim denedi. Gönderim yalnız curl
# tırnak hatasına takıldığı için çıkmadı — yani beni kural ihlalinden
# tasarım değil, ŞANS kurtardı. Kapı artık sürecin ortamında değil,
# DİSKTE duruyor; tetikleyen kim olursa olsun aynı kapı geçerli.
#
# Mehmet açtığında: touch /etc/enderunai/uyari-posta-acik
POSTA_KAPISI="/etc/enderunai/uyari-posta-acik"
if [ ! -f "$POSTA_KAPISI" ]; then
    printf '%s [POSTA-KAPALI] e-posta olusturuldu, GONDERILMEDI (%s bayt). Acmak icin: touch %s\n' \
        "$ZAMAN" "$(wc -c < "$MESAJ")" "$POSTA_KAPISI" >> "$GUNLUK"
    exit 0
fi

if [ "${UYARI_KURU:-0}" = "1" ]; then
    printf '%s [KURU-KOSU] e-posta olusturuldu, GONDERILMEDI (%s bayt, alici tanimli)\n' \
        "$ZAMAN" "$(wc -c < "$MESAJ")" >> "$GUNLUK"
    exit 0
fi

if printf 'user = "%s:%s"\n' "$SMTP_USER" "$SMTP_PASS" \
   | curl -K - --silent --show-error --ssl-reqd --max-time 30 \
          --url "smtps://${SMTP_HOST}:${SMTP_PORT}" \
          --mail-from "$SMTP_FROM" --mail-rcpt "$ALICI" \
          --upload-file "$MESAJ" 2>>"$GUNLUK"; then
    printf '%s [POSTA-GITTI] birim=%s\n' "$ZAMAN" "$BIRIM" >> "$GUNLUK"
else
    printf '%s [POSTA-DUSTU] birim=%s — dosya kanali yazildi, e-posta gitmedi\n' \
        "$ZAMAN" "$BIRIM" >> "$GUNLUK"
fi
