using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChainRequestedBrand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BrandIrrelevant",
                table: "rfq_items",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedBrand",
                table: "rfq_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BrandIrrelevant",
                table: "purchase_order_items",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedBrand",
                table: "purchase_order_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrandIrrelevant",
                table: "rfq_items");

            migrationBuilder.DropColumn(
                name: "RequestedBrand",
                table: "rfq_items");

            migrationBuilder.DropColumn(
                name: "BrandIrrelevant",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "RequestedBrand",
                table: "purchase_order_items");
        }
    }
}
