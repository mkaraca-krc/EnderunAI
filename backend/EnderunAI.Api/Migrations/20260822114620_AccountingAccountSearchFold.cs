using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AccountingAccountSearchFold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchFold",
                table: "accounting_accounts",
                type: "text",
                nullable: false,
                computedColumnSql: "translate(lower(\"Code\" || ' ' || \"Name\"), 'ışğüöçâîû', 'isguocaiu')",
                stored: true);

            /*
             * TRİGRAM İNDEKSİ — "içinde geçen" araması için.
             *
             * ÖLÇÜLDÜ (canlı, 1.114 satır): katlamayı satır satır
             * hesaplayan sıralı tarama 5,0 ms. Bugün taşınır ama seçicide
             * HER TUŞTA çalışıyor ve hesap planı büyüdükçe doğrusal
             * artıyor. Üretilmiş kolon katlamayı yazma zamanına aldı;
             * trigram indeksi de '%metin%' aramasını indeksten
             * karşılıyor — B-tree bunu yapamaz, yalnız önek aramasını
             * hızlandırır.
             *
             * pg_trgm canlıda ZATEN KURULU (ölçüldü); yine de
             * IF NOT EXISTS ile güvenceye alınıyor.
             */
            migrationBuilder.Sql(
                "CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql(
                """
                CREATE INDEX "IX_accounting_accounts_arama"
                ON accounting_accounts USING gin ("SearchFold" gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // GERİ ALINABİLİR: indeks önce düşer, sonra kolon.
            // Ters sırada Postgres kolonu indekse bağlı olduğu için
            // reddeder. Eklenti BIRAKILIYOR — başka nesneler
            // kullanıyor olabilir, silmek onları kırardı.
            migrationBuilder.Sql(
                """DROP INDEX IF EXISTS "IX_accounting_accounts_arama";""");

            migrationBuilder.DropColumn(
                name: "SearchFold",
                table: "accounting_accounts");
        }
    }
}
