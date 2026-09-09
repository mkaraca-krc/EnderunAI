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
# ═══ İKİNCİ ÇİZGİ: "MODELDE VAR AMA CANLIDA YOK" ═══
#
# Göç geçmişinin ürettiği şema ile canlının ayrıştığı yer. Bugün 14.
#
# MEKANİZMA ÖLÇÜLDÜ (2026-09-07): 14'ün 12'si, modelin YABANCI ANAHTAR
# tanımladığı ama canlıda HİÇ yabancı anahtarı olmayan tablolarda
# (sekreterya, evrak, kargo, İK — hepsi 2026-07-2x). Tabloyu kuran göç
# yalnız birincil anahtarı yazmış; modeldeki `HasOne(...).HasForeignKey`
# ilişkileri hiçbir göçte karşılık bulmamış.
#
# ASIL SEBEP DAHA GENEL: EF yalnız MODEL ↔ ANLIK GÖRÜNTÜ karşılaştırır,
# hiçbir zaman ANLIK GÖRÜNTÜ ↔ CANLI karşılaştırmaz. Bu yüzden
# `has-pending-model-changes` "değişiklik yok" derken canlı şema
# göçlerin söylediğinden farklı olabiliyor — ve bugüne kadar bunu
# soran hiçbir şey yoktu.
#
# KALINTI MI, SÜREKLİ Mİ: modüller tek bir dönemden, bugünkü yarım göç
# olayıyla (2026-09-03) ÖRTÜŞMÜYOR. Yani bugün için kalıntı. Ama
# "sürekli değil" demek ölçülemezdi çünkü kimse bakmıyordu; çizgi
# bundan sonra bakacak.
#
# DDL GÜNLÜĞÜ YOK: `log_statement = none`, yani elle DROP INDEX izi
# aranamaz. Bu bir sınır ve gizlenmiyor.
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
# ═══ ÖNCE DERLE, SONRA MODELİ ÜRET ═══
#
# ÖNCEKİ HÂLİ VE ÖLÇÜLEN KUSURU (2026-09-08): önce `--no-build`
# deneniyordu ve `bin/` doluysa BAŞARILI oluyordu. Ama o ikili
# ÇALIŞMA AĞACINDAKİ KAYNAĞA AİT OLMAK ZORUNDA DEĞİL.
#
# Nitekim değildi: KATALOG/1 kodu geri alındıktan (revert) sonra
# çalışma ağacında `RoleManualPermissionGrant` HİÇBİR YERDE yoktu,
# ama `bin/` içinde önceki derlemeden kalmıştı. Kapı modelde
# `role_manual_permission_grants` indekslerini gördü ve "modelde var
# canlıda yok: 16 > 14" diyerek yayını durdurdu.
#
# Kapı yanlış değildi — YANLIŞ ŞEYE bakıyordu. Ölçtüğünü sandığı şey
# (bugünkü kaynak) ile gerçekten ölçtüğü şey (eski bir derleme
# çıktısı) ayrışmıştı (Kural 65).
#
# Derleme artık HER ZAMAN önce koşuyor. `derleme-kos.sh` üzerinden:
# bellek tavanı ve tek örnek kapısı orada. Artımlı derleme değişiklik
# yoksa zaten saniyeler sürüyor; bedeli, yanlış ölçümün bedelinden
# küçük.
#
# ÜÇ SONUÇ, ÜÇ MESAJ (Kural 67) — VE KANIT ATILMIYOR.
#
# ÖLÇÜLDÜ (2026-09-09): bu kapı yayını "proje derlenemedi" diyerek
# durdurdu. Proje AYNI KOMUTLA sorunsuz derleniyordu; kapının derlemesi
# 1.7 saniye CPU harcayıp düşmüştü, yani hiç derlemeye başlamamıştı.
# SEBEP ÖĞRENİLEMEDİ, çünkü çıktı `>/dev/null 2>&1` ile atılıyordu.
#
# Kapı yanlış değildi; SÖYLEDİĞİ ŞEY yanlıştı. "Derlenemedi" bir
# İHLAL beyanıdır ve kaynakta hata olduğunu söyler; oysa aynı çıkış
# kodu "koşucu meşgul" (75) ya da "süreç öldürüldü" anlamına da
# gelebiliyordu. Ölçememeyi ihlal diye raporlamak, bugün birkaç kez
# görülen sınıfın aynısı.
#
# ÜÇÜ DE YAYINI DURDURUR (kapalı-düşen); ayrım RAPORDA, davranışta
# değil. Gevşetme yok — yalnız kapı ne ölçtüğünü artık söylüyor.
#
DERLEME_KAYDI="$GECICI/derleme.log"
"${REPO_ROOT}/scripts/derleme-kos.sh" \
    dotnet build "$PROJE/EnderunAI.Api.csproj" -v q --nologo >"$DERLEME_KAYDI" 2>&1
