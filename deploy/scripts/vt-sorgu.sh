#!/usr/bin/env bash
#
# ═══════════════════════════════════════════════════════════════════
# VT-SORGU — ÖLÇÜM İÇİN TEK VERİTABANI YOLU (Y3)
# ═══════════════════════════════════════════════════════════════════
#
# ═══ NEDEN VAR ═══
#
# Yanlış veritabanını ölçmek İKİ KEZ oldu:
#
#   1. S6 (2026-09-07): `audit_logs` tablosu ölçüldü, 0 satır çıktı,
#      "denetim izi yok" sanıldı. Uygulama o tabloya hiç yazmıyor;
#      doğru tablo `security_audit_events` ve 1.972 satır var.
#
#   2. E2 (2026-09-08): `psql -d "$DB"` çağrıldı ama $DB boştu.
#      psql sessizce VARSAYILAN veritabanına ("postgres") bağlandı.
#      241 tablo yerine boş bir şemada arama yapıldı; sonuç "0/100
#      eşleşme" çıktı ve bir an gerçek sanıldı.
#
# İkisini de yakaladım ama iki kez olan üçüncü kez de olur ve o
# sefer yakalanmayabilir. Y2'de `pkill -f` için yapılanın aynısı:
# "dikkat edeceğim" demek yerine ARACI DEĞİŞTİRMEK.
#
# ═══ NE YAPIYOR ═══
#
#   · Veritabanı adı AÇIKÇA verilmezse ÇALIŞMAZ.
#   · `postgres`, `template0`, `template1` bağlanmayı REDDEDER —
#     bunlar bakım veritabanları, ölçüm oraya yapılmaz.
#   · Her çıktının başına psql'in KENDİ bildirdiği
#     `current_database()` değerini ve satır sayısını basar.
#     İstenen ad ile bağlanılan ad karşılaştırılır; tutmazsa DURUR.
#
# ═══ KULLANIM ═══
#
#   vt-sorgu.sh --vt enderun_ai --sql 'SELECT count(*) FROM projects;'
#   vt-sorgu.sh --vt enderun_ai --dosya /tmp/sorgu.sql
#
set -euo pipefail

YASAK_VERITABANLARI=("postgres" "template0" "template1")

kullanim() {
    cat >&2 <<'K'
KULLANIM: vt-sorgu.sh --vt <veritabani_adi> (--sql "<sorgu>" | --dosya <yol>)

  --vt     ZORUNLU. Ölçülecek veritabanının adı. Tahmin edilmez,
           varsayılana düşülmez.
  --sql    Çalıştırılacak SQL.
  --dosya  SQL dosyası.

Bakım veritabanlarına (postgres, template0, template1) bağlanılmaz.
K
    exit 2
}

VT=""
SQL=""
DOSYA=""

while [ $# -gt 0 ]; do
    case "$1" in
        --vt)    VT="${2:-}";    shift 2 || kullanim ;;
        --sql)   SQL="${2:-}";   shift 2 || kullanim ;;
        --dosya) DOSYA="${2:-}"; shift 2 || kullanim ;;
        *) echo "[vt-sorgu] Bilinmeyen seçenek: $1" >&2; kullanim ;;
    esac
done

# ── KAPI 1: AD VERİLMEDİYSE ÇALIŞMA ────────────────────────────────
if [ -z "$VT" ]; then
    echo "[vt-sorgu] REDDEDİLDİ: --vt verilmedi." >&2
    echo "[vt-sorgu] Veritabanı adı tahmin edilmez. Bu araç tam olarak" >&2
    echo "[vt-sorgu] bu yüzden var: adsız çağrıda psql sessizce" >&2
    echo "[vt-sorgu] 'postgres' veritabanına bağlanır ve ölçüm yalan olur." >&2
    kullanim
fi

# ── KAPI 2: BAKIM VERİTABANLARI ────────────────────────────────────
for yasak in "${YASAK_VERITABANLARI[@]}"; do
    if [ "$VT" = "$yasak" ]; then
        echo "[vt-sorgu] REDDEDİLDİ: '${VT}' bir bakım veritabanı." >&2
        echo "[vt-sorgu] Ölçüm uygulama veritabanında yapılır." >&2
        exit 2
    fi
done

if [ -z "$SQL" ] && [ -z "$DOSYA" ]; then
    echo "[vt-sorgu] REDDEDİLDİ: --sql veya --dosya gerekli." >&2
    kullanim
fi

if [ -n "$DOSYA" ] && [ ! -f "$DOSYA" ]; then
    echo "[vt-sorgu] SQL dosyası yok: $DOSYA" >&2
    exit 2
fi

calistir() {
    sudo -u postgres psql -d "$VT" -v ON_ERROR_STOP=1 "$@"
}

# ── KAPI 3: BAĞLANILAN AD, İSTENEN AD MI ───────────────────────────
#
# Kural 65'in doğrudan panzehiri: "ölçtüğümü sandığım yer" ile
# "gerçekten ölçtüğüm yer" burada YÜZLEŞTİRİLİYOR. psql'in kendi
# ağzından alınıyor, bizim değişkenimizden değil.
BAGLANILAN="$(calistir -Atc 'SELECT current_database();' 2>/dev/null || true)"

if [ -z "$BAGLANILAN" ]; then
    echo "[vt-sorgu] BAĞLANILAMADI: '${VT}' (yok ya da erişilemiyor)." >&2
    exit 2
fi

if [ "$BAGLANILAN" != "$VT" ]; then
    echo "[vt-sorgu] DURDU: istenen '${VT}', bağlanılan '${BAGLANILAN}'." >&2
    exit 2
fi

CIKTI="$(mktemp)"
trap 'rm -f "$CIKTI"' EXIT

if [ -n "$DOSYA" ]; then
    calistir -A -F'|' -t -f "$DOSYA" > "$CIKTI"
else
    calistir -A -F'|' -t -c "$SQL" > "$CIKTI"
fi

SATIR="$(wc -l < "$CIKTI")"
TABLO="$(calistir -Atc "SELECT count(*) FROM information_schema.tables WHERE table_schema='public';")"

# ── HER ÇIKTININ BAŞLIĞI ───────────────────────────────────────────
echo "[vt-sorgu] veritabanı = ${BAGLANILAN}  (current_database(), psql'in kendi beyanı)"
echo "[vt-sorgu] public şemasında ${TABLO} tablo  ·  sonuç ${SATIR} satır"
echo "[vt-sorgu] ────────────────────────────────────────────────"
cat "$CIKTI"
