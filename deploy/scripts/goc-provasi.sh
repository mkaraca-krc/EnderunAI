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

# CANLI GEÇMİŞİ OKUNAMAZSA SUSMA — SONDA C ORTAYA ÇIKARDI.
#
# Burada `2>/dev/null | sort > "$gecmis"` vardı: psql düşerse hata
# yutuluyor, `$gecmis` BOŞ kalıyor ve o boşluk "hiçbir göç uygulanmamış"
# diye okunuyordu. Bozuk bağlantı dizesiyle koşulan sonda, 1 bekleyen
# göç yerine **206** bekleyen göç raporladı ve provayı yıkıcı beyan
# kapısında düşürdü — kapalı düştü, ama TEŞHİSİ YANLIŞTI. "Canlıya
# bağlanamadım" demesi gerekirken "beyanda geçmeyen yıkıcı kalem var"
# dedi. Yanlış teşhis, hatayı arayan kişiyi yanlış yere gönderir.
#
# Boru hattında psql'in çıkış kodu KAYBOLUR (`| sort` kazanır), bu
# yüzden önce dosyaya yazılıp durum ayrıca sınanıyor.
ham_gecmis="$(mktemp)"
if ! sudo -u postgres psql -d "$CANLI_DB" -tAc \
        'select "MigrationId" from "__EFMigrationsHistory"' > "$ham_gecmis" 2>&1; then
    hata "Canlı göç geçmişi OKUNAMADI (veritabanı: $CANLI_DB)."
    hata "Bağlantı dizesi ya da veritabanı adı hatalı olabilir."
    hata "Geçmiş okunamadan bekleyen göç hesaplanamaz — prova yapılmıyor."
    rm -f "$ham_gecmis"
    exit 1
fi
sort "$ham_gecmis" | grep -v '^$' > "$gecmis"
rm -f "$ham_gecmis"

if [ ! -s "$gecmis" ]; then
    hata "Canlı göç geçmişi BOŞ döndü — bu canlı bir veritabanı değil."
    hata "Boş geçmiş, bütün göçleri 'bekliyor' gösterir ve provayı yanıltır."
    exit 1
fi

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

# ═══════════════════════════════════════════════════════════════
#  YIKICI İŞLEM BEYANI — EŞLEŞTİRMELİ
# ═══════════════════════════════════════════════════════════════
#
# SONDA A ORTAYA ÇIKARDI: `DropColumn "Title"` geçerli SQL'dir, kopyada
# sorunsuz koşar ve prova "GEÇTİ" der. Yani bu kapı olmadan, canlıdaki
# her sütunu düşüren bir göç provadan KANITLI YEŞİL alarak yayına
# girerdi. Mehmet'in ölçütüyle: kapı o sınıf için SAHTEYDİ.
#
# YASAK DEĞİL BEYAN: meşru DropColumn vardır (ölü sütun temizliği).
# Yasak meşru işi engeller ve kapıyı devre dışı bıraktırır; beyan
# yalnızca "ne yaptığını söyle" der.
#
# BEYAN EŞLEŞTİRMELİDİR — dört şart:
#   1. göçteki her yıkıcı kalem beyanda ADIYLA geçmeli
#   2. beyanda olup göçte olmayan kalem varsa DÜŞER (kopyalanmış beyan)
#   3. "yıkıcı: evet" türü kalıp geçerli değildir — ad aranır
#   4. DropColumn + AddColumn aynı göçte ise RENAME DEĞİLDİR, veri
#      kaybıdır; ayrıca uyarılır
#
# BİÇİM: commit mesajında satır başında
#   YIKICI-BEYAN: WorkTasks.Title, WorkTasks.Aciklama
yikici_kalemler() {
    local dosya="$1"
    grep -oE 'DropColumn\([^;]*\)' "$dosya" 2>/dev/null | tr -d '\n' \
        | grep -oE 'name:[[:space:]]*"[^"]+",[[:space:]]*table:[[:space:]]*"[^"]+"' \
        | sed -E 's/name:[[:space:]]*"([^"]+)",[[:space:]]*table:[[:space:]]*"([^"]+)"/\2.\1/' || true
    grep -oE 'DropTable\([[:space:]]*name:[[:space:]]*"[^"]+"' "$dosya" 2>/dev/null \
        | sed -E 's/.*"([^"]+)"/\1/' || true
    grep -oiE '(DROP[[:space:]]+TABLE|TRUNCATE)[[:space:]]+"?([A-Za-z_][A-Za-z0-9_]*)"?' "$dosya" 2>/dev/null \
        | sed -E 's/.*[[:space:]]"?([A-Za-z_][A-Za-z0-9_]*)"?$/\1/' || true
}

