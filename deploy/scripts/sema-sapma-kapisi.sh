#!/usr/bin/env bash
#
# ŞEMA SAPMA CIRCIRI — MODELİN BİLMEDİĞİ NESNE ARTAMAZ.
#
# ═══ NEDEN VAR (2026-09-07) ═══
#
# Canlı veritabanında var olup EF modelinin HABERİ OLMAYAN indeksler
# var. Bunlar yalnız göçlerin ham SQL bloklarında yaşıyor; model ve
# anlık görüntü onlardan habersiz.
#
# BEDELİ ÖLÇÜLDÜ: modelden üretilecek bir temel göç (SQUASH/1) bu
# korumaları SESSİZCE DÜŞÜRÜR. Aralarında çek mükerrerlik koruması
# gibi gerçek savunmalar var.
#
# CIRCIR SQUASH'I BEKLEMİYOR — ONDAN BAĞIMSIZ BİR RİSK. Her yeni ham
# SQL indeksi açığı büyütüyor ve bunu bugüne kadar hiçbir şey
# ölçmüyordu. Çizgi kurulunca squash "acele edilmesi gereken" bir iş
# olmaktan çıkıyor: açık büyümüyor demektir.
#
# ═══ ÇİFT YÖNLÜ ═══
#
#   · Sapma ÇİZGİYİ AŞAMAZ  → yeni bir savunma yine yalnız ham SQL'de
#                             doğduysa kırmızı.
#   · Sapma çizgiden AZSA   → çizgi düşürülmeli (SEMA-KAYNAK/1
#                             ilerledikçe kendiliğinden). Gevşeklik
#                             bırakmak, ilerlemeyi görünmez kılar.
#
# ═══ İKİNCİ YÖN AYRICA RAPORLANIYOR ═══
#
# "Modelde var ama canlıda YOK" — göç geçmişinin ürettiği şema ile
# canlının ayrıştığı yer. Bugün 14 ve çoğu 0 satırlı tablolarda;
# çizgiye BAĞLANMADI çünkü ayrı bir karar (Mehmet'e rapor edildi).
#
# KULLANIM:  sema-sapma-kapisi.sh [--liste]

set -uo pipefail

REPO_ROOT="${REPO_ROOT:-/var/www/enderun-ai}"
CIZGI_DOSYASI="${REPO_ROOT}/deploy/bekci/sema-sapma-cizgi.txt"
PROJE="${REPO_ROOT}/backend/EnderunAI.Api"
GECICI="$(mktemp -d)"
trap 'rm -rf "$GECICI"' EXIT

LISTE=0
[ "${1:-}" = "--liste" ] && LISTE=1

hata() { echo "[sema-sapma] $*" >&2; }

# ── ORTAM ──
[ -n "${DB_CONNECTION:-}" ] || DB_CONNECTION="$(sudo grep -E '^DB_CONNECTION=' /etc/enderunai/backend.env 2>/dev/null | sed -E "s/^DB_CONNECTION=//" | tr -d "'\"")"
[ -n "$DB_CONNECTION" ] || { hata "HATA: DB_CONNECTION okunamadı."; exit 1; }
export DB_CONNECTION
export PATH="$PATH:/root/.dotnet/tools"

# ── MODELDEN ŞEMA ──
#
# `--no-build` deneniyor; ikili yoksa derleme yapılıyor. Derleme
# `derleme-kos.sh` üzerinden geçiyor: bellek tavanı ve tek örnek
# kapısı orada.
if ! (cd "$PROJE" && dotnet ef dbcontext script --context AppDbContext --no-build -o "$GECICI/model.sql" >/dev/null 2>&1); then
    "${REPO_ROOT}/scripts/derleme-kos.sh" dotnet build "$PROJE/EnderunAI.Api.csproj" -v q --nologo >/dev/null 2>&1
    (cd "$PROJE" && dotnet ef dbcontext script --context AppDbContext --no-build -o "$GECICI/model.sql" >/dev/null 2>&1) \
        || { hata "HATA: modelden şema üretilemedi."; exit 1; }
fi

