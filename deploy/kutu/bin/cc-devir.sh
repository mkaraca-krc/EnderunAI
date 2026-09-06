#!/usr/bin/env bash
#
# CC DEVRİ — VS CODE OTURUMU ÖLÜNCE tmux'TAKİ CC DEVRALIR.
#
# SORUN (ölçüldü 2026-09-04): CC, VS Code'un entegre terminalinde
# koşuyor ve o oturum `--enable-remote-auto-shutdown` ile kapanınca
# ölüyor. tmux oturumu (`cc-oturum.service`) hayatta kalıyor ama
# İÇİ BOŞ — içinde CC yoksa iş yine durur.
#
# NEDEN İKİ ÖRNEK AYNI ANDA KOŞMAMALI: ikisi de aynı konuşma
# dosyasına yazar. Bu yüzden devir, ESKİ SÜRECİN ÖLDÜĞÜ ÖLÇÜLMEDEN
# yapılmaz — "muhtemelen kapanmıştır" yetmez.
#
# FAIL-CLOSED: emin olamadığı her durumda HİÇBİR ŞEY YAPMAZ.
#   · Eski süreç hâlâ yaşıyorsa           -> çık
#   · tmux oturumu yoksa                  -> çık (hata)
#   · panelde bash/sh dışında bir şey varsa -> çık (zaten CC koşuyor)
#   · devralınacak konuşma dosyası yoksa  -> çık (hata)

set -uo pipefail

DURUM_DOSYASI="/var/lib/enderun-ai/cc-devir.conf"
GUNLUK="/var/log/enderun-cc-devir.log"
OTURUM="cc"

log() { echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) [$1] $2" >> "$GUNLUK"; }

[ -f "$DURUM_DOSYASI" ] || exit 0
# shellcheck disable=SC1090
. "$DURUM_DOSYASI"

: "${ESKI_PID:=}" "${KONUSMA:=}"
[ -n "$ESKI_PID" ] && [ -n "$KONUSMA" ] || { log ERROR "conf eksik"; exit 1; }

# 1. ESKİ SÜREÇ HÂLÂ YAŞIYOR MU — devrin tek tetikleyicisi bu.
if kill -0 "$ESKI_PID" 2>/dev/null; then
    exit 0
fi

# 2. tmux oturumu duruyor mu
tmux has-session -t "$OTURUM" 2>/dev/null || { log ERROR "tmux oturumu yok"; exit 1; }

# 3. PANEL BOŞ MU — doluysa CC zaten devralmış demektir.
MEVCUT="$(tmux display-message -t "$OTURUM" -p '#{pane_current_command}')"
if [ "$MEVCUT" != "bash" ] && [ "$MEVCUT" != "sh" ]; then
    exit 0
fi

# 4. Devralınacak konuşma gerçekten var mı
KAYIT="/root/.claude/projects/-root/${KONUSMA}.jsonl"
[ -s "$KAYIT" ] || { log ERROR "konuşma dosyası yok: $KAYIT"; exit 1; }

log INFO "eski süreç ($ESKI_PID) ölü — tmux'ta devralınıyor: $KONUSMA"
tmux send-keys -t "$OTURUM" "cd /var/www/enderun-ai && exec claude --resume ${KONUSMA}" Enter
sleep 20

# ── SÜRDÜRME SORUSU — ÖLÇÜLEREK CEVAPLANIR, KÖRLEMESİNE DEĞİL ──
#
# ÖLÇÜLDÜ (sonda D1, 2026-09-04): büyük/eski bir konuşmada
# `claude --resume` tuş bekleyen bir soru soruyor:
#     1. Resume from summary (recommended)
#     2. Resume full session as-is
# Devir bu ekranda TAKILI KALIRDI ve kimse tuşa basmayacaktı.
# "Kurdum, çalışır" demek tam olarak buydu.
#
# NEDEN KÖRLEMESİNE Enter DEĞİL: soru her zaman çıkmıyor (küçük
# konuşmalarda hiç çıkmıyor). Çıkmadığında gönderilen Enter, CC'nin
# giriş kutusuna düşer. Onun yerine EKRAN OKUNUYOR: soru görünüyorsa
# onaylanıyor, görünmüyorsa hiçbir tuş gönderilmiyor.
#
# HANGİ SEÇENEK: 1 (özet) — CC'nin kendi önerisi ve varsayılan olarak
# seçili geliyor, yani yalnız Enter yeter. Tam sürdürme kullanım
# sınırının önemli bir kısmını harcıyor ve devir gecenin ortasında,
# kimsenin bakmadığı bir anda olacak.
for _ in 1 2 3 4 5 6; do
    EKRAN="$(tmux capture-pane -t "$OTURUM" -p)"
    case "$EKRAN" in
        *"Resume from summary"*)
            log INFO "sürdürme sorusu göründü — özet seçeneği onaylanıyor"
            tmux send-keys -t "$OTURUM" Enter
            sleep 10
            break
            ;;
    esac
    sleep 5
done

log INFO "devir sonrası panelde: $(tmux display-message -t "$OTURUM" -p '#{pane_current_command}')"

# Tek seferlik: devir yapıldı, dosyayı kaldır ki döngüye girmesin.
rm -f "$DURUM_DOSYASI"
log INFO "devir tamam, tetik dosyası kaldırıldı"
