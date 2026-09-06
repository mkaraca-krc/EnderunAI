#!/usr/bin/env bash
#
# CC RAPORU — DIŞARIDAN OKUNAN TEK DOSYAYI ÜRETİR (KUTU/1 Parça 2).
#
# İKİ KAYNAK BİRLEŞİYOR:
#   1. CANLI OLGULAR — burada ölçülüyor (git, dağıtım, servisler,
#      sağlık, CC'nin nerede koştuğu). Kimse elle yazmadığı için
#      eskimezler.
#   2. ANLATI — /var/lib/enderun-ai/rapor-durum.txt. CC her paket
#      sonunda buraya yazar: ne bitti, sondalar ne dedi, açık
#      kararlar neler, şu an ne yapılıyor.
#
# NEDEN İKİSİ AYRI: elle yazılan bir "sistem sağlıklı" satırı,
# sistem çökse bile orada durur. Ölçülen satır duramaz.
#
# SIR YAZILMAZ. Bu betik hiçbir ortam değişkeni, bağlantı dizesi,
# belirteç ya da kişisel veri basmaz; bastığı her alan sabittir.
# Rapor yolunun kendisi de burada GEÇMEZ.

set -uo pipefail

HEDEF="/var/www/enderun-rapor/son.txt"
ANLATI="/var/lib/enderun-ai/rapor-durum.txt"
GECICI="$(mktemp)"
SATIR_TAVANI=200

# ARALIK — enderun-rapor.timer ile AYNI olmalı (OnUnitActiveSec=2min).
# Yaş uyarısının eşiği bunun üç katı.
ARALIK_SN=120
ESKI_ESIK=$(( ARALIK_SN * 3 ))

# ── KANAL KENDİ YAŞINI YAYINLAMALI ──────────────────────────────
#
# KURAL (Mehmet, 2026-09-06): *"Bir kanalın sessizliği, iyi haber ile
# ölümü aynı gösterir. Kanal kendi yaşını yayınlamıyorsa, o kanal
# güvenilmezdir."*
#
# 2026-09-06'da bu kanal 03:58'den 07:14'e kadar öldü ve dosya donmuş
# hâlde durdu. Okuyan için "her şey yolunda" ile "üretim durdu"
# birbirinden ayırt edilemiyordu.
#
# İKİ AYRI ÖNLEM, İKİ AYRI ARIZAYI KARŞILAR:
#   1. EN ÜSTTEKİ SAAT — üretim TAMAMEN ölse bile işe yarar. Dosya
#      donar, saat eskir, okuyan farkı görür. Bunun için okuyana
#      ölçütü de veriyoruz: "6 dakikadan eskiyse kanal ölmüştür."
#   2. KESİNTİ İTİRAFI — üretim geri DÖNDÜĞÜNDE, önceki dosyanın yaşı
#      ölçülüp raporun başına yazılır. Kesinti sessizce kapanmaz.
#
# (2) tek başına yetmez: üretim geri dönmezse itiraf da yazılmaz.
# (1) tek başına yetmez: okuyan raporu açmazsa kimse görmez. İkisi
# birlikte, "açtığımda görürüm" güvencesini verir.
ONCEKI_YAS=""
if [ -f "$HEDEF" ]; then
    ONCEKI_YAS=$(( $(date +%s) - $(stat -c %Y "$HEDEF" 2>/dev/null || echo 0) ))
fi

o(){ printf '%s\n' "$*" >> "$GECICI"; }

o "ENDERUN AI — CC DURUM RAPORU"
o "SON GUNCELLEME : $(TZ=Europe/Istanbul date '+%Y-%m-%d %H:%M:%S') TR   ($(date -u +%H:%M:%SZ) UTC)"
o "TAZELIK OLCUTU : bu rapor 2 dakikada bir yenilenir."
o "                 Ustteki saat ile SIZIN saatiniz arasinda 6 dakikadan"
o "                 fazla fark varsa RAPOR URETIMI DURMUS demektir."
o "════════════════════════════════════════════════════════════"

if [ -n "$ONCEKI_YAS" ] && [ "$ONCEKI_YAS" -gt "$ESKI_ESIK" ]; then
    o ""
    o "############################################################"
    o "#  KESINTI OLDU — RAPOR URETIMI $(( ONCEKI_YAS / 60 )) DAKIKA DURMUSTU."
    o "#  Bu satiri goruyorsaniz kanal geri dondu, ama arada"
    o "#  $(( ONCEKI_YAS / 60 )) dakika boyunca burasi ESKI BILGI gosterdi."
    o "############################################################"
