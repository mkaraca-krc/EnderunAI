#!/usr/bin/env bash
#
# SEED/1 SB4 SONDASI — KALDIRMA YENİDEN BAŞLATMADAN SAĞ ÇIKIYOR MU
#
# CANLI/1 K1'in yeni şartı tam olarak budur:
#   izni kaldır -> servisi yeniden başlat -> hâlâ kaldırılmış mı
#
# CANLIYA DOKUNMUYOR: enderun_ai_test üzerinde, ayrı portta kendi
# arka ucunu açıp kapatarak ölçüyor. Hedef veritabanı adı beyaz
# listeyle doğrulanıyor; adı tutmazsa hiç başlamıyor.
set -uo pipefail

KOK="/var/www/enderun-ai"
PORT=5158
ROL="${1:-Şantiye Şefi}"
IZIN="${2:-dashboard.view}"

log() { echo "[seed-sonda] $*"; }
oldu() { echo "[seed-sonda] HATA: $*" >&2; exit 1; }

CANLI="$(grep -E '^DB_CONNECTION=' /etc/enderunai/backend.env | sed -E "s/^DB_CONNECTION=//" | tr -d "'\"")"
TEST="${CANLI//Database=enderun_ai;/Database=enderun_ai_test;}"
case "$TEST" in
  *"Database=enderun_ai_test;"*) : ;;
  *) oldu "Hedef enderun_ai_test DEĞİL. Durduruldu." ;;
esac

PID=""
temizle() { [ -n "$PID" ] && kill -TERM -- "-${PID}" 2>/dev/null; sleep 1; [ -n "$PID" ] && kill -KILL -- "-${PID}" 2>/dev/null; true; }
trap temizle EXIT

# SONDANIN KENDİ DİSK KÖKÜ (SIZINTI/1).
#
# Bu sonda uygulamayı `enderun_ai_test` ile ayağa kaldırıyor ama
# yazma kökleri sabit kodlu olduğu için dosyalarını CANLI dizinlere
# bırakıyordu. Kökler artık dışarıdan veriliyor ve fail-closed kapı
# (YazmaKokleri) canlı kökle başlamayı reddediyor — verilmezse bu
# betik hiç açılmaz. Kapıyı gevşetmek yerine sondaya kendi kökü
# verildi.
SONDA_DISK="$(mktemp -d /tmp/seed-sonda-disk-XXXXXX)"
trap 'rm -rf "$SONDA_DISK"' EXIT

ac() {
  DB_CONNECTION="$TEST" JWT_SECRET="seed-sonda-$(head -c 9 /dev/urandom | base64 | tr -d '/+=')" \
  Uploads__Root="${SONDA_DISK}/uploads" \
  EInvoice__ArchivePath="${SONDA_DISK}/e-fatura" \
  Storage__ProjectFilesRoot="${SONDA_DISK}/project-files" \
  ASPNETCORE_URLS="http://127.0.0.1:${PORT}" \
    setsid dotnet "${SONDA_PUBLISH:-${KOK}/publish}/EnderunAI.Api.dll" > /tmp/seed-sonda-arka.log 2>&1 &
  PID=$!
  for i in $(seq 1 150); do
    curl -sf -m 2 "http://127.0.0.1:${PORT}/api/health" >/dev/null 2>&1 && return 0
    sleep 2
  done
  tail -15 /tmp/seed-sonda-arka.log >&2
  oldu "Arka uç ${PORT} açılmadı."
}

kapat() { temizle; PID=""; }

var_mi() {
  sudo -u postgres psql -d enderun_ai_test -t -A -c \
    "SELECT count(*) FROM role_permissions rp
     JOIN roles r ON r.\"Id\"=rp.\"RoleId\"
     JOIN permissions p ON p.\"Id\"=rp.\"PermissionId\"
     WHERE r.\"Name\"='${ROL}' AND p.\"Key\"='${IZIN}';"
}

log "Hedef: ${ROL} + ${IZIN}"

# 1) Once acilis: tohumlayici cifti kursun
log "1) Arka uc aciliyor (tohumlama)..."
ac; kapat
log "   baslangic: $(var_mi)"

# 2) Kaldirma: matris toggle'inin yaptiginin AYNISI
#    (grant sil + kaldirma kaydi yaz)
log "2) Izin kaldiriliyor (toggle davranisi taklit ediliyor)..."
sudo -u postgres psql -q -v ON_ERROR_STOP=1 -d enderun_ai_test <<SQL
DO \$\$
DECLARE v_rol uuid; v_izin uuid;
BEGIN
  SELECT "Id" INTO v_rol FROM roles WHERE "Name"='${ROL}';
  SELECT "Id" INTO v_izin FROM permissions WHERE "Key"='${IZIN}';
  IF v_rol IS NULL OR v_izin IS NULL THEN RAISE EXCEPTION 'rol ya da izin yok'; END IF;

  DELETE FROM role_permissions WHERE "RoleId"=v_rol AND "PermissionId"=v_izin;

  IF '${KALDIRMA_KAYDI:-evet}' = 'evet' THEN
    DELETE FROM role_permission_revocations WHERE "RoleId"=v_rol AND "PermissionId"=v_izin;
    INSERT INTO role_permission_revocations ("Id","RoleId","PermissionId","RevokedAtUtc")
    VALUES (gen_random_uuid(), v_rol, v_izin, now());
  END IF;
END \$\$;
SQL
log "   kaldirma sonrasi: $(var_mi)"

# 3) YENIDEN BASLATMA — sondanin kalbi
log "3) Arka uc YENIDEN baslatiliyor..."
ac; kapat

SONUC="$(var_mi)"
log "   yeniden baslatma sonrasi: ${SONUC}"

if [ "$SONUC" = "0" ]; then
  log "SONUC: KALDIRMA SAG CIKTI (yesil)"
  exit 0
else
  log "SONUC: IZIN GERI GELDI (kirmizi)"
  exit 1
fi
