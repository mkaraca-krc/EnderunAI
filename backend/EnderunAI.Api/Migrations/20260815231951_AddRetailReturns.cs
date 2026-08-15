using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRetailReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReturn",
                table: "retail_sales",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalSaleId",
                table: "retail_sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_retail_sales_OriginalSaleId",
                table: "retail_sales",
                column: "OriginalSaleId");

            migrationBuilder.AddForeignKey(
                name: "FK_retail_sales_retail_sales_OriginalSaleId",
                table: "retail_sales",
                column: "OriginalSaleId",
                principalTable: "retail_sales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_retail_sales_retail_sales_OriginalSaleId",
                table: "retail_sales");

            migrationBuilder.DropIndex(
                name: "IX_retail_sales_OriginalSaleId",
                table: "retail_sales");

            migrationBuilder.DropColumn(
                name: "IsReturn",
                table: "retail_sales");

            migrationBuilder.DropColumn(
                name: "OriginalSaleId",
                table: "retail_sales");
        }
    }
}