# ── AD AYIKLAMA ──
#
# TIRNAKLI AD ARANIYOR, BOŞLUKLA SINIRLI DEĞİL. İlk yazımda desen
# `"?[A-Za-z0-9_]+"?` idi ve uzun adları KIRPTI; sonuç 116/35 çıktı,
# doğrusu 95/14. Yanlış ayıklama, yanlış sayı üretir (Kural 81'in
# ölçüme uygulanması).
grep -oE 'CREATE (UNIQUE )?INDEX "[^"]+"' "$GECICI/model.sql" \
  | sed -E 's/.*INDEX "([^"]+)"/\1/' | sort -u > "$GECICI/model.txt"

# CANLI: birincil anahtar indeksleri hariç (onlar kısıttan doğuyor).
sudo -u postgres psql -d enderun_ai -tAc "
    select i.indexname from pg_indexes i
     where i.schemaname='public'
       and not exists (select 1 from pg_constraint c
                        where c.conname = i.indexname and c.contype='p')
     order by 1;" 2>/dev/null | sed 's/^ *//;s/ *$//' | grep -v '^$' | sort -u > "$GECICI/canli.txt"

MODEL_SAYI=$(wc -l < "$GECICI/model.txt")
CANLI_SAYI=$(wc -l < "$GECICI/canli.txt")

# ── TARAMA SAĞLIĞI — SIFIR KARŞILAŞTIRMA YEŞİL VERMEZ ──
#
# "Sapma yok" ile "hiçbir şey karşılaştırılmadı" aynı çıktıyı verir;
# ayıran tek şey sayımdır (Kural 48).
if [ "$MODEL_SAYI" -lt 100 ] || [ "$CANLI_SAYI" -lt 100 ]; then
    hata "TARAMA SAĞLIĞI DÜŞTÜ: model=${MODEL_SAYI} canlı=${CANLI_SAYI} indeks."
    hata "Bu sayılar beklenen mertebede değil; karşılaştırma anlamsız."
    exit 1
fi

comm -13 "$GECICI/model.txt" "$GECICI/canli.txt" > "$GECICI/sapma.txt"
comm -23 "$GECICI/model.txt" "$GECICI/canli.txt" > "$GECICI/eksik.txt"

SAPMA=$(wc -l < "$GECICI/sapma.txt")
EKSIK=$(wc -l < "$GECICI/eksik.txt")

CIZGI=$(grep -vE '^\s*#|^\s*$' "$CIZGI_DOSYASI" 2>/dev/null | head -1 | tr -dc '0-9')
[ -n "$CIZGI" ] || { hata "HATA: çizgi okunamadı: $CIZGI_DOSYASI"; exit 1; }

echo "[sema-sapma] model ${MODEL_SAYI} · canlı ${CANLI_SAYI} · modelin bilmediği ${SAPMA} (çizgi ${CIZGI}) · modelde var canlıda yok ${EKSIK}"

if [ "$LISTE" = "1" ]; then
    echo "--- MODELİN BİLMEDİĞİ ---"; cat "$GECICI/sapma.txt"
    echo "--- MODELDE VAR, CANLIDA YOK ---"; cat "$GECICI/eksik.txt"
fi

if [ "$SAPMA" -gt "$CIZGI" ]; then
    hata "ÇİZGİ AŞILDI: ${SAPMA} > ${CIZGI}."
    hata "Yeni bir şema nesnesi yine YALNIZ HAM SQL'de doğmuş demektir;"
    hata "model ondan habersiz ve temel göç onu sessizce düşürürdü."
    hata "Yeni nesneler:"
    comm -13 "$GECICI/model.txt" "$GECICI/canli.txt" | tail -5 | sed 's/^/  - /' >&2
    hata "Çözüm: nesneyi modele taşıyın (HasIndex / HasFilter)."
    exit 1
fi

if [ "$SAPMA" -lt "$CIZGI" ]; then
    hata "ÇİZGİ GEVŞEK: gerçek ${SAPMA}, çizgi ${CIZGI} — gevşeklik $((CIZGI - SAPMA))."
    hata "Çizgi bir TABANDIR. Gevşeklik bırakmak, kazanılan ilerlemeyi"
    hata "görünmez kılar ve geri kaymaya sessizce izin verir."
    hata "Çizgiyi ${SAPMA} yapın: $CIZGI_DOSYASI"
    exit 1
fi
