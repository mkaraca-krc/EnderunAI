using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixPurchaseRequestItemLineNumberIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_purchase_request_items_PurchaseRequestId_LineNumber",
                table: "purchase_request_items");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_request_items_PurchaseRequestId_LineNumber",
                table: "purchase_request_items",
                columns: new[] { "PurchaseRequestId", "LineNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_purchase_request_items_PurchaseRequestId_LineNumber",
                table: "purchase_request_items");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_request_items_PurchaseRequestId_LineNumber",
                table: "purchase_request_items",
                columns: new[] { "PurchaseRequestId", "LineNumber" },
                unique: true);
        }
    }
}