fi

o ""
o "── CANLI OLGULAR (ölçüldü, elle yazılmadı) ──"

cd /var/www/enderun-ai 2>/dev/null || o "  UYARI: depo dizinine girilemedi"
# NOT: `cut -c` bayt sayıyor ve çok baytlı karakteri ortadan
# kesiyordu (ölçüldü: "varsay?"). Kırpma kaldırıldı — bozuk bir
# başlık, uzun bir başlıktan kötüdür.
o "  HEAD            : $(git log -1 --format='%h %s' 2>/dev/null)"
o "  origin/main     : $(git rev-parse --short origin/main 2>/dev/null)"
o "  dagitilan commit: $(cut -c1-8 /var/lib/enderun-ai/last-deployed-commit 2>/dev/null)"
o "  itilmemis commit: $(git log --oneline origin/main..HEAD 2>/dev/null | wc -l)"
o "  calisma agaci   : $(git status --porcelain 2>/dev/null | wc -l) degisiklik"
o "  son safe-deploy : $(tr '\n' ' ' < /var/lib/enderun-ai/son-kosu 2>/dev/null)"

o ""
o "  backend  : $(systemctl is-active enderunai-backend.service 2>/dev/null) (baslangic $(systemctl show enderunai-backend.service -p ExecMainStartTimestamp --value 2>/dev/null | cut -c1-25))"
o "  frontend : $(systemctl is-active enderunai-frontend.service 2>/dev/null)"
o "  saglik   : HTTP $(curl -s -o /dev/null -m 8 -w '%{http_code}' http://127.0.0.1:5155/api/health 2>/dev/null)"

o ""
o "  tmux oturumu 'cc' : $(tmux has-session -t cc 2>/dev/null && echo VAR || echo YOK)"
o "  tmux panelinde    : $(tmux display-message -t cc -p '#{pane_current_command}' 2>/dev/null || echo '-')"
o "  cc-oturum.service : $(systemctl is-active cc-oturum.service 2>/dev/null)"
o "  cc-devir.timer    : $(systemctl is-active cc-devir.timer 2>/dev/null) / devir tetigi: $([ -f /var/lib/enderun-ai/cc-devir.conf ] && echo BEKLIYOR || echo 'yok (devir yapildi ya da kurulmadi)')"

o ""
# ── YEDEK GERİ YÜKLENEBİLİYOR MU ────────────────────────────────
#
# Damga YALNIZ başarılı bir tatbikatta yazılıyor; yaşı, "en son ne
# zaman gerçekten geri yükleyebildik" sorusunun cevabıdır. Tatbikat
# her gece 03:30'da koşuyor, yani 48 saatten eski bir damga iki
# gecenin kaçırıldığı anlamına gelir.
TATBIKAT_DAMGA="/var/lib/enderun-ai/tatbikat-son-basari.txt"
if [ -f "$TATBIKAT_DAMGA" ]; then
    t_yas=$(( ( $(date +%s) - $(stat -c %Y "$TATBIKAT_DAMGA" 2>/dev/null || echo 0) ) / 3600 ))
    t_bilgi="$(tr '\n' ' ' < "$TATBIKAT_DAMGA")"
    if [ "$t_yas" -gt 48 ]; then
        o "  geri yukleme  : !!! DAMGA ${t_yas} SAAT ESKI — iki gece kacirildi !!!"
    else
        o "  geri yukleme  : ${t_yas} saat once basarili"
    fi
    o "                  ${t_bilgi}"
else
    o "  geri yukleme  : !!! DAMGA YOK — tatbikat hic basariyla kosmadi !!!"
fi

o ""
o "── ANLATI (CC yaziyor) ──"
if [ -s "$ANLATI" ]; then
    cat "$ANLATI" >> "$GECICI"
else
    o "  (anlati dosyasi bos)"
fi

# SON N SATIR: dosya sinirsiz buyumesin. Baslik korunuyor ki
# kirpilmis bir rapor bile ne oldugunu soylesin.
if [ "$(wc -l < "$GECICI")" -gt "$SATIR_TAVANI" ]; then
    { head -4 "$GECICI"; echo "  ... (rapor ${SATIR_TAVANI} satira kirpildi) ..."; tail -n "$((SATIR_TAVANI-5))" "$GECICI"; } > "${GECICI}.k"
    mv "${GECICI}.k" "$GECICI"
fi

install -m 644 "$GECICI" "$HEDEF"
rm -f "$GECICI"
