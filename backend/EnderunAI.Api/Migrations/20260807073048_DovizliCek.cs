using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class DovizliCek : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AmountTry",
                table: "cheques",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "cheques",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            // Mevcut çeklerin tamamı TL; kuru 1, TL karşılığı da kendi
            // tutarı. Kolonlar 0 varsayılanıyla eklendiği için geri
            // doldurulmazsa TL çekler defterde sıfır değerli görünürdü.
            //
            // Dövizli çek zaten hiç girilmemişti (girilseydi sabit 1
            // kuruyla yanlış defterlenmiş olacaktı); bu yüzden koşulsuz
            // 1 atamak güvenli.
            migrationBuilder.Sql(@"
                UPDATE cheques
                SET ""ExchangeRate"" = 1,
                    ""AmountTry"" = ""Amount""
                WHERE ""ExchangeRate"" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmountTry",
                table: "cheques");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "cheques");
        }
    }
}
