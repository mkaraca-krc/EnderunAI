using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStockedSaleCostTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "sales_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryItemId",
                table: "sales_invoice_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LineCost",
                table: "sales_invoice_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCostAtSale",
                table: "sales_invoice_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AccountingVoucherId",
                table: "retail_sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LineCost",
                table: "retail_sale_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCostAtSale",
                table: "retail_sale_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_invoices_WarehouseId",
                table: "sales_invoices",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_invoice_items_InventoryItemId",
                table: "sales_invoice_items",
                column: "InventoryItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_sales_invoice_items_inventory_items_InventoryItemId",
                table: "sales_invoice_items",
                column: "InventoryItemId",
                principalTable: "inventory_items",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_sales_invoices_warehouses_WarehouseId",
                table: "sales_invoices",
                column: "WarehouseId",
                principalTable: "warehouses",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sales_invoice_items_inventory_items_InventoryItemId",
                table: "sales_invoice_items");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_invoices_warehouses_WarehouseId",
                table: "sales_invoices");

            migrationBuilder.DropIndex(
                name: "IX_sales_invoices_WarehouseId",
                table: "sales_invoices");

            migrationBuilder.DropIndex(
                name: "IX_sales_invoice_items_InventoryItemId",
                table: "sales_invoice_items");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "InventoryItemId",
                table: "sales_invoice_items");

            migrationBuilder.DropColumn(
                name: "LineCost",
                table: "sales_invoice_items");

            migrationBuilder.DropColumn(
                name: "UnitCostAtSale",
                table: "sales_invoice_items");

            migrationBuilder.DropColumn(
                name: "AccountingVoucherId",
                table: "retail_sales");

            migrationBuilder.DropColumn(
                name: "LineCost",
                table: "retail_sale_items");

            migrationBuilder.DropColumn(
                name: "UnitCostAtSale",
                table: "retail_sale_items");
        }
    }
}
