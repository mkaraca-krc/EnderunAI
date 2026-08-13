using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class RfqQuotationActiveUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_rfq_supplier_quotations_RfqSupplierId",
                table: "rfq_supplier_quotations");

            migrationBuilder.CreateIndex(
                name: "IX_rfq_supplier_quotations_RfqSupplierId",
                table: "rfq_supplier_quotations",
                column: "RfqSupplierId",
                unique: true,
                filter: "NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_rfq_supplier_quotations_RfqSupplierId",
                table: "rfq_supplier_quotations");

            migrationBuilder.CreateIndex(
                name: "IX_rfq_supplier_quotations_RfqSupplierId",
                table: "rfq_supplier_quotations",
                column: "RfqSupplierId");
        }
    }
}
