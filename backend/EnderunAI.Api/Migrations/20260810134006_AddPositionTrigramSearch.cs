using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <summary>
    /// Poz aramasını trigram benzerliğine taşır.
    ///
    /// Katı LIKE '%terim%' araması 23 binin üzerinde pozda iki şeyi
    /// birden yapamıyordu: kelime sırası farklıysa ("3x2,5 kablo" ↔
    /// "kablo 3x2,5") hiç bulamıyor, küçük bir yazım hatasında da boş
    /// dönüyordu.
    ///
    /// NORMALİZE SÜTUN ÜRETİLMİŞ (GENERATED): Türkçe harfler ve aksan
    /// veritabanı tarafında katlanıyor. Uygulama kodunda
    /// doldurulsaydı, pozu yazan her yol (içe aktarma, özel poz,
    /// düzenleme) bunu tek tek hatırlamak zorunda kalır ve biri
    /// unutulduğunda o poz aramada sessizce kaybolurdu.
    ///
    /// İ/ı AYRIMI: translate önce büyük/küçük Türkçe harfleri ASCII
    /// karşılığına çeviriyor, sonra lower() düşürüyor. Böylece "İ", "I",
    /// "ı" ve "i" aynı harfe iniyor — Türkçe klavyede en sık yapılan
    /// arama hatası bu.
    /// </summary>
    public partial class AddPositionTrigramSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // pg_trgm PostgreSQL 13'ten beri "trusted" eklenti: veritabanı
            // üzerinde CREATE yetkisi olan uygulama kullanıcısı kurabiliyor,
            // süperkullanıcı gerekmiyor.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql("""
                ALTER TABLE engineering_positions
                ADD COLUMN IF NOT EXISTS "SearchNormalized" text
                GENERATED ALWAYS AS (
                    lower(translate(
                        coalesce("Code", '') || ' ' ||
                        coalesce("Name", '') || ' ' ||
                        coalesce("SearchKeywords", '') || ' ' ||
                        coalesce("OfficialCode", '') || ' ' ||
                        coalesce("Category", ''),
                        'ÇĞİIÖŞÜçğıöşüÂÎÛâîû',
                        'CGIIOSUcgiosuAIUaiu'
                    ))
                ) STORED;
                """);

            // GIN + gin_trgm_ops: hem LIKE '%...%' hem de benzerlik
            // operatörlerini (%, <%) hızlandırıyor. İndekssiz her tuş
            // vuruşu 23 bin satırı tarardı.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_engineering_positions_search_trgm"
                ON engineering_positions
                USING gin ("SearchNormalized" gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_engineering_positions_search_trgm";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE engineering_positions
                DROP COLUMN IF EXISTS "SearchNormalized";
                """);

            // Eklenti bilerek KALDIRILMIYOR: başka bir yer kullanıyor
            // olabilir ve düşürmek o indeksleri de götürürdü.
        }
    }
}
