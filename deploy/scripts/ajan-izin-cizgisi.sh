#!/usr/bin/env bash
#
# AJAN/1 İZİN ÇIRASI — ÖLÇÜM KİMLİĞİNİN ERİŞİMİ BÜYÜYEMEZ.
#
# ═══ NEDEN VAR ═══
#
# `AJAN/1` canlı durumu ölçmek için açılan SALT OKUYAN bir kimliktir
# (bkz. `docs/AJAN-1-TEKLIF.md`). Mehmet Bey'in kararı: canlıda yazma
# YOK — hiçbir kapsamda, "asgari izinle" bile değil.
#
# Bir kimliğin izin kümesi sessizce büyür: "şunu da göremiyorum" der,
# bir izin eklenir, kimse saymaz. Bu betik sayar.
#
# ═══ ÇİFT YÖNLÜ (şema sapma circirinin aynı deseni) ═══
#
#   · Sayı çizgiyi AŞARSA → biri ajana izin eklemiş, KIRMIZI.
#   · Sayı çizgiden AZSA  → çizgi düşürülmeli. Gevşeklik bırakmak,
#                           ajanın neye eriştiğini görünmez kılar.
#
# ═══ SAYIDAN ÖNCE GELEN İKİ KOŞULSUZ KIRMIZI ═══
#
#   · herhangi bir YAZMA izni (create/edit/delete/manage/approve…)
#   · rol üzerinden gelen izinler — ajan role bağlanmamalı, izinleri
#     doğrudan atanmalı ki sayılabilsin ve rol değişince sessizce
#     genişlemesin
#
# ═══ "KULLANICI YOK" İLE "İZİN YOK" AYNI DEĞİL ═══
#
# Kimlik henüz açılmadıysa çıkış 3 (ÖLÇEMEDİ). Sıfır izin raporlamak,
# kimliğin güvenli olduğunu değil ölçümün boş olduğunu gösterirdi.
#
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CIZGI_DOSYASI="${REPO_ROOT}/deploy/bekci/ajan-izin-cizgisi.txt"
AJAN_KULLANICI="${AJAN_KULLANICI:-ajan-olcum}"
# Hedef veritabanı ayarlanabilir — YALNIZ bu kapının kendi sondası için.
# Varsayılan canlıdır; sonda prova zemininde koşup dalların ısırdığını
# gösterebilsin diye dışarıdan verilebiliyor.
AJAN_VT="${AJAN_VT:-enderun_ai}"

hata() { echo "[ajan-izin] $*" >&2; }
sorgu() { "${REPO_ROOT}/deploy/scripts/vt-sorgu.sh" --vt "$AJAN_VT" --sql "$1" 2>/dev/null | grep -vE '^\[vt-sorgu\]'; }

[ -f "$CIZGI_DOSYASI" ] || { hata "ÖLÇEMEDİ: çizgi dosyası yok: $CIZGI_DOSYASI"; exit 3; }
CIZGI="$(tr -dc '0-9' < "$CIZGI_DOSYASI")"
[ -n "$CIZGI" ] || { hata "ÖLÇEMEDİ: çizgi okunamadı."; exit 3; }

KIMLIK="$(sorgu "SELECT \"Id\" FROM users WHERE \"Username\" = '${AJAN_KULLANICI}'" | tr -d ' ' | head -1)"

if [ -z "$KIMLIK" ]; then
    hata "ÖLÇEMEDİ: '${AJAN_KULLANICI}' kullanıcısı canlıda YOK."
    hata "ÖLÇEMEDİ: kimlik henüz kurulmadıysa bu beklenen durumdur; 'izin yok' DEĞİLDİR."
    exit 3
fi

# ── Rol üzerinden gelen izinler: OLMAMALI ────────────────────────────
ROL_IZNI="$(sorgu "
  SELECT count(*) FROM user_roles ur
    JOIN role_permissions rp ON rp.\"RoleId\" = ur.\"RoleId\"
   WHERE ur.\"UserId\" = '${KIMLIK}'" | tr -d ' ' | head -1)"

# ── Doğrudan atanan izinler (Effect = izin ver) ──────────────────────
IZINLER="$(sorgu "
  SELECT p.\"Key\" FROM user_permission_overrides o
    JOIN permissions p ON p.\"Id\" = o.\"PermissionId\"
   WHERE o.\"UserId\" = '${KIMLIK}' AND o.\"Effect\" = 1
   ORDER BY p.\"Key\"" | tr -d ' ' | grep -v '^$')"

SAYI=$(printf '%s\n' "$IZINLER" | grep -c . || true)

# TARAMA SAĞLIĞI: kullanıcı var ama hiç izin okunamadıysa, sorgunun
# çalıştığından emin olmadan "0 izin, güvenli" denemez.
TOPLAM_IZIN="$(sorgu "SELECT count(*) FROM permissions" | tr -d ' ' | head -1)"
if [ "${TOPLAM_IZIN:-0}" -lt 50 ]; then
    hata "ÖLÇEMEDİ: izin kataloğundan yalnız ${TOPLAM_IZIN:-0} satır okundu (50+ bekleniyor)."
    exit 3
fi

echo "[ajan-izin] kullanıcı=${AJAN_KULLANICI} · doğrudan izin=${SAYI} (çizgi ${CIZGI}) · rol üzerinden=${ROL_IZNI} · katalog=${TOPLAM_IZIN}"
printf '%s\n' "$IZINLER" | sed 's/^/[ajan-izin]   /'

# ── KOŞULSUZ KIRMIZI 1: yazma izni ───────────────────────────────────
YAZMA="$(printf '%s\n' "$IZINLER" | grep -E '\.(create|edit|delete|manage|approve|post|import|export)$' || true)"
if [ -n "$YAZMA" ]; then
    hata "YAZMA İZNİ BULUNDU — ajan salt okuyan bir kimliktir:"
    printf '%s\n' "$YAZMA" | sed 's/^/[ajan-izin]   /' >&2
    exit 1
fi

# ── KOŞULSUZ KIRMIZI 2: rol bağı ─────────────────────────────────────
if [ "${ROL_IZNI:-0}" -gt 0 ]; then
    hata "AJAN BİR ROLE BAĞLI (${ROL_IZNI} izin rolden geliyor)."
    hata "Rol değişince ajanın erişimi SESSİZCE genişler. İzinler doğrudan atanmalı."
    exit 1
fi

# ── ÇİFT YÖNLÜ ÇİZGİ ─────────────────────────────────────────────────
if [ "$SAYI" -gt "$CIZGI" ]; then
    hata "ÇİZGİ AŞILDI: ${SAYI} > ${CIZGI}. Ajana izin eklenmiş."
    exit 1
fi

if [ "$SAYI" -lt "$CIZGI" ]; then
    hata "ÇİZGİ GEVŞEK: gerçek ${SAYI}, çizgi ${CIZGI} — gevşeklik $((CIZGI - SAYI))."
    hata "Çizgiyi ${SAYI} yapın: ${CIZGI_DOSYASI}"
    exit 1
fi

echo "[ajan-izin] çizgi tam: ${SAYI}/${CIZGI} · yazma izni yok · rol bağı yok"
exit 0
