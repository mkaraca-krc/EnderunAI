#!/usr/bin/env bash
#
# HESAP BEYAN SAPMASI — ZEMİNİN BEYANI CANLIDAN AYRIŞAMAZ.
#
# ═══ NEDEN VAR (2026-09-13) ═══
#
# Test fikstürü muhasebe hesaplarını `RequiresProject` /
# `RequiresCostCenter` bayrakları OLMADAN kuruyordu. Sonuç ölçüldü:
# stok muhasebesi testleri YEŞİLDİ, üretimde stok→muhasebe hattı HİÇ
# ÇALIŞMAMIŞTI (canlıda 150/153/770/740.03.09/379.01 hesaplarına ait
# tek fiş satırı yoktu). Zemin üretimi taklit etmiyordu — Kural 81.
#
# Düzeltme, üretimin hesap yapılandırmasını TEK YERDE beyan etmek oldu:
# `EnderunAI.Api.Tests/Infrastructure/UretimHesapYapilandirmasi.cs`.
# Bir muhafız (`FiksturHesapYapilandirmasiTests`) fikstürün o beyana
# uyduğunu ölçüyor.
#
# ═══ AMA BEYANIN BİR SINIRI VAR, VE BU O SINIRI KAPATIR ═══
#
# Beyan bir BELGEDİR, canlı sorgusu değil. Canlı değişip beyan
# güncellenmezse muhafız YEŞİL KALIR — yani "etkisizleşmiş savunma"
# sınıfına düşer. Belgelenmiş bir sınır iyidir; KAPATILABİLİR bir sınır
# kapatılmalıdır.
#
# Bu betik beyanı CANLIYLA karşılaştırır ve sapmayı SAYAR.
#
# ═══ ÇİFT YÖNLÜ ÇİZGİ (şema sapma circirinin aynı deseni) ═══
#
#   · Sapma ÇİZGİYİ AŞAMAZ  → beyan ile canlı sessizce ayrıştı, kırmızı.
#   · Sapma çizgiden AZSA   → çizgi düşürülmeli. Gevşeklik bırakmak,
#                             ilerlemeyi görünmez kılar.
#
# ═══ BUGÜNKÜ DEĞER VE NEDEN SIFIR DEĞİL ═══
#
# Bugün sapma 2: `150` ve `153` hesaplarında beyan `proje=false` diyor
# (Mehmet Bey'in kararı), canlı hâlâ `true`. Bayrak değişikliği canlıya
# HENÜZ UYGULANMADI — yayınla birlikte ekrandan uygulanacak.
#
# Yani bu sayaç aynı zamanda O BEKLEYEN İŞİN İZLEYİCİSİ: canlıda
# uygulanınca 2 → 0 düşer ve çizgi 0'a çekilir.
#
# ═══ NEREDE KOŞAR ═══
#
# `ucuz-kapilar.sh` içinde AĞIR kapı olarak — şema sapma circiriyle aynı
# yerde, aynı gerekçeyle: canlı veritabanına tek sorgu atar, yayın
# öncesi koşar ve ayrışmayı yayından ÖNCE görünür kılar.
#
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BEYAN="${REPO_ROOT}/backend/EnderunAI.Api.Tests/Infrastructure/UretimHesapYapilandirmasi.cs"
CIZGI_DOSYASI="${REPO_ROOT}/deploy/bekci/hesap-beyan-sapma-cizgi.txt"

hata() { echo "[hesap-beyan] $*" >&2; }

[ -f "$BEYAN" ] || { hata "ÖLÇEMEDİ: beyan dosyası yok: $BEYAN"; exit 3; }
[ -f "$CIZGI_DOSYASI" ] || { hata "ÖLÇEMEDİ: çizgi dosyası yok: $CIZGI_DOSYASI"; exit 3; }

CIZGI="$(tr -dc '0-9' < "$CIZGI_DOSYASI")"
[ -n "$CIZGI" ] || { hata "ÖLÇEMEDİ: çizgi okunamadı."; exit 3; }

# ── Beyanı ayrıştır: new("kod", proje, merkez, "gerekçe") ────────────
BEYAN_SATIRLARI="$(grep -oE 'new\("[^"]+",[[:space:]]*(true|false),[[:space:]]*(true|false)' "$BEYAN" \
  | sed -E 's/new\("([^"]+)",[[:space:]]*(true|false),[[:space:]]*(true|false)/\1|\2|\3/')"

BEYAN_SAYI=$(printf '%s\n' "$BEYAN_SATIRLARI" | grep -c '|' || true)

