#!/usr/bin/env bash
#
# GÖÇ PROVASI — CANLININ TAZE KOPYASINDA
#
# NEDEN VAR: `gocleri_dogrula` kapısı *"göç uygulandı mı"* sorusunu
# cevaplıyor. *"Doğru mu"* sorusunu HİÇBİR ŞEY cevaplamıyordu.
#
# 2026-08-30'da üretilen bir göç canlıda patlayacaktı
# (`AddColumn "xmin"` → sistem sütunu çakışması) ve bunu yakalayan tek
# şey İNSANIN `Up()`'ı OKUMASI oldu. Kapı onu durdurmazdı.
#
# NE YAPAR:
#   bekleyen göç var mı?
#     → canlının TAZE bir kopyasını al
#     → göçü o kopyaya uygula
#     → başarısızsa ÇIKIŞ 1 (yayın durur, canlıya DOKUNULMAZ)
#     → başarılıysa kopya silinir, yayın devam eder
#
# ═══ NEDEN HER KOŞUDA TAZE KOPYA ═══
# Var olan `enderun_prova` veritabanı ÖLÇÜLDÜ ve BAYAT çıktı: canlıda
# 205 göç varken onda 204, en yeni verisi bir ay eski. Bayat bir kopya
# üzerinde prova, provanın kendisini yalanlar — canlıda patlayacak bir
# göç orada geçebilir. Kopya her koşuda sıfırdan alınır ve sonunda
# silinir.
#
# ═══ NEDEN CANLININ KOPYASI, BOŞ ŞEMA DEĞİL ═══
# Boş şemada geçen bir göç canlıdaki VERİYE takılabilir: benzersizlik
# ihlali, NOT NULL dolduramama, dönüşüm hatası. Prova ancak gerçek
# veriyle anlamlıdır.
#
# ═══ DÜRÜST SINIR ═══
# Bu prova göçün UYGULANABİLDİĞİNİ gösterir, DOĞRU OLDUĞUNU değil.
# Yanlış ama uygulanabilir bir göç (yanlış sütuna yazan, veriyi sessizce
# bozan) buradan geçer. Onun tek panzehiri, göçün etkisini sınayan
# testtir. Prova, "canlıda patlayan göç" sınıfını kapatır — hepsini
# değil.
#
# ═══ DOĞUŞ NOTU ═══
# Bu düzenek ilk üç koşusunda üç kez yanıldı; üçünde de sebep göç değil,
# ÖLÇMEDEN VARSAYILAN KURULUMDU:
#   1. `dotnet-ef` PATH'te sanıldı — /root/.dotnet/tools altındaydı.
#   2. Tek DbContext sanıldı — iki tane var (AppDbContext, HrDbContext),
#      ayrıca JWT_SECRET gerekiyordu.
#   3. Bağlantının `ConnectionStrings__` ile geçtiği sanıldı — fabrika
#      `DB_CONNECTION` okuyor (AppDbContextFactory:12).
# Üçü de kodu okuyunca beş saniyede çıkıyordu. Üçü de "bu göç canlıda da
# patlardı" diye raporlandı — üçü de yalandı.
#
# Sınıflandırmanın asimetrik olmasının sebebi bu deneyim: kırmızı hüküm
# POZİTİF KANIT ister, kanıt yoksa KARAR VEREMEDİ.
#
# KULLANIM:
#   goc-provasi.sh          # bekleyen göç varsa prova eder
#   goc-provasi.sh --zorla  # bekleyen göç olmasa da son göçü oynatır
#                           # (düzeneğin kendisini sınamak için)

set -uo pipefail

REPO_ROOT="/var/www/enderun-ai"
ENV_FILE="/etc/enderunai/backend.env"
PROVA_DB="enderun_goc_provasi"
KIP="${1:-normal}"

log() { echo "[goc-provasi] $*"; }
hata() { echo "[goc-provasi] HATA: $*" >&2; }

canli="$(sudo grep -E '^DB_CONNECTION=' "$ENV_FILE" 2>/dev/null | sed -E 's/^DB_CONNECTION=//' | tr -d "'\"")"
[ -z "$canli" ] && { hata "DB_CONNECTION okunamadı."; exit 1; }

CANLI_DB="$(sed -n 's/.*Database=\([^;]*\).*/\1/p' <<<"$canli")"
[ -z "$CANLI_DB" ] && { hata "Canlı veritabanı adı çözülemedi."; exit 1; }

