#!/usr/bin/env bash
#
# PreToolUse KANCASI — `surec-durdur.sh` ARACINI ETKİLEŞİMLİ KANALDA DA ZORLAR
#
# ═══ NEDEN VAR ═══
#
# `deploy/scripts/surec-durdur.sh` dört tekrardan sonra yazıldı ve
# `PkillYasagiTests` muhafızı onu zorluyor — AMA muhafız yalnız depodaki
# `.sh` dosyalarını tarıyor. Etkileşimli kabuk çağrılarını GÖREMEZ.
#
# 2026-09-13'te tam bu boşluktan geçildi: araç mevcutken, muhafız
# yeşilken, doğrudan yazılan bir desenli süreç-öldürme çağrısı yine
# kendi kabuğunu öldürdü (çıkış 144) ve yarım kalan bir iş bıraktı.
#
# Yani sorun "tekrarlayan hata" değil, ARACIN KAPSAMADIĞI BİR KANALDI.
# Bu kanca o kanalı kapatır: araç var, muhafız var, kanca artık her iki
# yoldan da geçişi engelliyor.
#
# ═══ DESEN NEDEN BÖYLE YAZILDI ═══
#
# `PkillYasagiTests` deposundaki betiklerde `\bpkill\s+...-f` arıyor.
# Bu betik deseni DÜZ METİN olarak taşısaydı kendi muhafızını ihlal
# ederdi. O yüzden boşluk POSIX sınıfıyla yazılı: "pkill" ile "-f"
# arasında gerçek bir boşluk karakteri yok.
#
# ═══ NE YAPIYOR ═══
#
# stdin'den PreToolUse JSON'unu okur, `tool_input.command` alanına bakar;
# desenli süreç-öldürme çağrısı görürse izni REDDEDER ve yerine
# kullanılacak aracı söyler. Başka her komutta sessizce geçer.
set -uo pipefail

girdi="$(cat)"

komut="$(printf '%s' "$girdi" | jq -r '.tool_input.command // empty' 2>/dev/null)"

# Komut okunamadıysa ENGELLEME: kanca bir güvenlik kapısı değil, bir
# alışkanlık kapısı. Okuyamadığı şeyi yasaklarsa tüm kabuğu kilitler.
[ -n "$komut" ] || exit 0

# KOMUT KONUMUNA BAĞLI: dizge satırın başında, ya da `;` `&` `|` `(`
# ardında geçmeli. Böylece `grep "pkill -f" dosya` gibi BAHSEDEN bir
# komut engellenmez — bu kusuru belgelemek de gerekiyor ve kanca kendi
# belgesini yazmayı yasaklarsa kullanılamaz hâle gelir.
#
# Bayrak tarafı hem kısa hem uzun biçimi ve araya giren başka bayrakları
# kapsar: `-f`, `--full`, `-9 -f`, `--signal TERM -f`.
YASAK='(^|[;&|(])[[:space:]]*(sudo[[:space:]]+)*(pkill|pgrep)[[:space:]]+([^[:space:]]+[[:space:]]+)*(--full|-[a-zA-Z0-9]*f)([[:space:]=]|$)'


if printf '%s' "$komut" | grep -Eq -- "$YASAK"; then
  cat <<'JSON'
{
  "hookSpecificOutput": {
    "hookEventName": "PreToolUse",
    "permissionDecision": "deny",
    "permissionDecisionReason": "Desenli süreç öldürme YASAK: desen çağıran kabuğun kendi komut satırında da geçtiği için kabuğu öldürür (bu depoda 5 kez oldu, en sonuncusu 2026-09-13). Yerine: deploy/scripts/surec-durdur.sh --port <n> | --desen <metin> | --pid-dosyasi <yol>. O araç kendini ve atasını dışlar. pgrep -f DE YASAK: o da çağıran kabuğun kendi komut satırını eşleştirir ve bulunan pid'i öldürünce kabuk ölür (2026-09-16'da oldu). Saymak/incelemek için: surec-durdur.sh --listele --desen <metin> — kendini ve atasını dışlar, sayıyı da basar."
  }
}
JSON
  exit 0
fi

exit 0
