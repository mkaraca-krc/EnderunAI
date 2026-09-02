#!/bin/bash
# ═══════════════════════════════════════════════════════════════════
#  GÖÇÜ CANLIYA UYGULAMANIN ONAYLI TEK YOLU
# ═══════════════════════════════════════════════════════════════════
#
# NEDEN VAR: göç provası (`goc-provasi.sh`) safe-deploy'a BAĞLIYDI ama
# orada hiçbir zaman iş yapmıyordu. Ölçüldü: `gocleri_dogrula` bekleyen
# göç bulunca yayını zaten durduruyor, dolayısıyla deploy anında
# bekleyen göç OLMUYOR ve prova her koşuda "bekleyen göç yok" deyip
# çıkıyor. Son yayının günlüğünde aynen bu satır var.
#
# Yani prova, ÇAĞRILAN AMA HİÇBİR ZAMAN İŞ YAPMAYAN bir muhafızdı.
# Provanın işe yaradığı tek an, göçün ELLE uygulanmasından öncedir —
# ve orada onu koşmayı zorlayan hiçbir şey yoktu. Kural yazmak
# yetmezdi: kural, işi yapan kişinin hatırlamasına dayanır.
#
# BU BETİK O BAĞI MEKANİK YAPAR. Prova geçmeden canlıya tek bir DDL
# gitmez. `gocleri_dogrula` artık ham `dotnet ef database update`
# komutunu değil, bu betiği söyler.
#
# ÇIKIŞ KODLARI
#   0  göç uygulandı ve sonrası doğrulandı
#   1  reddedildi (prova düştü, yedek alınamadı, uygulama patladı)
#   2  KARAR VEREMEDİ (prova hüküm veremedi ya da ölçüm güvenilmez)
set -uo pipefail

REPO_ROOT="/var/www/enderun-ai"
ENV_FILE="/etc/enderunai/backend.env"
YEDEK_BETIGI="/usr/local/bin/enderun-backup.sh"

log()  { echo "[goc-uygula] $*"; }
hata() { echo "[goc-uygula] HATA: $*" >&2; }

canli="$(sudo grep -E '^DB_CONNECTION=' "$ENV_FILE" 2>/dev/null | sed -E 's/^DB_CONNECTION=//' | tr -d "'\"")"
[ -z "$canli" ] && { hata "DB_CONNECTION okunamadı."; exit 2; }
CANLI_DB="$(sed -n 's/.*Database=\([^;]*\).*/\1/p' <<<"$canli")"
[ -z "$CANLI_DB" ] && { hata "Canlı veritabanı adı çözülemedi."; exit 2; }

# bekleyen_kume <hedef_dosya> — canlıda uygulanmamış göçler.
# Geçmiş OKUNAMAZSA susulmaz: boş geçmiş "hiç göç yok" değildir.
bekleyen_kume() {
    local hedef="$1" kaynak gecmis ham
    kaynak="$(mktemp)"; gecmis="$(mktemp)"; ham="$(mktemp)"
    find "${REPO_ROOT}/backend/EnderunAI.Api/Migrations" -name '*.cs' 2>/dev/null \
      | grep -v Designer | grep -v Snapshot \
      | sed 's|.*/||; s|\.cs$||' | sort > "$kaynak"
    if ! sudo -u postgres psql -d "$CANLI_DB" -tAc \
            'select "MigrationId" from "__EFMigrationsHistory"' > "$ham" 2>&1; then
        rm -f "$kaynak" "$gecmis" "$ham"; return 1
    fi
    sort "$ham" | grep -v '^$' > "$gecmis"
    [ ! -s "$gecmis" ] && { rm -f "$kaynak" "$gecmis" "$ham"; return 1; }
    comm -23 "$kaynak" "$gecmis" | grep -v '^$' > "$hedef"
    rm -f "$kaynak" "$gecmis" "$ham"
    return 0
}

onces="$(mktemp)"
if ! bekleyen_kume "$onces"; then
    hata "KARAR VEREMEDİ: canlı göç geçmişi okunamadı."
    rm -f "$onces"; exit 2
fi
if [ ! -s "$onces" ]; then
    log "Bekleyen göç yok — uygulanacak bir şey yok."
    rm -f "$onces"; exit 0
fi
log "Uygulanacak göç(ler):"
sed 's/^/           /' "$onces"

