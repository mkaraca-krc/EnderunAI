using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryAttributeSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttributeSignature",
                table: "inventory_items",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_CompanyId_AttributeSignature",
                table: "inventory_items",
                columns: new[] { "CompanyId", "AttributeSignature" },
                unique: true,
                filter: "\"AttributeSignature\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_inventory_items_CompanyId_AttributeSignature",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "AttributeSignature",
                table: "inventory_items");
        }
    }
}