tum_kalemler=""; rename_uyarisi=""
while IFS= read -r goc_adi; do
    [ -z "$goc_adi" ] && continue
    dosya="$(ls "${REPO_ROOT}"/backend/EnderunAI.Api/Migrations/"$goc_adi".cs \
             "${REPO_ROOT}"/backend/EnderunAI.Api/Migrations/HumanResources/"$goc_adi".cs \
             2>/dev/null | head -1)"
    [ -z "$dosya" ] && continue
    k="$(yikici_kalemler "$dosya" | sort -u)"
    [ -n "$k" ] && tum_kalemler="${tum_kalemler}${k}
"
    if grep -q "DropColumn" "$dosya" && grep -q "AddColumn" "$dosya"; then
        rename_uyarisi="${rename_uyarisi}${goc_adi}
"
    fi
done < <(comm -23 "$kaynak" "$gecmis")

tum_kalemler="$(printf '%s' "$tum_kalemler" | grep -v '^$' | sort -u || true)"

if [ -n "$tum_kalemler" ]; then
    echo
    echo "╔══════════════════════════════════════════════════════════════════╗"
    echo "║  YIKICI İŞLEM İÇEREN GÖÇ                                         ║"
    echo "╚══════════════════════════════════════════════════════════════════╝"
    printf '%s\n' "$tum_kalemler" | sed 's/^/      /'

    if [ -n "$rename_uyarisi" ]; then
        echo
        echo "  ⚠ AYNI GÖÇTE DropColumn + AddColumn:"
        printf '%s' "$rename_uyarisi" | sed 's/^/      /'
        echo "    BU BİR RENAME DEĞİLDİR — VERİ KAYBIDIR. EF rename için"
        echo "    RenameColumn üretir; Drop+Add çifti eski değerleri SİLER."
        echo "    Rename beyanıyla geçirilmez."
    fi

    # BEYAN KAYNAĞI: commit mesajı. `YIKICI_BEYAN` ortam değişkeni yalnız
    # SINAMA içindir — düzeneğin kendi sondalarında commit üretmemek için.
    if [ -n "${YIKICI_BEYAN:-}" ]; then
        beyan="$YIKICI_BEYAN"
        log "(sınama kipi: beyan ortam değişkeninden okundu)"
    else
        beyan="$(git -C "$REPO_ROOT" log --format=%B -1 HEAD 2>/dev/null \
                 | grep -E '^YIKICI-BEYAN:' | sed 's/^YIKICI-BEYAN:[[:space:]]*//' || true)"
    fi

    if [ -z "$beyan" ]; then
        echo
        hata "PROVA DÜŞTÜ: yıkıcı işlem var, YIKICI-BEYAN yok."
        hata "Commit mesajına satır başında ekleyin, HER KALEMİ ADIYLA:"
        hata "    YIKICI-BEYAN: $(printf '%s' "$tum_kalemler" | tr '\n' ',' | sed 's/,$//')"
        exit 1
    fi

    eksik=""; fazla=""
    while IFS= read -r k; do
        [ -z "$k" ] && continue
        grep -qF "$k" <<<"$beyan" || eksik="${eksik}${k}
