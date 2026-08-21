using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class ChequeUniquenessExcludingVoided : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedChequeNumber",
                table: "cheques",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            /*
             * MEVCUT KAYITLAR DOLDURULUYOR.
             *
             * Boşluklar (aradakiler dahil) atılıp büyük harfe çevriliyor;
             * BAŞTAKİ SIFIRLAR KORUNUYOR — "0012345" ile "12345" farklı
             * çeklerdir ve birini diğerine indirgemek iki ayrı çeki tek
             * çek sanmaya yol açardı.
             *
             * Uygulamadaki `Cheque.NormalizeChequeNumber` ile AYNI kural.
             */
            migrationBuilder.Sql(
                """
                UPDATE cheques
                SET "NormalizedChequeNumber" =
                    upper(regexp_replace("ChequeNumber", '\s', '', 'g'));
                """);

            /*
             * BACKFILL DOĞRULAMASI — SESSİZ BOŞLUK BIRAKILMAZ.
             *
             * Yukarıdaki UPDATE bir satırı atlarsa o kayıt normalize
             * değeri BOŞ kalır; kısmi tekil indeks o satırları "aynı
             * boş değer" sanıp ilkinden sonrasını reddeder ya da daha
             * kötüsü, boş kalan kayıtlar mükerrer kontrolünün dışında
             * kalır. İkisi de sessizce olur. Bu yüzden migration
             * BURADA DURUYOR ve nedenini söylüyor.
             */
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE eksik integer;
                BEGIN
                    SELECT count(*) INTO eksik FROM cheques
                    WHERE "NormalizedChequeNumber" IS NULL
                       OR btrim("NormalizedChequeNumber") = '';

                    IF eksik > 0 THEN
                        RAISE EXCEPTION
                            'Normalize çek numarası % kayıtta boş kaldı; indeks kurulmadı.',
                            eksik;
                    END IF;
                END $$;
                """);

            /*
             * KISMİ TEKİL İNDEKS — asıl mükerrer engeli.
             *
             * ANAHTAR: şirket + yön + banka + şube + normalize çek no.
             *
             * KEŞİDECİ ANAHTARDA YOK (kullanıcı kararı). Türkiye'de çek
             * numarası banka ve şube bazında zaten tekildir; keşideciyi
             * anahtara koymak kısıtı GEVŞETİRDİ: aynı çek, keşideci
             * alanı farklı yazılarak (biri boş, biri dolu, ya da unvan
             * farklı yazılmış) ikinci kez girilebilir ve sistem
             * yakalayamazdı. Canlıda keşideci 21 çekin yalnız 4'ünde
             * dolu olduğu için bu risk küçük değil, büyüktü.
             *
             * Karar ölçümle alındı: banka + şube + normalize no bazında
             * çakışan AKTİF kayıt sayısı SIFIR.
             *
             * WHERE "Status" <> 90 AND "IsDeleted" = false:
             *   İptal edilen çek numarayı BLOKE ETMEZ. Bildirilen hata
             *   buydu — yanlış girilip iptal edilen çek numarası bir
             *   daha kullanılamıyordu.
             *
             * ŞUBE İÇİN COALESCE ŞART: şube NULL olabiliyor ve Postgres
             * tekil indekste NULL'ları ÇAKIŞTIRMAZ; olmasaydı şubesiz
             * çeklerde kısıt sessizce hiç çalışmazdı.
             *
             * upper(btrim(...)): "Ziraat" ile "ZİRAAT " aynı bankadır.
             * Veritabanı kültürü C.UTF-8 (ölçüldü) — upper() burada
             * C# ToUpperInvariant ile aynı davranıyor, Türkçe "i"
             * tuzağı yok.
             */
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "IX_cheques_aktif_benzersizlik"
                ON cheques (
                    "CompanyId",
                    "Direction",
                    upper(btrim("BankName")),
                    coalesce(upper(btrim("BankBranch")), ''),
                    "NormalizedChequeNumber")
                WHERE "Status" <> 90 AND "IsDeleted" = false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // GERİ ALINABİLİR: indeks önce düşer, sonra kolon. Ters
            // sırada denenirse Postgres kolonu indekse bağlı olduğu
            // için reddeder.
            migrationBuilder.Sql(
                """DROP INDEX IF EXISTS "IX_cheques_aktif_benzersizlik";""");

            migrationBuilder.DropColumn(
                name: "NormalizedChequeNumber",
                table: "cheques");
        }
    }
}