# ── 1) PROVA ── canlıya dokunmadan ÖNCE, canlının taze kopyasında.
log "════ GÖÇ PROVASI ════"
"${REPO_ROOT}/deploy/scripts/goc-provasi.sh"
prova_kodu="${PIPESTATUS[0]}"

case "$prova_kodu" in
    0) log "PROVA GEÇTİ — canlıya uygulamaya devam ediliyor." ;;
    2)
        hata "════════════════════════════════════════════════"
        hata "PROVA KARAR VEREMEDİ (çıkış 2). Canlıya DOKUNULMADI."
        hata "'Karar veremedim' ile 'sorun yok' AYNI ŞEY DEĞİLDİR."
        hata "İnsan bakmalı; düzelttikten sonra bu betiği tekrar koşun."
        rm -f "$onces"; exit 2 ;;
    *)
        hata "PROVA DÜŞTÜ (çıkış ${prova_kodu}). Canlıya DOKUNULMADI."
        hata "Göç canlının kopyasında patladı — canlıda da patlardı."
        rm -f "$onces"; exit 1 ;;
esac

# ── 2) YARIŞ KONTROLÜ ── prova ile uygulama arasında küme değişmemeli.
sonras="$(mktemp)"
if ! bekleyen_kume "$sonras"; then
    hata "KARAR VEREMEDİ: prova sonrası bekleyen küme okunamadı."
    rm -f "$onces" "$sonras"; exit 2
fi
if ! diff -q "$onces" "$sonras" >/dev/null; then
    hata "KARAR VEREMEDİ: bekleyen göç kümesi prova ile uygulama arasında DEĞİŞTİ."
    hata "Prova, uygulanacak olandan başka bir kümeyi sınamış olur."
    comm -23 "$onces" "$sonras" | sed 's/^/           provada vardı, şimdi yok: /' >&2
    comm -13 "$onces" "$sonras" | sed 's/^/           provada yoktu, şimdi var: /' >&2
    rm -f "$onces" "$sonras"; exit 2
fi
log "Küme değişmedi — prova, uygulanacak göçlerin ta kendisini sınadı."

# ── 3) YEDEK ── geri dönüşü olmayan işten önce.
log "════ YEDEK ════"
if [ ! -x "$YEDEK_BETIGI" ]; then
    hata "Yedek betiği bulunamadı ($YEDEK_BETIGI). Yedeksiz göç uygulanmaz."
    rm -f "$onces" "$sonras"; exit 1
fi
if ! sudo "$YEDEK_BETIGI"; then
    hata "Yedek ALINAMADI. Yedeksiz göç uygulanmaz."
    rm -f "$onces" "$sonras"; exit 1
fi

# ── 4) UYGULAMA ──
EF_ARACI="${DOTNET_EF:-/root/.dotnet/tools/dotnet-ef}"
[ ! -x "$EF_ARACI" ] && { hata "KARAR VEREMEDİ: dotnet-ef bulunamadı."; rm -f "$onces" "$sonras"; exit 2; }

log "════ CANLIYA UYGULANIYOR ════"
for baglam in AppDbContext HrDbContext; do
    log "bağlam: $baglam"
    if ! DB_CONNECTION="$canli" \
            ConnectionStrings__DefaultConnection="$canli" \
            "$EF_ARACI" database update \
            --project "${REPO_ROOT}/backend/EnderunAI.Api" \
            --context "$baglam"; then
        hata "GÖÇ UYGULANIRKEN DÜŞTÜ ($baglam)."
        hata "Yedek yukarıda alındı; gerekirse ondan dönülür."
        rm -f "$onces" "$sonras"; exit 1
    fi
done

# ── 5) SONRASI KANITI ── "uyguladım" demek yetmez, ölçülür.
kalan="$(mktemp)"
if ! bekleyen_kume "$kalan"; then
    hata "KARAR VEREMEDİ: uygulama sonrası ölçüm yapılamadı."
    rm -f "$onces" "$sonras" "$kalan"; exit 2
fi
if [ -s "$kalan" ]; then
    hata "UYGULAMA EKSİK: hâlâ bekleyen göç var."
    sed 's/^/           /' "$kalan" >&2
    rm -f "$onces" "$sonras" "$kalan"; exit 1
fi
log "UYGULAMA KANITI: bekleyen göç kalmadı."
log "Göç uygulandı. Artık safe-deploy koşulabilir."
rm -f "$onces" "$sonras" "$kalan"
exit 0
