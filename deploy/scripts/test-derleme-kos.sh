#!/usr/bin/env bash
# DERLEME KOŞUCUSU — ÖLÜ SCOPE SONDASI.
#
# ═══ DOĞURAN OLAY (2026-09-11) ═══
#
# Başarıyla biten bir rig derlemesinden sonra `enderun-derleme.scope`
# İÇİNDE HİÇ SÜREÇ YOKKEN (TasksCurrent=0) "active (running)" kaldı.
# Derleme koşucusunun tek örnek kapısı onu "koşan derleme" saydı ve bir
# sonraki rig "ÖLÇEMEDİ: derleme koşucusu meşgul" ile durdu. Kilidi
# boş bir scope işgal ediyordu; elle bulundu. Kimse bakmasaydı kilit
# sessizce işgal edilmiş kalırdı.
#
# SAHTE ARAÇLARLA DAVRANIŞ ÖLÇÜMÜ: gerçek systemd'ye dokunulmaz;
# `systemctl`, `systemd-run`, `systemd-cgls`, `dotnet` sahte sürümlerle
# PATH'in önüne konur ve betiğin ne ÇAĞIRDIĞI kaydedilir.
#
# AYAKLAR (beklenen sonuç ÖNCEDEN yazılı, Kural 61):
#   L1 canlı koşu (etkin, görev=3)       → çıkış 75, stop YOK, koşu YOK
#   L2 ölü scope (etkin, görev=0)        → "ÖLÜ SCOPE" yazılır, stop, koşu VAR
#   L3 temiz (etkin değil)               → koşu VAR, "kilidi temiz" satırı
#   L4 koşudan SONRA ölü kaldı           → stop, "ÖLÜ KALDI" yazılır, çıkış kodu KORUNUR
#   L5 görev sayısı okunamıyor (etkin)   → stop YOK, çıkış 75 (kapalı düşer)
set -uo pipefail

KOK="${REPO_ROOT:-/var/www/enderun-ai}"
KOSUCU="${KOK}/scripts/derleme-kos.sh"
GECICI="$(mktemp -d)"
trap 'rm -rf "$GECICI"' EXIT

mkdir -p "$GECICI/bin"
cat > "$GECICI/bin/systemctl" <<'SAHTE'
#!/usr/bin/env bash
echo "ÇAĞRI: systemctl $*" >> "$SAHTE_KAYIT"
durum_dosyasi="$SAHTE_DURUM"
case "$1" in
  is-active)
    [ "$(sed -n 1p "$durum_dosyasi")" = "etkin" ] && exit 0 || exit 3 ;;
  show)
    sed -n 2p "$durum_dosyasi"; exit 0 ;;
  stop)
    printf 'etkin-degil\n\n' > "$durum_dosyasi"; exit 0 ;;
  reset-failed) exit 0 ;;
esac
exit 0
SAHTE
cat > "$GECICI/bin/systemd-run" <<'SAHTE'
#!/usr/bin/env bash
echo "ÇAĞRI: systemd-run $*" >> "$SAHTE_KAYIT"
while [ "$1" != "--" ]; do shift; done; shift
"$@"; kod=$?
# L4: koşu bittikten sonra scope ölü kalsın
if [ "${SAHTE_SONRA_OLU:-0}" = "1" ]; then printf 'etkin\n0\n' > "$SAHTE_DURUM"; fi
exit $kod
SAHTE
printf '#!/usr/bin/env bash\nexit 0\n' > "$GECICI/bin/systemd-cgls"
printf '#!/usr/bin/env bash\necho "ÇAĞRI: dotnet $*" >> "$SAHTE_KAYIT"; exit 0\n' > "$GECICI/bin/dotnet"
chmod +x "$GECICI/bin/"*

gecti=0; kaldi=0
ayak() {  # ad, beklenen_kod, durum_satir1, durum_satir2, sonra_olu, komut_kodu, beklenen_kalip(ler) (| ile), yasak_kalip
  local ad="$1" bek="$2" d1="$3" d2="$4" sonra="$5" kkod="$6" kalip="$7" yasak="${8:-}"
  export SAHTE_KAYIT="$GECICI/kayit-$ad" SAHTE_DURUM="$GECICI/durum-$ad" SAHTE_SONRA_OLU="$sonra"
  : > "$SAHTE_KAYIT"; printf '%s\n%s\n' "$d1" "$d2" > "$SAHTE_DURUM"
  local cikti kod
  cikti="$(PATH="$GECICI/bin:$PATH" bash "$KOSUCU" bash -c "exit $kkod" 2>&1)"; kod=$?
  local tum; tum="$(cat "$SAHTE_KAYIT"; echo "$cikti")"
  local ok=1
  [ "$kod" = "$bek" ] || ok=0
  IFS='|' read -ra kaliplar <<< "$kalip"
  for k in "${kaliplar[@]}"; do grep -qF -- "$k" <<< "$tum" || { ok=0; echo "   eksik: '$k'"; }; done
  if [ -n "$yasak" ]; then IFS='|' read -ra yk <<< "$yasak"; for k in "${yk[@]}"; do grep -qF -- "$k" <<< "$tum" && { ok=0; echo "   olmamalıydı: '$k'"; }; done; fi
  if [ $ok = 1 ]; then echo "GEÇTİ  $ad (çıkış $kod)"; gecti=$((gecti+1)); else echo "KALDI  $ad (çıkış $kod, beklenen $bek)"; echo "$tum" | sed 's/^/     | /' | head -12; kaldi=$((kaldi+1)); fi
}

# "ÇAĞRI:" öneki SAHTE ARACIN kaydıdır. Betiğin kendi mesajı da
# "systemctl stop …" metnini içeriyor; önek olmadan mesaj çağrı sanıldı
# (ilk koşuda L1 böyle yanlış KALDI — düzenek hatası, Kural 81).
ayak L1-canli-kosu        75 etkin 3 0 0 "ZATEN KOŞAN" "ÇAĞRI: systemctl stop|ÇAĞRI: systemd-run"
ayak L2-olu-scope         0  etkin 0 0 0 "ÖLÜ SCOPE|ÇAĞRI: systemctl stop|ÇAĞRI: systemd-run"
ayak L3-temiz             0  etkin-degil "" 0 0 "kilidi temiz|ÇAĞRI: systemd-run" "ÇAĞRI: systemctl stop"
ayak L4-sonra-olu-kaldi   7  etkin-degil "" 1 7 "ÇAĞRI: systemd-run|ÖLÜ KALDI|ÇAĞRI: systemctl stop"
ayak L5-gorev-okunamiyor  75 etkin "" 0 0 "karar veremedim" "ÇAĞRI: systemctl stop|ÇAĞRI: systemd-run"

echo "sonuç: $gecti geçti, $kaldi kaldı"
[ "$kaldi" = 0 ]
