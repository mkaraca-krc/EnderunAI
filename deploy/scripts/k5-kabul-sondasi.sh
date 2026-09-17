#!/usr/bin/env bash
#
# K5 KABUL ALETİNİN SONDASI — ALET YEŞİL DE DİYEBİLİYOR MU?
#
# ═══ NEDEN VAR (Mehmet Bey, 2026-09-17) ═══
#
# *"Bir alet hiç yeşil diyemiyorsa, kırmızısı da bilgi taşımaz."*
#
# `k5-kabul-olcumu.sh` yayındaki pakette üç kırmızı verdi. Ama kırmızı
# taban tek başına aletin çalıştığını göstermez — alet her şeye kırmızı
# diyor da olabilir.
#
# Ayrıca o aletin İLK HÂLİNDE gerçek bir kusur vardı: küçültülmüş
# CSS'te `head -1` medya sorgusu içindeki kuralı taban sanıyordu ve
# ölçüt YANLIŞ SEBEPLE geçiyordu. Bu sonda o kusurun geri gelmediğini
# de sınar.
#
# Aynı gün İKİ KEZ "yanlış sebeple yeşil" görüldü (denetim banner'ının
# testi ve bu aletin [2/3]'ü). Üçüncüsü dağıtım gecesinde görülmesin.
#
# ═══ İKİ AYAK ═══
#
#   POZİTİF : düzeltilmiş paket  -> ÜÇÜ DE YEŞİL, çıkış 0
#   NEGATİF : karma DEĞİŞMİŞ ama taban repeat(4,1fr), ve medya
#             kuralları ÖNDE -> [2/3] ve [3/3] KIRMIZI, çıkış 1
#
# Negatif ayak bilerek zor: karma değiştiği için naif bir alet
# "indi" der ve geçer; medya kuralları önde olduğu için ayıklama
# yapmayan alet `1fr` görüp geçer.
#
set -uo pipefail
KOK="/var/www/enderun-ai"
PORT="${K5_SONDA_PORT:-8099}"
S="$(mktemp -d /tmp/k5-sonda-XXXXXX)"
SRV=""

temizle() { [ -n "$SRV" ] && kill "$SRV" 2>/dev/null; rm -rf "$S"; }
trap temizle EXIT

mkdir -p "$S/_next/static/chunks" "$S/login"

# POZİTİF paket
cat > "$S/_next/static/chunks/yeni-karma-abc123.css" <<'CSS'
.erp-panel{padding:22px}@media(max-width:760px){.erp-quick-grid{grid-template-columns:1fr}}.erp-quick-grid{display:grid;grid-template-columns:repeat(2, minmax(0, 1fr));gap:12px}.erp-quick-grid a{border:1px solid #ccc}
CSS

# NEGATİF paket — medya kuralları ÖNDE, taban repeat(4,1fr)
cat > "$S/_next/static/chunks/tuzak-karma-xyz789.css" <<'CSS'
@media(max-width:760px){.erp-quick-grid{grid-template-columns:1fr}}@media(max-width:1000px){.erp-quick-grid{grid-template-columns:repeat(2,1fr)}}.erp-quick-grid{display:grid;grid-template-columns:repeat(4,1fr);gap:10px}
CSS

sayfa_yaz() {
    cat > "$S/login/index.html" <<HTML
<!doctype html><html><head>
<link rel="stylesheet" href="/_next/static/chunks/$1" data-precedence="next"/>
</head><body>sonda</body></html>
HTML
}

( cd "$S" && python3 -m http.server "$PORT" --bind 127.0.0.1 >/dev/null 2>&1 & echo $! > "$S/srv.pid" )
sleep 2
SRV="$(cat "$S/srv.pid" 2>/dev/null || true)"

gecti=0; dustu=0
ayak() {  # $1 ad, $2 css dosyası, $3 beklenen çıkış
    sayfa_yaz "$2"
    local kod
    K5_ADRES="http://127.0.0.1:${PORT}" K5_ESKI_KARMA=0d-p5jtxagtp5 \
        "${KOK}/deploy/scripts/k5-kabul-olcumu.sh" >/dev/null 2>&1
    kod=$?
    if [ "$kod" = "$3" ]; then
        echo "  ✓ $1 (çıkış $kod)"; gecti=$((gecti+1))
    else
        echo "  ✗ $1 — çıkış $kod, beklenen $3"; dustu=$((dustu+1))
    fi
}

echo "[sonda] k5 kabul aleti"
ayak "POZİTİF: düzeltilmiş paket → ÜÇÜ DE YEŞİL" yeni-karma-abc123.css 0
ayak "NEGATİF: karma değişmiş ama taban repeat(4,1fr) → KIRMIZI" tuzak-karma-xyz789.css 1

echo "[sonda] geçti: ${gecti} · düştü: ${dustu}"
[ "$dustu" -eq 0 ] || exit 1
