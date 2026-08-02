using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class LinkHrAssetsToInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InventoryItemId",
                table: "hr_asset_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InventoryQuantity",
                table: "hr_asset_assignments",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IssueStockMovementId",
                table: "hr_asset_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IssuedUnitCost",
                table: "hr_asset_assignments",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReturnStockMovementId",
                table: "hr_asset_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "hr_asset_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_hr_asset_assignments_InventoryItemId",
                table: "hr_asset_assignments",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_hr_asset_assignments_IssueStockMovementId",
                table: "hr_asset_assignments",
                column: "IssueStockMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_hr_asset_assignments_ReturnStockMovementId",
                table: "hr_asset_assignments",
                column: "ReturnStockMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_hr_asset_assignments_WarehouseId",
                table: "hr_asset_assignments",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_hr_asset_assignments_inventory_items_InventoryItemId",
                table: "hr_asset_assignments",
                column: "InventoryItemId",
                principalTable: "inventory_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_hr_asset_assignments_stock_movements_IssueStockMovementId",
                table: "hr_asset_assignments",
                column: "IssueStockMovementId",
                principalTable: "stock_movements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_hr_asset_assignments_stock_movements_ReturnStockMovementId",
                table: "hr_asset_assignments",
                column: "ReturnStockMovementId",
                principalTable: "stock_movements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_hr_asset_assignments_warehouses_WarehouseId",
                table: "hr_asset_assignments",
                column: "WarehouseId",
                principalTable: "warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_hr_asset_assignments_inventory_items_InventoryItemId",
                table: "hr_asset_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_hr_asset_assignments_stock_movements_IssueStockMovementId",
                table: "hr_asset_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_hr_asset_assignments_stock_movements_ReturnStockMovementId",
                table: "hr_asset_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_hr_asset_assignments_warehouses_WarehouseId",
                table: "hr_asset_assignments");

            migrationBuilder.DropIndex(
                name: "IX_hr_asset_assignments_InventoryItemId",
                table: "hr_asset_assignments");

            migrationBuilder.DropIndex(
                name: "IX_hr_asset_assignments_IssueStockMovementId",
                table: "hr_asset_assignments");

            migrationBuilder.DropIndex(
                name: "IX_hr_asset_assignments_ReturnStockMovementId",
                table: "hr_asset_assignments");

            migrationBuilder.DropIndex(
                name: "IX_hr_asset_assignments_WarehouseId",
                table: "hr_asset_assignments");

            migrationBuilder.DropColumn(
                name: "InventoryItemId",
                table: "hr_asset_assignments");

            migrationBuilder.DropColumn(
                name: "InventoryQuantity",
                table: "hr_asset_assignments");

            migrationBuilder.DropColumn(
                name: "IssueStockMovementId",
                table: "hr_asset_assignments");

            migrationBuilder.DropColumn(
                name: "IssuedUnitCost",
                table: "hr_asset_assignments");

            migrationBuilder.DropColumn(
                name: "ReturnStockMovementId",
                table: "hr_asset_assignments");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "hr_asset_assignments");
        }
    }
}
