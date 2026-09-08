#!/usr/bin/env bash
#
# ŞEMA KARŞILAŞTIRMA — SQUASH/1'İN KABUL ÖLÇÜTÜ
#
# ═══ NEDEN VAR ═══
#
# SQUASH/1'in tek gerçek sorusu şu: göçleri sıkıştırdıktan sonra
# ÜRETİLEN ŞEMA aynı mı? "Göç sayısı azaldı" bunu cevaplamaz;
# yanlış bir sıkıştırma da göç sayısını azaltır.
#
# Bu betik iki veritabanının şemasını nesne nesne karşılaştırır:
# tablo + sütun + veri tipi + null'lanabilirlik + indeks adı.
#
# ═══ NEDEN VERİ DEĞİL ŞEMA ═══
#
# Sıkıştırma veriye dokunmaz; dokunursa zaten yedek + geri yükleme
# tatbikatı onu yakalar (ayrı kapı, ayrı ölçüm).
#
# ═══ İKİ BAĞLAM ═══
#
# ÖLÇÜLDÜ: yalnız `AppDbContext` uygulanınca 31 nesnelik fark çıkıyor
# ve hepsi `hr_*` — çünkü ikinci bir bağlam var (`HrDbContext`).
# İkisi de uygulandığında fark SIFIR. Karşılaştırma yapan herkes bunu
# bilmeli, yoksa var olmayan bir sapma rapor eder.
#
# KULLANIM: sema-karsilastir.sh <db1> <db2>
set -uo pipefail

BIR="${1:?birinci veritabani}"
IKI="${2:?ikinci veritabani}"

sema() {
    sudo -u postgres psql -d "$1" -t -A -F'|' -c "
        SELECT 'TABLO|' || table_name || '|' || column_name || '|'
               || data_type || '|' || is_nullable
        FROM information_schema.columns WHERE table_schema='public'
        UNION ALL
        SELECT 'INDEKS|' || indexname || '|||'
        FROM pg_indexes WHERE schemaname='public'
        ORDER BY 1;" 2>/dev/null | sort
}

D1="$(mktemp)"; D2="$(mktemp)"
trap 'rm -f "$D1" "$D2"' EXIT

sema "$BIR" > "$D1"
sema "$IKI" > "$D2"

N1="$(wc -l < "$D1")"; N2="$(wc -l < "$D2")"

# BOŞ ŞEMA SESSİZCE GEÇMEZ: iki boş veritabanı da "aynı" çıkardı ve
# hiçbir şey kanıtlamazdı (Kural 48).
if [ "$N1" -lt 100 ] || [ "$N2" -lt 100 ]; then
    echo "[sema-karsilastir] ÖLÇEMEDİ: şema beklenenden küçük (${BIR}=${N1}, ${IKI}=${N2})."
    exit 2
fi

SADECE1="$(comm -23 "$D1" "$D2" | wc -l)"
SADECE2="$(comm -13 "$D1" "$D2" | wc -l)"

echo "[sema-karsilastir] ${BIR}: ${N1} nesne · ${IKI}: ${N2} nesne"
echo "[sema-karsilastir] yalnız ${BIR}: ${SADECE1} · yalnız ${IKI}: ${SADECE2}"

if [ "$SADECE1" -eq 0 ] && [ "$SADECE2" -eq 0 ]; then
    echo "[sema-karsilastir] ŞEMALAR AYNI ✓"
    exit 0
fi

echo "[sema-karsilastir] ŞEMALAR FARKLI — ilk 20 fark:"
{ comm -23 "$D1" "$D2" | sed "s/^/  yalnız ${BIR}: /"
  comm -13 "$D1" "$D2" | sed "s/^/  yalnız ${IKI}: /"; } | head -20
exit 1
