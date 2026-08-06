using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionPriceComponent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_position_unit_prices_EngineeringPositionId_Year_Institution",
                table: "position_unit_prices");

            migrationBuilder.AddColumn<int>(
                name: "Component",
                table: "position_unit_prices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_position_unit_prices_EngineeringPositionId_Year_Institution~",
                table: "position_unit_prices",
                columns: new[] { "EngineeringPositionId", "Year", "Institution", "Component" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_position_unit_prices_EngineeringPositionId_Year_Institution~",
                table: "position_unit_prices");

            migrationBuilder.DropColumn(
                name: "Component",
                table: "position_unit_prices");

            migrationBuilder.CreateIndex(
                name: "IX_position_unit_prices_EngineeringPositionId_Year_Institution",
                table: "position_unit_prices",
                columns: new[] { "EngineeringPositionId", "Year", "Institution" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }
    }
}
