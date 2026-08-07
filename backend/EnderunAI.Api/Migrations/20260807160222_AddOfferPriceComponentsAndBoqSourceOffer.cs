using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferPriceComponentsAndBoqSourceOffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceOfferId",
                table: "project_boqs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LaborUnitPrice",
                table: "offer_items",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaterialUnitPrice",
                table: "offer_items",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OverheadUnitPrice",
                table: "offer_items",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            // GERİYE DOLDURMA: mevcut tekliflerde bileşen ayrımı yoktu.
            // Modelin kuralı "bileşen girilmemiş kalemde tutarın tamamı
            // malzemedir"; bunu veriye de yazıyoruz ki icmale aktarım ve
            // raporlar hesaplama yapmadan doğru dağılımı okusun. Toplam
            // değişmiyor.
            migrationBuilder.Sql(@"
                UPDATE offer_items
                SET ""MaterialUnitPrice"" = ""UnitSalesPrice""
                WHERE ""MaterialUnitPrice"" = 0
                  AND ""LaborUnitPrice"" = 0
                  AND ""OverheadUnitPrice"" = 0;");

            migrationBuilder.CreateIndex(
                name: "IX_project_boqs_SourceOfferId",
                table: "project_boqs",
                column: "SourceOfferId");

            migrationBuilder.AddForeignKey(
                name: "FK_project_boqs_offers_SourceOfferId",
                table: "project_boqs",
                column: "SourceOfferId",
                principalTable: "offers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_project_boqs_offers_SourceOfferId",
                table: "project_boqs");

            migrationBuilder.DropIndex(
                name: "IX_project_boqs_SourceOfferId",
                table: "project_boqs");

            migrationBuilder.DropColumn(
                name: "SourceOfferId",
                table: "project_boqs");

            migrationBuilder.DropColumn(
                name: "LaborUnitPrice",
                table: "offer_items");

            migrationBuilder.DropColumn(
                name: "MaterialUnitPrice",
                table: "offer_items");

            migrationBuilder.DropColumn(
                name: "OverheadUnitPrice",
                table: "offer_items");
        }
    }
}
