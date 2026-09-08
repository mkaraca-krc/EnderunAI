#!/usr/bin/env bash
#
# ═══════════════════════════════════════════════════════════════════
# PROVA ZEMİNİ — ŞEMA SADIK MI, VERİ SADIK MI (PZ1-PZ3)
# ═══════════════════════════════════════════════════════════════════
#
# ═══ NEDEN VAR: BUGÜNKÜ FK HATASI ═══
#
# `enderun_squash_olcum` üzerinde iki ayrı iddia kanıtlanmaya çalışıldı
# (2026-09-08):
#
#   SQUASH/1 geri alma  → `__EFMigrationsHistory` satırları. Zemin
#     ŞEMA olarak sadıktı ve veriyi provanın kendisi doldurdu.
#     KANIT GEÇERLİ: parmak izi 8709ef5e -> değişti -> 8709ef5e.
#
#   KATALOG/1 geri alma → `role_permissions` satırları. Bu iddia
#     GERÇEK KİMLİKLER gerektiriyordu (RoleId, PermissionId).
#     Zeminde o izin satırı YOKTU ve komut yabancı anahtar ihlaliyle
#     düştü. KANIT ÜRETİLEMEDİ.
#
# ═══ AYRIM ═══
#
#   ŞEMA SADIK  : taze veritabanına göç uygulanarak kurulur. Yapı
#                 canlıyla birebir, VERİ BOŞ. "Göç uygulanabiliyor mu",
#                 "şema ayrışmış mı" gibi iddialar burada kanıtlanır.
#                 GERÇEK KİMLİK GEREKTİREN HİÇBİR İDDİA BURADA
#                 KANITLANAMAZ — kimlikler orada yoktur.
#
#   VERİ SADIK  : canlıdan kopyalanarak kurulur. Gerçek satırlar,
#                 gerçek kimlikler, gerçek yabancı anahtarlar. Satır
#                 geri alma, benzersizlik çakışması, dönüşüm hatası
#                 gibi iddialar ancak burada kanıtlanır.
#
# ═══ KURAL 81'İN VERİTABANI HÂLİ ═══
#
# "Rig üretimi imite etmeli" yalnız ağ, port ve dizin için değil —
# VERİ için de geçerli. Şema sadık bir zemin, veri gerektiren bir
# iddiada rig'in üretime benzemediği durumdur; farkı yalnız hata
# mesajı söyler, o da şanslıysanız.
#
# ═══ KULLANIM ═══
#
#   prova-zemini.sh --sema <ad>              # yapı sadık, veri BOŞ
#   prova-zemini.sh --veri <ad> [tablo...]   # canlıdan kopya
#
# Tablo verilmezse tüm veritabanı kopyalanır. Tablo verilirse yalnız
# onlar — iki satırlık bir iddia için tüm veritabanını kopyalamak
# orantısızdır.
#
set -euo pipefail

CANLI_DB="enderun_ai"
KIP=""
AD=""

log() { echo "[prova-zemini] $*"; }
hata() { echo "[prova-zemini] HATA: $*" >&2; exit 2; }

case "${1:-}" in
    --sema|--veri) KIP="${1#--}"; AD="${2:-}"; shift 2 || true ;;
    *) hata "KULLANIM: prova-zemini.sh (--sema|--veri) <ad> [tablo...]" ;;
esac

[ -n "$AD" ] || hata "Zemin adı verilmedi. Ad tahmin edilmez (Y3)."

case "$AD" in
    "$CANLI_DB"|postgres|template0|template1)
        hata "'${AD}' korunan bir veritabanı; prova zemini olarak kullanılamaz." ;;
    prova_*|enderun_*prova*|enderun_*olcum*) : ;;
    *) hata "Zemin adı 'prova' ya da 'olcum' içermeli — canlıyla karıştırılmasın." ;;
esac

sudo -u postgres psql -q -c "DROP DATABASE IF EXISTS ${AD};"
sudo -u postgres psql -q -c "CREATE DATABASE ${AD};"
# PUBLIC'E KAPALI DOĞSUN: açık-veritabanı kapısı yeni bir kaçak görmesin.
sudo -u postgres psql -q -c "REVOKE CONNECT ON DATABASE ${AD} FROM PUBLIC;"

if [ "$KIP" = "veri" ]; then
    if [ "$#" -gt 0 ]; then
        for t in "$@"; do
            sudo -u postgres pg_dump -d "$CANLI_DB" -t "public.${t}" \
                --no-owner --no-privileges | sudo -u postgres psql -q -d "$AD"
        done
        log "VERİ SADIK zemin hazır: ${AD} (tablolar: $*)"
    else
        sudo -u postgres pg_dump -d "$CANLI_DB" --no-owner --no-privileges \
            | sudo -u postgres psql -q -d "$AD"
        log "VERİ SADIK zemin hazır: ${AD} (tüm veritabanı)"
    fi
    log "Gerçek kimlikler burada. Satır geri alma, FK ve benzersizlik"
    log "iddiaları BURADA kanıtlanır."
else
    log "ŞEMA SADIK zemin hazır: ${AD} (boş — göçü siz uygulayın)"
    log ""
    log "⚠ VERİ SADIK DEĞİL. Gerçek kimlik gerektiren bir iddiayı"
    log "⚠ BURADA KANITLAYAMAZSINIZ. 2026-09-08'de denendi:"
    log "⚠ role_permissions geri alma komutu yabancı anahtar ihlaliyle"
    log "⚠ düştü, çünkü o PermissionId zeminde yoktu. Kanıt üretilemedi."
    log "⚠ Veri gerektiren prova için: prova-zemini.sh --veri <ad> [tablo...]"
fi
