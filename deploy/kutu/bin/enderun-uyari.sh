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

# İKİNCİ ARGÜMAN: hangi olay (NÖBET/1 · K8).
#   dustu   — varsayılan, OnFailure'dan gelir.
#   duzeldi — nobet.sh'den gelir; düşmüş bir birim toparlanınca.
# AYRI BİR BETİK YAZILMADI: teslim yolu (SMTP, alıcı listesi, posta
# kapısı, kuru koşu) TEK yerde kalmalı. İkinci bir yol açsaydım biri
# düzeltilip öteki unutulurdu (Kural 79).
OLAY="${2:-dustu}"

GUNLUK="/var/log/enderun-uyari.log"
SON="/var/lib/enderun-ai/uyari-son.txt"

# GÜRÜLTÜ SINIRI DURUMU — NÖBET/1 · K8.
DURUM_DIZINI="/var/lib/enderun-ai/nobet"
SUSTURMA_SANIYE="${UYARI_SUSTURMA_SANIYE:-1800}"
ALICI_DOSYASI="/etc/enderunai/uyari-alicilar.txt"
ORTAM="/etc/enderunai/backend.env"

ZAMAN="$(TZ=Europe/Istanbul date '+%Y-%m-%d %H:%M:%S') TR"

# systemd'den durum özeti — sır içermez, birim adı ve sonuç kodu.
DURUM="$(systemctl show "$BIRIM" -p Result -p ExecMainStatus -p ActiveState --value 2>/dev/null | tr '\n' ' ')"
SON_SATIRLAR="$(journalctl -u "$BIRIM" -n 8 --no-pager -o cat 2>/dev/null)"

if [ "$OLAY" = "duzeldi" ]; then
    GOVDE="ENDERUN AI — BIRIM DUZELDI

birim  : ${BIRIM}
zaman  : ${ZAMAN}
durum  : ${DURUM}

Onceki ariza kapandi. Bu posta, arizanin bittigini bildirir.
"
else
    GOVDE="ENDERUN AI — BIRIM DUSTU

birim  : ${BIRIM}
zaman  : ${ZAMAN}
durum  : ${DURUM}

son gunluk satirlari:
${SON_SATIRLAR}
"
fi

# ── KANAL 1: DOSYA — KOŞULSUZ ───────────────────────────────────
#
# SUSTURMA BU KANALI ETKİLEMEZ. Gürültü sınırı POSTAYA konuyor;
# dosya ve günlük her olayda yazılıyor. Susturulan bir arızanın
# hiçbir izi kalmasaydı, "30 dakikadır sessiz" ile "hiç olmadı"
# birbirinden ayrılamazdı.
mkdir -p "$(dirname "$SON")"
printf '%s\n' "$GOVDE" > "$SON"

# ── GÜRÜLTÜ SINIRI — ARIZA KİMLİĞİNE GÖRE (NÖBET/1 · K8) ────────
#
# ÖNCEKİ DAVRANIŞ: her düşüşte bir posta, aynı arıza tekrarlarsa
# tekrar posta, düzelince hiçbir şey. Beş dakikada bir yeniden
# başlayan bir birim, posta kutusunu doldurup asıl uyarıyı boğardı.
#
# KİMLİK NEDEN DURUMU DA İÇERİYOR: aynı birimin AYNI şekilde
# düşmesi tekrar, FARKLI şekilde düşmesi YENİ BİLGİDİR. İkincisi
# susturulmaz.
KIMLIK="$(printf '%s|%s' "$BIRIM" "$DURUM" | md5sum | cut -c1-12)"
DURUM_DOSYASI="${DURUM_DIZINI}/$(printf '%s' "$BIRIM" | tr '/@' '__').durum"
SIMDI="$(date +%s)"
mkdir -p "$DURUM_DIZINI"

ONCEKI_KIMLIK=""
ONCEKI_SON=0
if [ -r "$DURUM_DOSYASI" ]; then
    # SOURCE EDİLMİYOR: kendi yazdığımız dosya bile olsa, `.` ile
    # okumak bir kod çalıştırma yüzeyidir. Alanlar tek tek ayrıştırılıyor.
    ONCEKI_KIMLIK="$(sed -n 's/^kimlik=//p' "$DURUM_DOSYASI" | head -1)"
    ONCEKI_SON="$(sed -n 's/^son=//p' "$DURUM_DOSYASI" | head -1)"
    case "$ONCEKI_SON" in (*[!0-9]*|"") ONCEKI_SON=0 ;; esac
fi

if [ "$OLAY" = "dustu" ]; then
    printf 'kimlik=%s\nson=%s\nhal=DUSTU\nbirim=%s\n' \
        "$KIMLIK" "$SIMDI" "$BIRIM" > "$DURUM_DOSYASI"

    if [ "$ONCEKI_KIMLIK" = "$KIMLIK" ] \
       && [ "$(( SIMDI - ONCEKI_SON ))" -lt "$SUSTURMA_SANIYE" ]; then
        printf '%s [SUSTURULDU] birim=%s kimlik=%s aynı arıza %s sn önce bildirildi (sınır %s sn)\n' \
            "$ZAMAN" "$BIRIM" "$KIMLIK" "$(( SIMDI - ONCEKI_SON ))" "$SUSTURMA_SANIYE" >> "$GUNLUK"
        exit 0
    fi
else
    # Düzeldi: arıza kaydı kapanıyor.
    rm -f "$DURUM_DOSYASI"
fi

printf '%s [UYARI] olay=%s birim=%s durum=%s\n' "$ZAMAN" "$OLAY" "$BIRIM" "$DURUM" >> "$GUNLUK"

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
    # KONU DA OLAYI SÖYLER: gövde "DUZELDI" derken konunun "dustu"
    # demesi, posta kutusunda bakan kişiyi yanıltırdı — ve çoğu kişi
    # yalnız konuya bakar.
    if [ "$OLAY" = "duzeldi" ]; then
        printf 'Subject: [ENDERUN] birim duzeldi: %s\n' "$BIRIM"
    else
        printf 'Subject: [ENDERUN] birim dustu: %s\n' "$BIRIM"
    fi
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

# KURU KOŞU KAPISI — ORTAMDA **VE** DİSKTE.
#
# 2026-09-08'de bu betiği sondalarken kuru koşuyu systemd şablonuna
# `Environment=UYARI_KURU=1` drop-in'i ile açtım. Şablon üzerinden
# tetiklenen çağrılar kuru koştu; ama `nobet.sh` bu betiği DOĞRUDAN
# çağırıyor ve drop-in o yolu hiç görmedi. Sonuç: sondanın "düzeldi"
# ayağı GERÇEK BİR POSTA gönderdi.
#
# Gönderim kapısı bu dersi zaten almıştı ("kapı artık sürecin
# ortamında değil, DİSKTE duruyor; tetikleyen kim olursa olsun aynı
# kapı geçerli") — kuru koşu kapısı almamıştı. Şimdi aldı.
KURU_DOSYASI="/etc/enderunai/uyari-kuru"
if [ -f "$KURU_DOSYASI" ] || [ "${UYARI_KURU:-0}" = "1" ]; then
    printf '%s [KURU-KOSU] e-posta olusturuldu, GONDERILMEDI (%s bayt, alici tanimli, kaynak=%s)\n' \
        "$ZAMAN" "$(wc -c < "$MESAJ")" \
        "$([ -f "$KURU_DOSYASI" ] && echo dosya || echo ortam)" >> "$GUNLUK"
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