"
    done <<<"$tum_kalemler"

    for b in $(tr ',' ' ' <<<"$beyan"); do
        b="$(tr -d ' ' <<<"$b")"
        [ -z "$b" ] && continue
        grep -qE '^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?$' <<<"$b" || continue
        grep -qxF "$b" <<<"$tum_kalemler" || fazla="${fazla}${b}
"
    done

    if [ -n "$eksik" ]; then
        echo
        hata "PROVA DÜŞTÜ: beyanda ADIYLA geçmeyen yıkıcı kalem var:"
        printf '%s' "$eksik" | sed 's/^/           /' >&2
        hata "Kalıp beyan (\"yıkıcı: evet\") geçerli değildir; ad aranır."
        exit 1
    fi

    if [ -n "$fazla" ]; then
        echo
        hata "PROVA DÜŞTÜ: beyanda olup göçte OLMAYAN kalem var:"
        printf '%s' "$fazla" | sed 's/^/           /' >&2
        hata "Bu, eski bir beyanın kopyalandığının işaretidir."
        exit 1
    fi

    echo
    log "YIKICI BEYAN EŞLEŞTİ: $beyan"
    log "SAHİPLİK ve TAZELİK kanıtı aşağıda görünmeden geçiş yok."
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

# SAYI EŞİTLİĞİ TAZELİK KANITI DEĞİLDİR — KÜME EŞİTLİĞİ GEREKİR.
#
# İlk hâli yalnız `count(*)` karşılaştırıyordu. 205 = 205 iki farklı
# göç kümesi için de doğru olabilir: kopyada A varken canlıda B varsa
# sayılar tutar, kümeler tutmaz. Daha kötüsü, kopyada uygulanmış bir
# göç bekleyen sayılmaz — o göç kopyada SESSİZCE atlanır ve prova
# "uyguladım" demeden yeşil verir. Bayatlığın en tehlikeli biçimi
# sayıyla görünmez.
c_kume="$(mktemp)"; p_kume="$(mktemp)"
sudo -u postgres psql -d "$CANLI_DB" -tAc \
    'select "MigrationId" from "__EFMigrationsHistory"' | sort | grep -v '^$' > "$c_kume"
sudo -u postgres psql -d "$PROVA_DB" -tAc \
    'select "MigrationId" from "__EFMigrationsHistory"' | sort | grep -v '^$' > "$p_kume"
if ! diff -q "$c_kume" "$p_kume" >/dev/null; then
    hata "Kopya taze DEĞİL: göç KÜMELERİ canlıyla aynı değil (sayılar eşit olsa da)."
    comm -23 "$c_kume" "$p_kume" | sed 's/^/           yalnız canlıda: /' >&2
    comm -13 "$c_kume" "$p_kume" | sed 's/^/           yalnız kopyada: /' >&2
    rm -f "$c_kume" "$p_kume"
    sudo -u postgres dropdb --if-exists "$PROVA_DB" >/dev/null 2>&1
    exit 1
fi
rm -f "$c_kume" "$p_kume"
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
log "TAZELİK KANITI: canlı $c_goc göç · kopya $p_goc göç · KÜMELER BİREBİR AYNI"

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