# TARAMA SAĞLIĞI: beyan okunamadıysa "sapma yok" sonucu, ayrışma
# olmadığının değil AYRIŞTIRMANIN ÖLDÜĞÜNÜN kanıtı olurdu.
if [ "$BEYAN_SAYI" -lt 5 ]; then
    hata "ÖLÇEMEDİ: beyandan yalnız ${BEYAN_SAYI} hesap ayrıştırıldı (en az 5 bekleniyor)."
    hata "ÖLÇEMEDİ: dosya biçimi değişmiş olabilir. 'sapma yok' sonucu bu hâlde anlamsız."
    exit 3
fi

# ── Canlıdan oku ─────────────────────────────────────────────────────
KODLAR=$(printf '%s\n' "$BEYAN_SATIRLARI" | cut -d'|' -f1 | sed "s/^/'/;s/$/'/" | paste -sd',' -)

CANLI="$("${REPO_ROOT}/deploy/scripts/vt-sorgu.sh" --vt enderun_ai --sql \
  "SELECT \"Code\"||'|'||\"RequiresProject\"||'|'||\"RequiresCostCenter\" FROM accounting_accounts WHERE \"Code\" IN (${KODLAR}) ORDER BY \"Code\"" 2>/dev/null \
  | grep -E '^[0-9]' || true)"

CANLI_SAYI=$(printf '%s\n' "$CANLI" | grep -c '|' || true)

if [ "$CANLI_SAYI" -eq 0 ]; then
    hata "ÖLÇEMEDİ: canlıdan hiç hesap okunamadı (veritabanına ulaşılamadı?)."
    exit 3
fi

# ── Karşılaştır ──────────────────────────────────────────────────────
SAPMALAR=()
while IFS='|' read -r kod proje merkez; do
    [ -n "$kod" ] || continue
    satir="$(printf '%s\n' "$CANLI" | grep -E "^${kod}\|" | head -1)"

    if [ -z "$satir" ]; then
        # Canlıda hiç yok: beyan bir hesabı varsayıyor ama hesap planında
        # o kod yok. Sapmadır — sessiz geçilmez.
        SAPMALAR+=("${kod}: canlıda HİÇ YOK (beyan proje=${proje} merkez=${merkez})")
        continue
    fi

    c_proje="$(printf '%s' "$satir" | cut -d'|' -f2)"
    c_merkez="$(printf '%s' "$satir" | cut -d'|' -f3)"
    # BİÇİM ÖLÇÜLDÜ, VARSAYILMADI (2026-09-13): ilk yazımda psql'in
    # 't'/'f' yazdığını varsaydım ve gerisini "false" saydım — sonuç,
    # olmayan 3 sapma. Postgres `||` ile birleştirilen boole'yi
    # 'true'/'false' olarak yazar. Her iki biçim de kabul ediliyor ki
    # araç sürümü değişirse sessizce yanlış saymasın.
    case "$c_proje" in t|true|TRUE) c_proje="true" ;; f|false|FALSE) c_proje="false" ;;
        *) hata "ÖLÇEMEDİ: ${kod} proje değeri tanınmadı: '${c_proje}'"; exit 3 ;; esac
    case "$c_merkez" in t|true|TRUE) c_merkez="true" ;; f|false|FALSE) c_merkez="false" ;;
        *) hata "ÖLÇEMEDİ: ${kod} masraf merkezi değeri tanınmadı: '${c_merkez}'"; exit 3 ;; esac

    [ "$c_proje" != "$proje" ] && SAPMALAR+=("${kod}: proje beyan=${proje} canlı=${c_proje}")
    [ "$c_merkez" != "$merkez" ] && SAPMALAR+=("${kod}: masraf merkezi beyan=${merkez} canlı=${c_merkez}")
done <<< "$BEYAN_SATIRLARI"

SAPMA=${#SAPMALAR[@]}

echo "[hesap-beyan] beyan ${BEYAN_SAYI} hesap · canlıda bulunan ${CANLI_SAYI} · SAPMA ${SAPMA} (çizgi ${CIZGI})"
for s in "${SAPMALAR[@]}"; do echo "[hesap-beyan]   ${s}"; done

if [ "$SAPMA" -gt "$CIZGI" ]; then
    hata "ÇİZGİ AŞILDI: ${SAPMA} > ${CIZGI}."
    hata "Zeminin beyanı canlıdan ayrıştı. Testler bu hâlde ürünü değil"
    hata "kendini sınar. Ya canlıyı beyana, ya beyanı canlıya getirin."
    exit 1
fi

if [ "$SAPMA" -lt "$CIZGI" ]; then
    hata "ÇİZGİ GEVŞEK: gerçek ${SAPMA}, çizgi ${CIZGI} — gevşeklik $((CIZGI - SAPMA))."
    hata "Gevşeklik bırakmak ilerlemeyi görünmez kılar."
    hata "Çizgiyi ${SAPMA} yapın: ${CIZGI_DOSYASI}"
    exit 1
fi

echo "[hesap-beyan] çizgi tam: ${SAPMA}/${CIZGI}"
exit 0
