#!/usr/bin/env bash
#
# YEDEK ŞİFRELEME ANAHTARININ BASILI KOPYASINI ÜRETİR.
#
# Anahtar kaybolursa şifreli yedek kurtarılamaz. Sunucudaki kopya
# gece yedeğinin çalışması için gerekli; ama kurtarma senaryosu
# "sunucu tümüyle gitti" ise anahtar sunucuda ARANAMAZ. Bu yüzden
# sunucu dışında iki bağımsız kopya tutuluyor: parola yöneticisi ve
# kasada basılı kopya.
#
# ─────────────────────────────────────────────────────────────────
# BU BETİK ANAHTARI EKRANA YAZAR. Kayda, günlüğe veya bir dosyaya
# YÖNLENDİRMEYİN. Çıktısını başka bir programa boru ile bağlamayın.
# Yazdırdıktan sonra terminal geçmişini temizleyin.
# ─────────────────────────────────────────────────────────────────
#
# BİÇİM: 4'lü bloklar. Elle yazarken ve okurken hata payını düşürür;
# 64 karakteri kesintisiz okumak, atlanan tek karakteri fark
# ettirmez.
#
# KONTROL TOPLAMI: anahtarın SHA-256 özetinin ilk 8 karakteri.
# Basılı kopyadan geri yazıldığında bu değer yeniden hesaplanıp
# karşılaştırılır — yazım hatası ANAHTARI DENEMEDEN yakalanır.
# Kontrol toplamı anahtarı ELE VERMEZ: özetten geri dönüş yok.

set -uo pipefail

BACKUP_KEY_FILE="${1:-/etc/enderunai/backup-key}"

if [ ! -s "$BACKUP_KEY_FILE" ]; then
    echo "HATA: anahtar dosyası yok veya boş: $BACKUP_KEY_FILE" >&2
    exit 1
fi

if [ ! -t 1 ]; then
    echo "HATA: çıktı bir terminale bağlı değil." >&2
    echo "Bu betiğin çıktısı dosyaya veya boruya YÖNLENDİRİLEMEZ — anahtar sır." >&2
    exit 1
fi

ANAHTAR="$(cat "$BACKUP_KEY_FILE")"
OZET="$(printf '%s' "$ANAHTAR" | sha256sum | cut -c1-8)"

echo
echo "  ENDERUN AI — YEDEK ŞİFRELEME ANAHTARI"
echo "  Üretim tarihi: $(stat -c %y "$BACKUP_KEY_FILE" | cut -d' ' -f1)"
echo "  Uzunluk: ${#ANAHTAR} karakter"
echo
echo "$ANAHTAR" | fold -w4 | paste -sd' ' - | fold -s -w 60 | sed 's/^/  /'
echo
echo "  KONTROL TOPLAMI: $OZET"
echo
echo "  DOĞRULAMA (geri yazdıktan sonra):"
echo "    printf '%s' '<bosluksuz-anahtar>' | sha256sum | cut -c1-8"
echo "    Çıkan değer $OZET ile AYNI olmalı."
echo
echo "  Bu kâğıt kasada saklanır. Aynı değerin bir kopyası parola"
echo "  yöneticisinde durur. Sunucudaki kopya yalnız gece yedeğinin"
echo "  çalışması içindir ve kurtarmada güvenilemez."
echo