# ═══════════════════════════════════════════════════════════════
#  TEK DERLEME — BÜTÜN ÖLÇÜMLER AYNI ÇIKTIYI OKUR
# ═══════════════════════════════════════════════════════════════
#
# SORU 3'ÜN CEVABI. Eski düzenek "bekleyen göç"ü `ls Migrations/*.cs`
# ile DOSYA ADINDAN, "uygulandı"yı ise veritabanından okuyordu. İki
# ayrı gerçek karşılaştırılıyordu ve aralarındaki fark sessizce
# yutuluyordu: bir dosya var ama derlenmiş sınıfta `[Migration]`
# niteliği yoksa dosya "bekliyor" görünür, EF ise onu hiç görmez.
#
# Ayrıca ÖLÇÜLDÜ: `--no-build` ile çalıştırılan `migrations list`,
# kaynaktan SİLİNMİŞ bir göçü hâlâ listeledi — çünkü eski derleme
# çıktısını okuyordu. Yani "hangi göçler var" sorusunun cevabı bile
# hangi ikiliyi okuduğunuza bağlı.
#
# Kapatma: burada BİR KEZ derlenir, sonraki bütün `dotnet ef`
# çağrıları `--no-build` ile AYNI ikiliyi okur. Bekleyen küme de,
# uygulanan küme de tek kaynaktan gelir.
log "Derleniyor (bir kez) — bütün ölçümler aynı çıktıyı okuyacak."
derleme="$(mktemp)"
if ! dotnet build "${REPO_ROOT}/backend/EnderunAI.Api" --nologo -v q >"$derleme" 2>&1; then
    hata "KARAR VEREMEDİ: proje DERLENMEDİ — prova yapılamadı."
    hata "Bu bir göç hatası değil, derleme hatası:"
    tail -15 "$derleme" | sed 's/^/           /' >&2
    rm -f "$derleme"
    sudo -u postgres dropdb --if-exists "$PROVA_DB" >/dev/null 2>&1
    exit 2
fi
rm -f "$derleme"

# ef_liste <baglam> <cikti_dosyasi>
# Kopyaya bağlanır, o bağlamın göçlerini "id<TAB>uygulandi_mi" olarak yazar.
# AYNI BAĞLAM, AYNI BAĞLANTI — bekleyen de uygulanan da buradan gelir.
ef_liste() {
    local baglam="$1" hedef="$2" ham
    ham="$(mktemp)"
    if ! DB_CONNECTION="$prova_baglanti" \
            ConnectionStrings__DefaultConnection="$prova_baglanti" \
            JWT_SECRET="goc-provasi-gecici-anlamsiz-deger-0123456789" \
            "$EF_ARACI" migrations list --no-build --json \
            --project "${REPO_ROOT}/backend/EnderunAI.Api" \
            --context "$baglam" >"$ham" 2>&1; then
        hata "  ($baglam) migrations list düştü; çıktının sonu:"
        tail -8 "$ham" | sed 's/^/           /' >&2
        rm -f "$ham"; return 1
    fi
    # AYRIŞTIRMA SAĞLAM OLMAK ZORUNDA — ÖLÇÜLDÜ:
    # `dotnet ef --json`, JSON'dan ÖNCE kayıt satırları basıyor
    # ("info: ...Database.Command[20101]") ve bunların içinde de `[`
    # var. İlk `[` karakterinden okuyan ayrıştırıcı HrDbContext'te
    # düştü, üstelik SESSİZCE — komut başarılıydı (çıkış 0), okuma
    # başarısızdı. Artık JSON dizisi sondan aranır ve düşerse ham
    # çıktının kuyruğu basılır.
    python3 - "$ham" "$hedef" <<'PYEOF'
import io, json, sys
ham = io.open(sys.argv[1], encoding='utf-8', errors='replace').read()
son = ham.rfind(']')
kayitlar = None
if son >= 0:
    bas = -1
    while True:
        bas = ham.find('[', bas + 1)
        if bas < 0 or bas > son:
            break
        try:
            aday = json.loads(ham[bas:son + 1])
        except Exception:
            continue
        if isinstance(aday, list) and all(isinstance(x, dict) and 'id' in x for x in aday):
            kayitlar = aday
            break
if kayitlar is None:
    sys.stderr.write('göç listesi JSON olarak okunamadı; ham çıktının sonu:\n')
    sys.stderr.write(ham[-800:] + '\n')
    sys.exit(1)
with io.open(sys.argv[2], 'w', encoding='utf-8') as f:
    for k in sorted(kayitlar, key=lambda x: x['id']):
        f.write('%s\t%s\n' % (k['id'], 'E' if k['applied'] else 'H'))
PYEOF
    local d=$?
    rm -f "$ham"
    return $d
}

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
ef_bekleyen_toplam=0

