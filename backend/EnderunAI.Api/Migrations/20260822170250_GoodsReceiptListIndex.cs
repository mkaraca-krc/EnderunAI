using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class GoodsReceiptListIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_goods_receipts_liste",
                table: "goods_receipts",
                columns: new[] { "CompanyId", "ReceiptDate", "CreatedAtUtc", "Id" },
                descending: new[] { false, true, true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_goods_receipts_liste",
                table: "goods_receipts");
        }
    }
}
