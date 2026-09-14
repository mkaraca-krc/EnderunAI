# CANLIYA GERİ YÜKLEME — FELAKET ANI YORDAMI

**Bu belge, veritabanı kaybedildiğinde açılacak belgedir.** Betik:
`scripts/enderun-kurtarma.sh`. Betik çalışmıyorsa aşağıdaki elle
adımlar aynı işi yapar.

---

## 0 — ÖNCE DUR, SORUYU CEVAPLA

**Ne kayboldu?** Üç hâl, üç farklı yol:

| hâl | yapılacak |
|---|---|
| Veri bozuldu / yanlış silindi, veritabanı ayakta | geri yükleme (bu belge) |
| Veritabanı düşmüş ama diskler sağlam | önce `systemctl status postgresql`, geri yükleme SON çare |
| Sunucu tamamen gitti | **yedekler de gitti** — sunucu dışı kopya YOK (BEKLEYEN KARARLAR) |

**Geri yükleme, verinin yedek anına DÖNMESİ demektir.** Yedekten sonra
girilen her kayıt kaybolur. Kayıp kabul edilebilir değilse önce durun.

---

## 1 — HANGİ YEDEK

    ls -lt /var/backups/enderun/db_*.dump.gpg | head -5
    cat /var/lib/enderun-ai/tatbikat-son-basari.txt   # en son NE ZAMAN geri yükleyebildik

Her yedeğin yanında `db_<damga>.satirlar.txt` vardır: o anın tablo/satır
sayıları. **Damgası olmayan yedek yüklenmez** — doğrulayacak şey yoktur.

Gece yedeği 03:00, tatbikat 03:33. Son tatbikat başarısının yaşı,
"en son ne zaman gerçekten geri yükleyebildik"in cevabıdır.

---

## 2 — BETİKLE (tercih edilen)

    # önce PROVA — canlıya dokunmaz, seçtiğiniz yedeğin sağlamlığını gösterir
    scripts/enderun-kurtarma.sh \
        --yedek /var/backups/enderun/db_YYYYAAGG_SSDDss.dump.gpg \
        --hedef enderun_kurtarma_provasi

    # sonra CANLI — iki bağımsız onay şart
    KURTARMA_ONAYI="EVET-enderun_ai-GERI-YUKLE" \
    scripts/enderun-kurtarma.sh \
        --yedek /var/backups/enderun/db_YYYYAAGG_SSDDss.dump.gpg \
        --hedef enderun_ai

Betiğin yaptıkları, sırayla:

1. **ön denetim** — yedek + damgası var mı, çift eşleşiyor mu, dosya
   açılıyor mu (`PGDMP`), disk yeter mi
2. **geri dönüş kopyası** — canlının ŞU ANKİ hâli şifreli dökülür ve
   açıldığı doğrulanır. *Alınamazsa canlıya dokunulmaz.* Seçilen yedek
   yanlış çıkarsa dönülecek tek yer budur.
3. servisleri durdur (`enderunai-backend`, `enderunai-frontend`)
4. bağlantıları kes, veritabanını düşür ve `enderun_user` sahipliğiyle kur
5. **yükle** — `gpg --decrypt | pg_restore --role=enderun_user`, düz ara
   dosya yok, iki ucun çıkış kodu da denetlenir
6. **doğrulama 1 — satırlar:** 242 tablonun sayıları damgayla TAM eşleşmeli
7. **doğrulama 2 — göç geçmişi:** `__EFMigrationsHistory` ile koddaki göç
   dosyaları karşılaştırılır. Yedek koddan eskiyse bekleyen göçler
   listelenir → `deploy/scripts/goc-uygula.sh`
8. **doğrulama 3 — sahiplik:** `public` şemadaki her tablo `enderun_user`
   sahipliğinde olmalı
9. servisleri kaldır + `deploy/scripts/healthcheck.sh`

**Doğrulamaların herhangi biri düşerse servisler KALDIRILMAZ** ve geri
dönüş kopyasının yolu günlüğe yazılır.

---

## 3 — ELLE (betik yoksa)

    # 1) geri dönüş kopyası — ATLAMAYIN
    sudo -u postgres pg_dump -d enderun_ai -F c \
      | gpg --batch --symmetric --cipher-algo AES256 \
            --passphrase-file /etc/enderunai/backup-key \
            --output /var/backups/enderun/kurtarma-oncesi_$(date -u +%Y%m%d_%H%M%S).dump.gpg

    # 2) servisleri durdur
    systemctl stop enderunai-backend enderunai-frontend

    # 3) bağlantıları kes, düşür, kur
    sudo -u postgres psql -d postgres \
      -c "select pg_terminate_backend(pid) from pg_stat_activity
          where datname='enderun_ai' and pid <> pg_backend_pid();" \
      -c 'DROP DATABASE IF EXISTS "enderun_ai";' \
      -c 'CREATE DATABASE "enderun_ai" OWNER "enderun_user";'

    # 4) yükle
    gpg --batch --passphrase-file /etc/enderunai/backup-key \
        --decrypt /var/backups/enderun/db_YYYYAAGG_SSDDss.dump.gpg \
      | sudo -u postgres pg_restore --dbname=enderun_ai \
            --role=enderun_user --no-owner --no-privileges

    # 5) sahiplik denetimi — BOŞ dönmeli
    sudo -u postgres psql -d enderun_ai -c \
      "select tableowner, count(*) from pg_tables
       where schemaname='public' and tableowner <> 'enderun_user' group by 1;"

    # 6) göç geçmişi
    sudo -u postgres psql -d enderun_ai -tAc \
      'select count(*) from "__EFMigrationsHistory";'
    find backend/EnderunAI.Api/Migrations -name '*.cs' \
         ! -name '*.Designer.cs' ! -name '*ModelSnapshot.cs' | wc -l
    # eşit değilse: deploy/scripts/goc-uygula.sh

    # 7) kaldır ve sına
    systemctl start enderunai-backend enderunai-frontend
    deploy/scripts/healthcheck.sh