DERLEME_KODU=$?

if [ "$DERLEME_KODU" -eq 75 ]; then
    hata "ÖLÇEMEDİ: derleme koşucusu MEŞGUL (çıkış 75, EX_TEMPFAIL)."
    hata "Bu bir şema sapması bulgusu DEĞİLDİR — ölçüm hiç yapılmadı."
    exit 1
fi

if [ "$DERLEME_KODU" -ne 0 ]; then
    # DERLEYİCİ HATASI MI, BAŞKA BİR ŞEY Mİ: ayrımı çıktının kendisi
    # söyler. Derleyici hatası yoksa "derlenemedi" demeyi hak etmiyoruz.
    if grep -qE ": error [A-Z]+[0-9]+" "$DERLEME_KAYDI"; then
        hata "HATA: proje derlenemedi; model üretilemez."
        grep -E ": error [A-Z]+[0-9]+" "$DERLEME_KAYDI" | head -5 >&2
    else
        hata "ÖLÇEMEDİ: derleme çıkış $DERLEME_KODU verdi ama çıktıda"
        hata "DERLEYİCİ HATASI YOK. Ölçüm yapılamadı; şema hakkında"
        hata "bu koşudan hiçbir sonuç çıkmaz."
        tail -15 "$DERLEME_KAYDI" >&2
    fi
    exit 1
fi

# SAVUNMA KAYDI (Kural 72): önceki biçimde bu satırın bir kopyası
# `if ! ...; then ... fi` bloğunun İÇİNDE, 12 boşluk girintiyle
# duruyordu. Blok kaldırıldığı için o kopya da silindi; satırın
# kendisi — aynı mesaj, aynı `exit 1` — burada duruyor. Üstüne bir
# savunma EKLENDİ: derleme düşerse model hiç üretilmiyor.
(cd "$PROJE" && dotnet ef dbcontext script --context AppDbContext --no-build -o "$GECICI/model.sql" >/dev/null 2>&1) \
    || { hata "HATA: modelden şema üretilemedi."; exit 1; }

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

CIZGI=$(grep -vE '^\s*#|^\s*$' "$CIZGI_DOSYASI" 2>/dev/null | sed -n '1p' | tr -dc '0-9')
CIZGI_EKSIK=$(grep -vE '^\s*#|^\s*$' "$CIZGI_DOSYASI" 2>/dev/null | sed -n '2p' | tr -dc '0-9')
[ -n "$CIZGI" ] && [ -n "$CIZGI_EKSIK" ] \
    || { hata "HATA: iki çizgi de okunamadı: $CIZGI_DOSYASI (1. satır sapma, 2. satır eksik)"; exit 1; }

echo "[sema-sapma] model ${MODEL_SAYI} · canlı ${CANLI_SAYI} · modelin bilmediği ${SAPMA} (çizgi ${CIZGI}) · modelde var canlıda yok ${EKSIK} (çizgi ${CIZGI_EKSIK})"

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

# ── İKİNCİ ÇİZGİ ──
if [ "$EKSIK" -gt "$CIZGI_EKSIK" ]; then
    hata "İKİNCİ ÇİZGİ AŞILDI: ${EKSIK} > ${CIZGI_EKSIK}."
    hata "Modelin tanımladığı bir nesne canlıda YOK — göç geçmişi ile"
    hata "canlı şema ayrışmış. EF bunu göremez: yalnız model ile anlık"
    hata "görüntüyü karşılaştırır, canlıya hiç bakmaz."
    comm -23 "$GECICI/model.txt" "$GECICI/canli.txt" | tail -5 | sed 's/^/  - /' >&2
    exit 1
fi

if [ "$EKSIK" -lt "$CIZGI_EKSIK" ]; then
    hata "İKİNCİ ÇİZGİ GEVŞEK: gerçek ${EKSIK}, çizgi ${CIZGI_EKSIK}."
    hata "Çizgiyi ${EKSIK} yapın: $CIZGI_DOSYASI (2. sayı)"
    exit 1
fi

if [ "$SAPMA" -lt "$CIZGI" ]; then
    hata "ÇİZGİ GEVŞEK: gerçek ${SAPMA}, çizgi ${CIZGI} — gevşeklik $((CIZGI - SAPMA))."
    hata "Çizgi bir TABANDIR. Gevşeklik bırakmak, kazanılan ilerlemeyi"
    hata "görünmez kılar ve geri kaymaya sessizce izin verir."
    hata "Çizgiyi ${SAPMA} yapın: $CIZGI_DOSYASI"
    exit 1
fi
