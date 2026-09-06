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

o(){ printf '%s\n' "$*" >> "$GECICI"; }

o "ENDERUN AI — CC DURUM RAPORU"
o "uretildi: $(date -u +%Y-%m-%dT%H:%M:%SZ) (UTC)   ~$(TZ=Europe/Istanbul date +%H:%M) TR"
o "════════════════════════════════════════════════════════════"
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
