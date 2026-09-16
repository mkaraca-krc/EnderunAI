#!/usr/bin/env bash
#
# SÜREÇ DURDURMA — `pkill -f` YERİNE TEK ARAÇ (Y2)
#
# ═══ NEDEN VAR ═══
#
# `pkill -f <desen>` bu oturumda DÖRT KEZ kendi kabuğumu öldürdü
# (çıkış 144). Sebep her seferinde aynı: desen, çağıran kabuğun
# KENDİ komut satırında da geçiyor ve `pkill` onu da eşleştiriyor.
# Bir kez bir düzenlemenin yarıda kalmasına yol açtı.
#
# Dördüncüden sonra "dikkat edeceğim" demek çözüm değil. Tekrarlayan
# bir insan hatası kişisel dikkatle değil ARAÇLA çözülür: bu betik
# kendini ve atasını asla öldürmez ve bunu bir muhafız zorluyor
# (`PkillYasagiTests`).
#
# ═══ NE YAPIYOR ═══
#
#   surec-durdur.sh --port 3005          portu dinleyen süreci durdurur
#   surec-durdur.sh --desen "next start" desene uyanları durdurur
#                                        (KENDİSİ ve ATASI HARİÇ)
#   surec-durdur.sh --pid-dosyasi /tmp/x.pid
#   surec-durdur.sh --listele --desen "VBCSCompiler"   ÖLDÜRMEZ, sayar
#
# ═══ `--listele` NEDEN EKLENDİ (2026-09-16) ═══
#
# Bu betik `pkill -f`i kapsıyordu ama `pgrep -f`i KAPSAMIYORDU. Kancanın
# kendi tavsiye metni bile "pgrep ile pid bul, kill <pid> yap" diyordu —
# ve o yol aynı tuzağı taşıyor: `pgrep -f <desen>` çağıran kabuğun KENDİ
# komut satırını da eşleştirir. 2026-09-16'da tam bunu yaptım ve
# kabuğumu öldürdüm (çıkış 144), üstelik kural depoda yazılıyken.
#
# Aynı gün ikinci bir hâli de çıktı: SAYMA. `pgrep -fc` / `ps | grep -c`
# kendi boru hattını da sayar; "kalan: 4" dedim, gerçek sıfırdı. Bu
# yüzden `--listele` hem PID basar hem SAYIYI, ikisi de kendini ve
# atasını dışlayarak.
#
# Varsayılan TERM, 3 sn sonra hâlâ yaşıyorsa KILL.
set -uo pipefail

KENDI=$$
ATA="${PPID:-0}"

log() { echo "[surec-durdur] $*"; }

hedefler=()
kip=""
deger=""

case "${1:-}" in
    --port|--desen|--pid-dosyasi) kip="$1"; deger="${2:?deger gerekli}" ;;
    --listele) kip="$1"; deger="" ;;
    *) echo "kullanım: $0 --port <n> | --desen <metin> | --pid-dosyasi <yol> | --listele --desen <metin>" >&2; exit 64 ;;
esac

# --listele: ÖLDÜRMEZ. Sayma ve inceleme için güvenli yol.
if [ "$kip" = "--listele" ]; then
    kip="${2:-}"; deger="${3:-}"
    [ "$kip" = "--desen" ] || { log "--listele yalnız --desen ile kullanılır."; exit 2; }
    n=0
    while read -r p; do
        [ -z "$p" ] && continue
        [ "$p" = "$KENDI" ] && continue
        [ "$p" = "$ATA" ] && continue
        printf '%s %s\n' "$p" "$(ps -p "$p" -o rss= 2>/dev/null | tr -d ' ')"
        n=$((n + 1))
    done < <(pgrep -f -- "$deger" 2>/dev/null)
    log "eşleşen (kendisi ve atası hariç): $n"
    exit 0
fi

case "$kip" in
    --port)
        while read -r p; do
            [ -n "$p" ] && hedefler+=("$p")
        done < <(ss -ltnp "sport = :${deger}" 2>/dev/null \
                 | grep -oE 'pid=[0-9]+' | cut -d= -f2 | sort -u)
        ;;
    --desen)
        # KENDİNİ VE ATASINI DIŞLA — `pkill -f`in yapmadığı tek şey.
        while read -r p; do
            [ -z "$p" ] && continue
            [ "$p" = "$KENDI" ] && continue
            [ "$p" = "$ATA" ] && continue
            hedefler+=("$p")
        done < <(pgrep -f -- "$deger" 2>/dev/null)
        ;;
    --pid-dosyasi)
        [ -f "$deger" ] || { log "PID dosyası yok: $deger"; exit 0; }
        p="$(cat "$deger" 2>/dev/null)"
        [ -n "$p" ] && hedefler+=("$p")
        ;;
esac

if [ "${#hedefler[@]}" -eq 0 ]; then
    # BOŞ SONUÇ SESSİZ GEÇMİYOR: "durdurulacak süreç yoktu" ile
    # "aradığım yeri bulamadım" farklı şeyler ve ikincisi hata olabilir.
    log "Durdurulacak süreç bulunamadı (${kip} ${deger})."
    exit 0
fi

log "Durdurulacak: ${hedefler[*]}"

for p in "${hedefler[@]}"; do kill -TERM "$p" 2>/dev/null || true; done
sleep 3

for p in "${hedefler[@]}"; do
    if kill -0 "$p" 2>/dev/null; then
        log "PID ${p} TERM ile durmadı, KILL gönderiliyor."
        kill -KILL "$p" 2>/dev/null || true
    fi
done

log "Bitti."
