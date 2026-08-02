using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class addEngineerşngReferencesToOFFeritems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EngineeringPositionId",
                table: "offer_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EngineeringRecipeId",
                table: "offer_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecipeVersion",
                table: "offer_items",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_offer_items_EngineeringPositionId",
                table: "offer_items",
                column: "EngineeringPositionId");

            migrationBuilder.AddForeignKey(
                name: "FK_offer_items_engineering_positions_EngineeringPositionId",
                table: "offer_items",
                column: "EngineeringPositionId",
                principalTable: "engineering_positions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_offer_items_engineering_positions_EngineeringPositionId",
                table: "offer_items");

            migrationBuilder.DropIndex(
                name: "IX_offer_items_EngineeringPositionId",
                table: "offer_items");

            migrationBuilder.DropColumn(
                name: "EngineeringPositionId",
                table: "offer_items");

            migrationBuilder.DropColumn(
                name: "EngineeringRecipeId",
                table: "offer_items");

            migrationBuilder.DropColumn(
                name: "RecipeVersion",
                table: "offer_items");
        }
    }
}