# ═══════════════════════════════════════════════════════════════
#  ÖNCE ÖLÇÜMÜ VE ÇAPRAZ DOĞRULAMA — HEPSİ DDL'DEN ÖNCE
# ═══════════════════════════════════════════════════════════════
#
# SORU 1'İN CEVABI BURADA VERİLİR: kopya gerçekten sıfırdan mı kuruldu,
# uygulanacak göç kopyada ZATEN var mı?
#
# SONDA C2 ORTAYA ÇIKARDI: `dotnet ef migrations list` veritabanına
# BAĞLANAMADIĞI HÂLDE ÇIKIŞ 0 DÖNDÜ ve bütün göçleri "uygulanmamış"
# gösterdi — "kopyada uygulanmış 0 · bekleyen 199". Bağlanamamak ile
# hiçbir şeyin uygulanmamış olması AYNI ŞEY DEĞİLDİR, ama araç ikisini
# aynı çıktıyla anlatıyor. Ölçümün kendisi sessizce yalan söyleyebilir.
#
# Kapatma: EF'in "uygulanmış" saydığı toplam, kopyanın
# `__EFMigrationsHistory` satır sayısıyla ($p_goc) TUTMAK ZORUNDA.
# İki bağımsız okuyucu; tuttukları yerde ölçüme güvenilir, tutmadıkları
# yerde susulmaz.
declare -A ONCE_DOSYA
ef_uygulanmis_toplam=0
for baglam in "${BAGLAMLAR[@]}"; do
    o_dosya="$(mktemp)"
    if ! ef_liste "$baglam" "$o_dosya"; then
        hata "KARAR VEREMEDİ ($baglam): göç listesi okunamadı — prova yapılamadı."
        sudo -u postgres dropdb --if-exists "$PROVA_DB" >/dev/null 2>&1
        exit 2
    fi
    ONCE_DOSYA[$baglam]="$o_dosya"
    o_uygulanmis="$(grep -c 'E$' "$o_dosya" || true)"
    o_bekleyen="$(grep -c 'H$' "$o_dosya" || true)"
    ef_uygulanmis_toplam=$(( ef_uygulanmis_toplam + o_uygulanmis ))
    ef_bekleyen_toplam=$(( ef_bekleyen_toplam + o_bekleyen ))
    log "  ÖNCE ($baglam): kopyada uygulanmış $o_uygulanmis · bekleyen $o_bekleyen"
    if [ "$o_bekleyen" -gt 0 ] && [ "$o_bekleyen" -le 20 ]; then
        grep 'H$' "$o_dosya" | cut -f1 | sed 's/^/           bekleyen: /'
    elif [ "$o_bekleyen" -gt 20 ]; then
        grep 'H$' "$o_dosya" | cut -f1 | head -5 | sed 's/^/           bekleyen: /'
        log "           … ve $(( o_bekleyen - 5 )) tane daha"
    fi
done

if [ "$ef_uygulanmis_toplam" != "$p_goc" ]; then
    hata "KARAR VEREMEDİ: iki okuyucu kopya hakkında AYNI ŞEYİ SÖYLEMİYOR."
    hata "EF 'uygulanmış' diyor: $ef_uygulanmis_toplam · kopyanın geçmişinde: $p_goc satır"
    hata "En olası sebep: EF kopyaya bağlanamadı ve bunu hata olarak bildirmedi."
    hata "Ölçüme güvenilemeyen yerde prova yapılmaz."
    sudo -u postgres dropdb --if-exists "$PROVA_DB" >/dev/null 2>&1
    exit 2