# ── 1) Bekleyen göç var mı ──
kaynak="$(mktemp)"; gecmis="$(mktemp)"
temizle() { rm -f "$kaynak" "$gecmis"; }
trap temizle EXIT

{ ls "${REPO_ROOT}"/backend/EnderunAI.Api/Migrations/*.cs 2>/dev/null
  ls "${REPO_ROOT}"/backend/EnderunAI.Api/Migrations/HumanResources/*.cs 2>/dev/null; } \
  | grep -v Designer | grep -v Snapshot \
  | sed 's|.*/||; s|\.cs$||' | sort > "$kaynak"

sudo -u postgres psql -d "$CANLI_DB" -tAc \
    'select "MigrationId" from "__EFMigrationsHistory"' 2>/dev/null | sort > "$gecmis"

bekleyen="$(comm -23 "$kaynak" "$gecmis" | grep -c . || true)"

if [ "$bekleyen" -eq 0 ] && [ "$KIP" != "--zorla" ]; then
    log "Bekleyen göç yok — prova gerekmiyor."
    exit 0
fi

if [ "$bekleyen" -gt 0 ]; then
    log "Bekleyen göç: $bekleyen"
    comm -23 "$kaynak" "$gecmis" | sed 's/^/           /'
else
    log "--zorla: bekleyen göç yok, düzenek sınanıyor."
fi

# ── 2) TAZE kopya ──
log "Canlının taze kopyası alınıyor: $PROVA_DB"
sudo -u postgres psql -tAc "select pg_terminate_backend(pid) from pg_stat_activity where datname='$PROVA_DB';" >/dev/null 2>&1
sudo -u postgres dropdb --if-exists "$PROVA_DB" || { hata "Eski kopya silinemedi."; exit 1; }

# KOPYA CANLIYLA AYNI SAHİBE AİT OLMALI.
#
# İlk sürüm kopyayı `postgres` adına açıyordu ve negatif kontrolde
# `42501: permission denied for schema public` çıktı: bağlantı
# `enderun_user` ile geliyor, şema `postgres`'e ait, kullanıcı tablo
# oluşturamıyor. BU HÂLİYLE PROVA GEÇERLİ BİR GÖÇÜ DE REDDEDERDİ —
# yani her göçü kırmızı verirdi. Yanlış kırmızının en kötü türü.
#
# Sahip canlıdan OKUNUYOR, varsayılmıyor.
CANLI_SAHIP="$(sudo -u postgres psql -tAc \
    "select pg_get_userbyid(datdba) from pg_database where datname='$CANLI_DB';" \
    | tr -d ' ')"
[ -z "$CANLI_SAHIP" ] && { hata "Canlı veritabanının sahibi okunamadı."; exit 1; }
log "Kopya sahibi canlıdan alındı: $CANLI_SAHIP"

if ! sudo -u postgres createdb -O "$CANLI_SAHIP" -T "$CANLI_DB" "$PROVA_DB" 2>/dev/null; then
    # TEMPLATE, canlıya açık bağlantı varsa çalışmaz — dump yoluna düş.
    log "TEMPLATE yolu kapalı (canlıda açık bağlantı olabilir), dump yoluna geçiliyor."
    sudo -u postgres createdb -O "$CANLI_SAHIP" "$PROVA_DB" || { hata "Kopya oluşturulamadı."; exit 1; }
    if ! sudo -u postgres pg_dump "$CANLI_DB" | sudo -u postgres psql -q -d "$PROVA_DB" >/dev/null 2>&1; then
        hata "Kopya doldurulamadı."; exit 1
    fi
fi

# ── 3) Kopyanın TAZE olduğunu KANITLA ──
c_goc="$(sudo -u postgres psql -d "$CANLI_DB" -tAc 'select count(*) from "__EFMigrationsHistory";')"
p_goc="$(sudo -u postgres psql -d "$PROVA_DB" -tAc 'select count(*) from "__EFMigrationsHistory";')"

if [ "$c_goc" != "$p_goc" ]; then
    hata "Kopya taze değil: canlı $c_goc göç, kopya $p_goc göç."
    hata "Bayat kopya üzerinde prova, provanın kendisini yalanlar."
    exit 1
fi
# SAHİPLİK KANITI — varsayılmaz, okunur.
p_sahip="$(sudo -u postgres psql -tAc \
    "select pg_get_userbyid(datdba) from pg_database where datname='$PROVA_DB';" | tr -d ' ')"
if [ "$p_sahip" != "$CANLI_SAHIP" ]; then
    hata "KARAR VEREMEDİ: kopyanın sahibi canlıyla aynı değil ($p_sahip ≠ $CANLI_SAHIP)."
    hata "Bu hâliyle prova GEÇERLİ bir göçü de reddeder (42501)."
    sudo -u postgres dropdb --if-exists "$PROVA_DB" >/dev/null 2>&1
    exit 2
fi
log "SAHİPLİK KANITI: kopya sahibi $p_sahip · canlıyla aynı"

# TAZELİK KANITI HER KOŞUDA BASILIR — sessizce doğrulanmaz.
#
# Var olan `enderun_prova` ÖLÇÜLDÜ ve bayat çıktı: canlıda 205 göç
# varken onda 204, verisi bir ay eski. Bayat kopya üzerinde yapılan
# prova, provanın kendisini YALANLAR ve "prova ettik" diyerek yanlış
# güvence üretir. Okunmayan bir kontrol, kontrol değildir.
log "TAZELİK KANITI: canlı $c_goc göç · kopya $p_goc göç · EŞİT"

# ── 4) Göçü kopyaya uygula ──
prova_baglanti="${canli//Database=$CANLI_DB;/Database=$PROVA_DB;}"
if [ "$prova_baglanti" = "$canli" ]; then
    hata "Bağlantı dizesi değiştirilemedi — PROVA CANLIYA UYGULARDI. Durdum."
    exit 1
fi

# ── ARAÇ VARLIĞI ÖNCE DOĞRULANIR ──
#
# İLK KOŞUDA BU KONTROL YOKTU VE DÜZENEK YANLIŞ SEBEPLE KIRMIZI VERDİ:
# `dotnet-ef` bulunamadığında "PROVA DÜŞTÜ — bu göç canlıda da patlardı"
# yazdı. Göç patlamamıştı; ARAÇ YOKTU.
#
# Bu, bir kapının en tehlikeli hâli: yanlış kırmızı, kapıya güveni yok
# eder ve bir sonraki kişi onu devre dışı bırakır. "Araç yok" ile
# "göç bozuk" AYRI SONUÇLARDIR ve ayrı raporlanmalıdır (Kural 68 —
# muhafızın üç sonucu vardır: GEÇTİ / İHLAL / KARAR VEREMEDİ).
EF_ARACI="${DOTNET_EF:-/root/.dotnet/tools/dotnet-ef}"

if [ ! -x "$EF_ARACI" ]; then
    hata "KARAR VEREMEDİ: dotnet-ef bulunamadı ($EF_ARACI)."
    hata "Bu bir göç hatası DEĞİL — araç eksik. Prova yapılamadı."
    sudo -u postgres dropdb --if-exists "$PROVA_DB" >/dev/null 2>&1
    exit 2
fi

# ── İKİ BAĞLAM AYRI AYRI ──
#
# Depoda iki DbContext var: `AppDbContext` (Migrations/) ve `HrDbContext`
# (Migrations/HumanResources/). `--context` verilmezse `dotnet ef`
# "More than one DbContext was found" der ve DÜŞER — bu bir GÖÇ HATASI
# DEĞİLDİR. İlk koşumda düzenek tam da bunu "bu göç canlıda da patlardı"
# diye raporladı; ikinci yanlış kırmızıydı.
#
# JWT_SECRET DE GEREKİYOR: `dotnet ef` uygulamanın Host'unu ayağa
# kaldırıyor ve doğrulama oradan geçiyor. Gerçek sır GEREKMİYOR —
# göç uygulanırken kimse jeton üretmiyor; yalnız değişkenin VARLIĞI
# aranıyor. Bu yüzden burada tek kullanımlık, anlamsız bir değer
# veriliyor ve HİÇBİR YERE YAZILMIYOR.
BAGLAMLAR=(AppDbContext HrDbContext)
sonuc=0

for baglam in "${BAGLAMLAR[@]}"; do
    log "Göç kopyaya uygulanıyor — bağlam: $baglam"
    cikti="$(mktemp)"

    # DB_CONNECTION — FABRİKANIN OKUDUĞU DEĞİŞKEN.
    #
    # Önce yalnız `ConnectionStrings__DefaultConnection` veriliyordu ve
    # düzenek ÜÇÜNCÜ kez kurulum hatasıyla düştü:
    # "Migration işlemi için DB_CONNECTION tanımlı değil."
    # `AppDbContextFactory:12` tasarım zamanında `DB_CONNECTION`
    # okuyor — ölçüldü, varsayılmadı. İkisi de veriliyor.
    #
    # DEĞER PROVA KOPYASINI GÖSTERİYOR, CANLIYI DEĞİL. Bu satır
    # yanlış olursa prova canlıya uygulanır; yukarıda ayrıca
    # kontrol ediliyor (bağlantı dizesi değişmediyse durulur).
    if DB_CONNECTION="$prova_baglanti" \
            ConnectionStrings__DefaultConnection="$prova_baglanti" \
            JWT_SECRET="goc-provasi-gecici-anlamsiz-deger-0123456789" \
            "$EF_ARACI" database update \
            --project "${REPO_ROOT}/backend/EnderunAI.Api" \
            --context "$baglam" >"$cikti" 2>&1; then
        # BAŞARI YOLUNDA DA UYGULANAN GÖÇLER BASILIYOR.
        #
        # İlk sürümde EF çıktısı geçici dosyaya gidiyor ve siliniyordu;
        # başarılı bir koşuda HANGİ göçün uygulandığı görünmüyordu.
        # Teşhis için gerekli — ve pozitif kontrolün boş olup olmadığı
        # ancak buradan anlaşılır.
        uygulanan="$(grep -c "Applying migration" "$cikti" || true)"
        log "  $baglam: GEÇTİ · uygulanan göç: $uygulanan"
        [ "$uygulanan" -gt 0 ] && grep "Applying migration" "$cikti" | sed 's/^/           /'
        true
    else
        # ═══════════════════════════════════════════════════════════
        #  SINIFLANDIRMA ASİMETRİKTİR: "GÖÇ PATLARDI" HÜKMÜ
        #  POZİTİF OLARAK KANITLANMADIKÇA VERİLMEZ.
        # ═══════════════════════════════════════════════════════════
        #
        # İlk sürüm bir YASAK LİSTESİ tutuyordu (dotnet-ef yok,
        # More than one DbContext, MSB*...). O liste GÖZLEMLE BÜYÜR ve
        # her büyüme BİR YANLIŞ KIRMIZIYLA SATIN ALINIR: ilk iki koşuda
        # iki kez oldu, üçüncüsü henüz görülmemiş bir kurulum hatasıyla
        # gelirdi ve yine "bu göç canlıda da patlardı" derdi.
        #
        # NEDEN ASİMETRİK: yanlış kırmızı KAPININ KENDİSİNİ ÖLDÜRÜR —
        # bir sonraki kişi ona güvenmez ve devre dışı bırakır. Yanlış
        # "karar veremedi" ise yalnızca bir insanın bakmasını ister.
        # Maliyet farkı asimetrik olduğu için sınıflandırma da öyle.
        # (KAPI/1'in kararıyla aynı ilke: bilinmeyen durum güvenli
        # tarafa düşer.)
        #
        # KIRMIZI İÇİN ÜÇ KANITTAN BİRİ ARANIR:
        kanit=""

        #  (1) Uygulama aşamasına GERÇEKTEN girildi mi?
        #      `dotnet ef` her göç için "Applying migration '...'" basar.
        #      Bu satır varsa araç/derleme/bağlam kurma aşaması geçilmiş
        #      demektir; hata göçün kendisindedir.
        if grep -q "Applying migration" "$cikti"; then
            kanit="uygulama aşamasına girildi (Applying migration)"
        fi

        #  (2) PostgreSQL'in ürettiği bir hata mı?
        #      Npgsql `PostgresException` ve beş karakterlik SQLSTATE
        #      basar (42P07 = relation already exists gibi).
        if grep -qE "PostgresException|\b[0-9][0-9A-Z]{4}:" "$cikti"; then
            kanit="${kanit:+$kanit · }PostgreSQL hatası (SQLSTATE)"
        fi

        #  (3) Hata metninde uygulanmaya çalışılan göçün adı geçiyor mu?
        while IFS= read -r goc_adi; do
            [ -z "$goc_adi" ] && continue
            if grep -qF "$goc_adi" "$cikti"; then
                kanit="${kanit:+$kanit · }göç adı çıktıda: $goc_adi"
                break
            fi
        done < <(comm -23 "$kaynak" "$gecmis")

        if [ -z "$kanit" ]; then
            hata "KARAR VEREMEDİ ($baglam): hata göçe ait olduğu KANITLANAMADI."
            hata "Araç, derleme ya da bağlam kurma aşamasında düşmüş olabilir."
            hata "Bir insan bakmalı — yayın durduruldu ama göç suçlanmadı."
            tail -15 "$cikti" >&2
            rm -f "$cikti"
            sudo -u postgres dropdb --if-exists "$PROVA_DB" >/dev/null 2>&1
            exit 2
        fi

        hata "PROVA DÜŞTÜ ($baglam) — bu göç canlıda da patlardı."
        hata "GEREKÇE: $kanit"
        hata "Gerekçesiz kırmızı verilmez; aşağıda ham çıktı:"
        tail -25 "$cikti" >&2
        sonuc=1
        rm -f "$cikti"
        break
    fi
    rm -f "$cikti"
done

# ═══════════════════════════════════════════════════════════════
#  UYGULAMA SONRASI DOĞRULAMA: "EF NE DEDİ" DEĞİL, "VERİTABANINDA
#  NE OLDU".
# ═══════════════════════════════════════════════════════════════
#
# NEGATİF KONTROLDE ORTAYA ÇIKAN GERÇEK KUSUR: düzenek
# "Bekleyen göç: 1 — SONDA_BozukGoc" dedi, sonra "PROVA GEÇTİ" dedi.
# İkisi aynı anda doğru olamaz.
#
# Sebep: iki taraf FARKLI ŞEYE bakıyordu —
#   bu betik : `ls Migrations/*.cs`  → DOSYA ADI
#   dotnet ef: `[Migration]` niteliği → DERLENMİŞ SINIF
# Sonda göçünde nitelik yoktu; dosya vardı, göç yoktu. EF hiçbir şey
# uygulamadı ve "başarılı" döndü.
#
# BU, PROVANIN VEREBİLECEĞİ EN TEHLİKELİ YANLIŞ GÜVENCEDİR: kırmızı
# değil, YEŞİL verir. "Prova ettik" denir, hiçbir şey prova edilmemiştir.
#
# Kapatma: uygulamadan SONRA kopyanın göç geçmişi yeniden okunur ve
# bekleyen göçlerin GERÇEKTEN eklendiği doğrulanır.
if [ "$sonuc" -eq 0 ] && [ "$bekleyen" -gt 0 ]; then
    sonrasi="$(mktemp)"
    sudo -u postgres psql -d "$PROVA_DB" -tAc \
        'select "MigrationId" from "__EFMigrationsHistory"' 2>/dev/null | sort > "$sonrasi"

    eksik=""
    while IFS= read -r goc_adi; do
        [ -z "$goc_adi" ] && continue
        grep -qxF "$goc_adi" "$sonrasi" || eksik="${eksik}${goc_adi}\n"
    done < <(comm -23 "$kaynak" "$gecmis")
    rm -f "$sonrasi"

    if [ -n "$eksik" ]; then
        hata "KARAR VEREMEDİ: bekleyen göçler kopyaya UYGULANMADI."
        hata "EF 'başarılı' döndü ama geçmişe eklenmeyen göç var:"
        printf "%b" "$eksik" | sed 's/^/           /' >&2
        hata "Muhtemel sebep: göç dosyasında [Migration] niteliği yok —"
        hata "dosya var, EF onu göç olarak görmüyor."
        sudo -u postgres dropdb --if-exists "$PROVA_DB" >/dev/null 2>&1
        exit 2
    fi

    log "UYGULAMA KANITI: bekleyen $bekleyen göçün hepsi kopyanın geçmişinde."
fi

[ "$sonuc" -eq 0 ] && log "PROVA GEÇTİ — iki bağlam da canlının kopyasında sorunsuz uygulandı."

# ── 5) Kopya silinir ──
sudo -u postgres psql -tAc "select pg_terminate_backend(pid) from pg_stat_activity where datname='$PROVA_DB';" >/dev/null 2>&1
sudo -u postgres dropdb --if-exists "$PROVA_DB" >/dev/null 2>&1
log "Kopya silindi."

exit $sonuc
