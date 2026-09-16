#!/usr/bin/env bash
#
# ═══════════════════════════════════════════════════════════════════
# SABAH RAPORU — HATIRLAMAYA BAĞLI OLMAYAN RAPOR
# ═══════════════════════════════════════════════════════════════════
#
# ═══ NEDEN VAR (2026-09-16) ═══
#
# 16 Eylül raporu 09:00'da hazır olmadı; 13:09'da yazıldı. Kusur
# disiplinde değil YAPIDAYDI: rapor birinin hatırlamasına bağlıydı.
# Mehmet Bey'in düzeltmesi: zamanlayıcıya bağla.
#
# ═══ İLK SATIR OKUNMAMIŞ KIRMIZI SAYISIDIR ═══
#
# Raporun en üstünde başka hiçbir şey olmaz. 15 Eylül'de bir kapı
# kırmızı yandı ve 15 saat görülmedi (Kural 97); o kırmızının artık
# saklanabileceği bir yer yok.
#
# ═══ "RAPOR YOK" İLE "RAPOR TEMİZ" AYRI ŞEYLERDİR ═══
#
# Bu betik düşerse birim `OnFailure=` ile okunmamış kırmızılar
# defterine yazar. Yani üretilemeyen rapor SESSİZ KALMAZ: ertesi
# yayının uçuş öncesi kapısı onu görür ve durur.
#
# Ayrıca raporun kendi damgası var (ÜRETİM): bayat bir raporu taze
# sanmak, raporsuz kalmaktan kötüdür.
#
set -uo pipefail

KOK="${REPO_KOK:-/var/www/enderun-ai}"
CIKTI="${SABAH_RAPORU:-/var/lib/enderun-ai/sabah-raporu.txt}"
DEFTER="${KIRMIZI_DEFTERI:-/var/lib/enderun-ai/okunmamis-kirmizilar.txt}"
GUNLUK="${NGINX_GUNLUK:-/var/log/nginx/access.log}"

mkdir -p "$(dirname "$CIKTI")" 2>/dev/null || true

# Sayımlar ÖNCE yapılır; biri patlarsa rapor yarım yazılmaz, betik düşer
# ve düşüşü deftere yazılır (yarım rapor, yanlış güven verir).
okunmamis="$(grep -c . "$DEFTER" 2>/dev/null || true)"
okunmamis="${okunmamis:-0}"

bugun="$(date -u '+%d/%b/%Y')"
ham="$(mktemp)"; trap 'rm -f "$ham"' EXIT
grep -F "[$bugun" "$GUNLUK" > "$ham" 2>/dev/null || true

say() { awk "$1" "$ham" 2>/dev/null | wc -l; }
toplam="$(wc -l < "$ham")"
x5="$(say '$9>=500 && $9!=502')"
b502="$(say '$9==502')"
a401="$(say '$9==401')"
a429="$(say '$9==429')"
cikis="$(grep -c 'auth/logout' "$ham" 2>/dev/null || true)"; cikis="${cikis:-0}"
giris="$(grep 'POST /api/auth/login' "$ham" 2>/dev/null | awk '$9==200' | wc -l)"

saglik="$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5155/api/health --max-time 10 || echo 000)"
surum="$(curl -s http://127.0.0.1:5155/api/health --max-time 10 2>/dev/null \
         | grep -oE '"surum":"[^"]*"' | cut -d'"' -f4 || true)"
yayinda="$(cut -c1-8 /var/lib/enderun-ai/last-deployed-commit 2>/dev/null || echo bilinmiyor)"
takim="$(tr '\n' ' ' < /var/lib/enderun-ai/tam-takim-son.txt 2>/dev/null || echo 'damga yok')"
tatbikat="$(tr '\n' ' ' < /var/lib/enderun-ai/tatbikat-son-basari.txt 2>/dev/null || echo 'damga yok')"
yedek="$(ls -t /var/backups/enderun/db_*.dump.gpg 2>/dev/null | head -1)"
yedek_yas="?"
[ -n "$yedek" ] && yedek_yas="$(( ( $(date +%s) - $(stat -c %Y "$yedek") ) / 3600 )) saat"
agac="$(git -C "$KOK" status --porcelain 2>/dev/null | wc -l)"

{
  # ── İLK SATIR: BAŞKA HİÇBİR ŞEY YAZILMAZ ────────────────────────
  if [ "$okunmamis" -gt 0 ]; then
      echo "OKUNMAMIŞ KIRMIZI: $okunmamis  ← ÖNCE BUNU OKUYUN"
      echo
      sed 's/^/    /' "$DEFTER"
  else
      echo "OKUNMAMIŞ KIRMIZI: 0"
  fi
  echo
  echo "ÜRETİM: $(date -u '+%Y-%m-%dT%H:%M:%SZ')  (bu damga bayatsa rapor bayattır)"
  echo "══════════════════════════════════════════════════════════"
  echo "CANLI"
  echo "  sürüm         : ${surum:-okunamadı}   (son yayın kaydı: $yayinda)"
  echo "  sağlık        : $saglik"
  echo "  yedek tazeliği: ${yedek_yas}"
  echo "  tatbikat      : $tatbikat"
  echo "  gece takımı   : $takim"
  echo "  çalışma ağacı : $agac kirli dosya"
  echo
  echo "BUGÜN ($bugun, UTC)"
  echo "  toplam istek  : $toplam"
  echo "  502 dışı 5xx  : $x5        ← 0 olmalı"
  echo "  502           : $b502       (yalnız yayın takas penceresi beklenir)"
  echo "  401           : $a401"
  echo "  429           : $a429"
  echo "  çıkış         : $cikis"
  echo "  BAŞARILI GİRİŞ: $giris"
  if [ "$toplam" -lt 50 ]; then
      echo "  ⚠ ÖLÇEMEDİ: günlük trafik 50'nin altında; bu sayılar hiçbir şey kanıtlamaz."
  fi
} > "$CIKTI"

echo "[sabah-raporu] yazıldı: $CIKTI (okunmamış kırmızı: $okunmamis)"