fi
log "ÖNCE KANITI: EF'in saydığı $ef_uygulanmis_toplam uygulanmış göç, kopyanın geçmişiyle TUTUYOR."

# DOSYA ile DERLENMİŞ İKİLİ ÇELİŞİYOR MU — ARTIK DDL'DEN ÖNCE.
if [ "$bekleyen" -gt 0 ] && [ "$ef_bekleyen_toplam" -eq 0 ]; then
    hata "KARAR VEREMEDİ: dosyada $bekleyen bekleyen göç var, EF hiçbirini görmüyor."
    hata "İki olası sebep: (a) göç dosyasında [Migration] niteliği yok,"
    hata "(b) kopya bayat ve o göç kopyada zaten uygulanmış görünüyor."
    hata "Her iki hâlde de prova hiçbir şeyi sınamadı."
    comm -23 "$kaynak" "$gecmis" | sed 's/^/           /' >&2
    sudo -u postgres dropdb --if-exists "$PROVA_DB" >/dev/null 2>&1
    exit 2
fi

for baglam in "${BAGLAMLAR[@]}"; do
    log "Göç kopyaya uygulanıyor — bağlam: $baglam"
    cikti="$(mktemp)"

    once="$(mktemp)"; cp "${ONCE_DOSYA[$baglam]}" "$once"

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
            "$EF_ARACI" database update --no-build \
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

        # ═══════════════════════════════════════════════════════════
        #  UYGULAMA KANITI — DURUM DEĞİL, FARK
        # ═══════════════════════════════════════════════════════════
        #
        # Eski hâli DURUM ölçüyordu: "bekleyen göçler kopyanın
        # geçmişinde mi?" Bu soru, göç KOPYAYA ZATEN UYGULANMIŞSA da
        # "evet" der. Yani bayat bir kopyada hiçbir şey uygulanmadan
        # prova YEŞİL verirdi — provanın verebileceği en tehlikeli
        # yanlış güvence, çünkü kırmızı değil yeşildir.
        #
        # Artık FARK ölçülüyor: aynı bağlam, aynı bağlantı, aynı
        # derlenmiş ikili; önce ve sonra okunur, aradaki fark
        # bekleyen kümeyle BİREBİR eşleşmek zorundadır.
        sonra="$(mktemp)"
        if ! ef_liste "$baglam" "$sonra"; then
            hata "KARAR VEREMEDİ ($baglam): uygulama sonrası liste okunamadı."
            rm -f "$cikti" "$once" "$sonra"
            sudo -u postgres dropdb --if-exists "$PROVA_DB" >/dev/null 2>&1
            exit 2
        fi

        bekleyen_kume="$(mktemp)"; fark_kume="$(mktemp)"
        grep 'H$' "$once"  | cut -f1 | sort > "$bekleyen_kume"
        # fark = sonra'da uygulanmış olup önce'de uygulanmamış olanlar
        comm -13 <(grep 'E$' "$once" | cut -f1 | sort) \
                 <(grep 'E$' "$sonra" | cut -f1 | sort) > "$fark_kume"
        f_say="$(grep -c . "$fark_kume" || true)"
        b_say="$(grep -c . "$bekleyen_kume" || true)"
        log "  FARK ($baglam): $f_say göç eklendi · beklenen $b_say"

        if [ "$b_say" -gt 0 ] && [ "$f_say" -eq 0 ]; then
            hata "PROVA GEÇERSİZ ($baglam): FARK BOŞ."
            hata "$b_say göç bekliyordu, kopyanın geçmişine HİÇBİRİ eklenmedi."
            hata "Hiçbir şey uygulanmadıysa hiçbir şey prova edilmemiştir."
            grep . "$bekleyen_kume" | sed 's/^/           uygulanmayan: /' >&2
            rm -f "$cikti" "$once" "$sonra" "$bekleyen_kume" "$fark_kume"
            sudo -u postgres dropdb --if-exists "$PROVA_DB" >/dev/null 2>&1
            exit 2
        fi

        if ! diff -q "$bekleyen_kume" "$fark_kume" >/dev/null; then
            hata "PROVA GEÇERSİZ ($baglam): fark, bekleyen kümeyle EŞLEŞMİYOR."
            comm -23 "$bekleyen_kume" "$fark_kume" | sed 's/^/           uygulanmadı: /' >&2
            comm -13 "$bekleyen_kume" "$fark_kume" | sed 's/^/           beklenmeden uygulandı: /' >&2
            rm -f "$cikti" "$once" "$sonra" "$bekleyen_kume" "$fark_kume"
            sudo -u postgres dropdb --if-exists "$PROVA_DB" >/dev/null 2>&1
            exit 2
        fi

        [ "$f_say" -gt 0 ] && \
            log "  UYGULAMA KANITI ($baglam): fark = bekleyen küme · EŞLEŞTİ"
        rm -f "$once" "$sonra" "$bekleyen_kume" "$fark_kume"
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

        #  (0) BAĞLANTI SINIFI HATA — KIRMIZI DEĞİL, KARAR VEREMEDİ.
        #
        # SONDA C ORTAYA ÇIKARDI: yanlış parolayla koşulan prova
        # `28P01 password authentication failed` aldı ve kapı bunu
        # "PostgreSQL hatası (SQLSTATE)" sayıp **"bu göç canlıda da
        # patlardı"** dedi. Göçün kendisi hiç çalışmadı; araç
        # veritabanına bağlanamadı bile. Bu, betiğin aşağıda ilan
        # ettiği asimetri ilkesinin doğrudan ihlaliydi: kırmızı
        # POZİTİF kanıt ister, "bir SQLSTATE gördüm" o kanıt değildir.
        #
        # Bağlantı sınıfı SQLSTATE'ler göçe ulaşılamadığını söyler:
        #   08xxx bağlantı istisnası · 28xxx yetkilendirme reddi
        #   3D000 veritabanı yok    · 3F000 şema yok
        #   53xxx kaynak yetersiz   · 57P03 sunucu hazır değil
        if grep -qE "\b(08[0-9A-Z]{3}|28[0-9A-Z]{3}|3D000|3F000|53[0-9A-Z]{3}|57P03):" "$cikti" \
           && ! grep -q "Applying migration" "$cikti"; then
            hata "KARAR VEREMEDİ ($baglam): veritabanına BAĞLANILAMADI."
            hata "Göç hiç çalışmadı — bu göç hakkında hüküm verilemez."
            hata "Bağlantı/yetki kurulumunu düzeltip provayı tekrar koşun."
            grep -E "\b(08[0-9A-Z]{3}|28[0-9A-Z]{3}|3D000|3F000|53[0-9A-Z]{3}|57P03):" "$cikti" \
                | head -3 | sed 's/^/           /' >&2
            rm -f "$cikti"
            sudo -u postgres dropdb --if-exists "$PROVA_DB" >/dev/null 2>&1
            log "Kopya silindi."
            exit 2
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
# (Eski DURUM ölçen blok buradan kaldırıldı — yerini döngü içindeki
# FARK ölçümü aldı. Durum ölçümü bayat kopyada yeşil veriyordu.)

[ "$sonuc" -eq 0 ] && log "PROVA GEÇTİ — iki bağlam da canlının kopyasında sorunsuz uygulandı."

for _b in "${BAGLAMLAR[@]}"; do rm -f "${ONCE_DOSYA[$_b]}"; done

# ── 5) Kopya silinir ──
sudo -u postgres psql -tAc "select pg_terminate_backend(pid) from pg_stat_activity where datname='$PROVA_DB';" >/dev/null 2>&1
sudo -u postgres dropdb --if-exists "$PROVA_DB" >/dev/null 2>&1
log "Kopya silindi."

exit $sonuc