**`--role=enderun_user` ŞART.** Onsuz yüklenen nesneler `postgres`a ait
olur (2026-09-13 provasında ölçüldü: 242 tablo). Sonradan
`reassign owned by` ile düzeltilemez — PostgreSQL süperkullanıcı
nesnelerinin devrini reddeder.

**Göç dosyaları İKİ klasörde:** `Migrations/` ve
`Migrations/HumanResources/`. Yalnız birine bakan bir sayım yanlış alarm
üretir.

---

## 4 — SONRASI

- Geri dönüş kopyası (`kurtarma-oncesi_*.dump.gpg`) **silinmez**; işler
  oturana kadar durur.
- Yedek anı ile felaket anı arasındaki kayıp **yazıya geçirilir**:
  hangi tablolar, kaç satır, hangi kullanıcılar etkilendi.
- `security_audit_events` geri yüklenen kopyada da vardır; kayıp
  penceresini oradan okuyabilirsiniz.

---

---

# YAYIN GERİ ALMA (kurtarmadan AYRI iş)

Veritabanı kaybı değil, **yeni sürüm arızası** için. Kurtarma yordamı
veriyi geri getirir; bu bölüm KODU ve ŞEMAYI geri alır.

## G1 — KOD: TEK KOMUT

    deploy/scripts/geri-al.sh --prova     # önce bu: ön koşulları sınar
    deploy/scripts/geri-al.sh --uygula    # geri alır

Yaptığı: `publish/` ← `publish-rollback/`, `.next/` ← `frontend-next-rollback/`,
servisleri yeniden başlatır, `healthcheck.sh` koşar. Eski hâli
`*.geri-alinan` olarak bırakır (silmez).

**Ön koşul denetimi "dizin var mı" ile yetinmez**: `EnderunAI.Api.dll`
ve `BUILD_ID` aranır — yarım bir publish de dizin olarak vardır.

## G2 — GÖÇ: AYRI KOMUT, SQL ÖNCEDEN YAZILI

**`geri-al.sh` GÖÇÜ GERİ ALMAZ.** Şema değişikliği içeren bir yayında
kod geri alınır, şema ileride kalır.

**Sabahın körü kimse SQL üretmeye çalışmasın** — 2026-09-14 yayınındaki
tek göç için geri alma SQL'i depoda hazır ve **prova edilmiştir**:

    deploy/geri-alma/20260913144150-geri.sql

Koşma:

    sudo -u postgres psql -d enderun_ai -v ON_ERROR_STOP=1 \
        -f /var/www/enderun-ai/deploy/geri-alma/20260913144150-geri.sql

Yaptığı: `audit_logs` tablosunu üç indeksiyle geri kurar ve göç
geçmişinden satırı siler.

**PROVA SONUCU (2026-09-14, canlı şemadan kopya veritabanında):**

| adım | sonuç |
|---|---|
| ileri | `audit_logs` düştü, tablo 242 → **241**, geçmişe satır eklendi |
| geri | tablo geri geldi, **4 indeks** (canlıyla aynı), geçmişten satır silindi, 242 |
| sütun karşılaştırması | **BİREBİR** |

**Bu göç zararsız tarafta:** `audit_logs` iki aydır yetim — varlık
sınıfı yok, `DbSet` yok, yazan kod yok, 0 satır. Yine de yazıldı:
**kolay adımın yazılmaması, zor adımın yazılmamasını normalleştirir.**

Yeni bir göç için geri alma SQL'i şöyle üretilir:

    cd backend/EnderunAI.Api
    DB_CONNECTION=... dotnet ef migrations script <hedef> <onceki> \
        --context AppDbContext -o ../../deploy/geri-alma/<hedef>-geri.sql

ve **koşulmadan önce prova veritabanında denenir.**

---

## ÖLÇÜLMEMİŞ KALAN — DÜRÜSTÇE

Bu yordamın **3, 9 ve healthcheck adımları canlı hedefte hiç
koşturulmadı** (servis durdurma/kaldırma). Provada bilerek atlanıyorlar;
canlıda denemek için canlıyı düşürmek gerekirdi. Ölçülen: 1, 2, 4, 5, 6,
7, 8 — prova hedefinde uçtan uca, üç doğrulama kapısı da ayrı ayrı
kırmızı yandırılarak.
