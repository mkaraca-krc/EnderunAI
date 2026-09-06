#!/usr/bin/env bash
#
# CC OTURUMU — BAĞLANTIDAN BAĞIMSIZ KABUK.
#
# NEDEN VAR: 2026-09-04'te ölçüldü — CC, VS Code'un entegre
# terminalinde koşuyordu. Süreç zinciri:
#   claude -> bash -> ptyHost -> code-server(--enable-remote-auto-shutdown)
# Yani GM'nin bilgisayarı bağlantıyı kestiğinde code-server kendini
# kapatıyor, pty kapanıyor ve CC SIGHUP ile ölüyordu. "İş devam eder"
# ifadesi ölçülmeden doğru sanılıyordu; ölçüldüğünde YANLIŞ çıktı.
#
# NEDEN SYSTEMD BİRİMİ, DÜZ `tmux new-session` DEĞİL:
# Düz tmux da systemd'nin (PID 1) çocuğu oluyor, ama DOĞDUĞU
# cgroup'ta kalıyor — bugünkü ölçümde o cgroup
# `user.slice/user-0.slice/session-1689.scope`, yani VS Code'un
# oturumu. `KillUserProcesses=no` olduğu için bugün hayatta kalırdı;
# ama bu, AYARIN bugünkü değerine bağlı bir hayatta kalma. Ayar
# değişirse ya da oturum farklı kapanırsa dayanak yok olur.
#
# Kendi birimi olduğunda tmux sunucusu kendi cgroup'unda doğar ve
# hiçbir giriş oturumuna bağlı olmaz. Dayanak ayar değil, yapı olur.
#
# İDEMPOTENT: oturum varsa dokunmaz. Birim yeniden başlatılırsa
# koşan iş kaybolmaz.

set -uo pipefail

OTURUM="cc"
CALISMA_DIZINI="/var/www/enderun-ai"

if tmux has-session -t "$OTURUM" 2>/dev/null; then
    echo "oturum '$OTURUM' zaten var — dokunulmadı."
    exit 0
fi

tmux new-session -d -s "$OTURUM" -c "$CALISMA_DIZINI"
echo "oturum '$OTURUM' kuruldu."
