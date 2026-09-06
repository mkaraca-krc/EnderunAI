#!/usr/bin/env bash
#
# CC'Yİ tmux OTURUMUNDA BAŞLATIR.
#
# Oturum `cc-oturum.service` tarafından kuruluyor; bu betik yalnız
# panelde CC'yi çalıştırır.
#
# `--resume <oturum>` verilirse o konuşma kaldığı yerden sürer;
# verilmezse yeni bir konuşma başlar.
#
# İZİN KİPİNE BU BETİK KARAR VERMEZ. `--dangerously-skip-permissions`
# gibi bir bayrak burada YOK: izin kipini sessizce genişletmek,
# paketin bütün dar-taraf disiplinini anlamsız kılardı.
#
# PANEL BOŞ DEĞİLSE ÜZERİNE YAZMAZ — koşan bir işi öldürmemek için.

set -uo pipefail

OTURUM="cc"
DEVAM="${1:-}"

tmux has-session -t "$OTURUM" 2>/dev/null || {
    echo "HATA: '$OTURUM' oturumu yok. Önce: systemctl start cc-oturum.service" >&2
    exit 1
}

MEVCUT="$(tmux display-message -t "$OTURUM" -p '#{pane_current_command}')"
if [ "$MEVCUT" != "bash" ] && [ "$MEVCUT" != "sh" ]; then
    echo "HATA: panelde zaten '$MEVCUT' koşuyor — üzerine yazılmadı." >&2
    exit 1
fi

if [ -n "$DEVAM" ]; then
    tmux send-keys -t "$OTURUM" "cd /var/www/enderun-ai && exec claude --resume $DEVAM" Enter
    echo "CC başlatıldı (devam: $DEVAM)."
else
    tmux send-keys -t "$OTURUM" "cd /var/www/enderun-ai && exec claude" Enter
    echo "CC başlatıldı (yeni konuşma)."
fi
