using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseIntegrationFaz1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GoodsReceiptId",
                table: "stock_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectSiteId",
                table: "stock_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalCost",
                table: "stock_movements",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "stock_movements",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AverageUnitCost",
                table: "inventory_items",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_GoodsReceiptId",
                table: "stock_movements",
                column: "GoodsReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ProjectSiteId",
                table: "stock_movements",
                column: "ProjectSiteId");

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_goods_receipts_GoodsReceiptId",
                table: "stock_movements",
                column: "GoodsReceiptId",
                principalTable: "goods_receipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_project_sites_ProjectSiteId",
                table: "stock_movements",
                column: "ProjectSiteId",
                principalTable: "project_sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_goods_receipts_GoodsReceiptId",
                table: "stock_movements");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_project_sites_ProjectSiteId",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_GoodsReceiptId",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_ProjectSiteId",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "GoodsReceiptId",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "ProjectSiteId",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "TotalCost",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "AverageUnitCost",
                table: "inventory_items");
        }
    }
}
