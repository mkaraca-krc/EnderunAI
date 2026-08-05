using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <summary>
    /// Stok kartına eksik alanlar (son alış fiyatı, tercih edilen
    /// tedarikçi, KDV oranı, açıklama, görsel) ve satın alma
    /// kalemlerinin stok kartı bağı.
    ///
    /// ŞEMA KAYMASI NOTU: <c>purchase_request_items.InventoryItemId</c>
    /// kolonu, indeksi ve yabancı anahtarı veritabanında ZATEN VARDI —
    /// 20260724083553_AddCurrentAccountAccountingLinks göçü eklemişti.
    /// Ama <c>PurchaseRequestItem</c> modelinden bir noktada göç
    /// yazılmadan kaldırılmış, model ile şema birbirinden ayrılmıştı.
    /// EF bu yüzden kolonu yeniden oluşturmaya kalkıyor ve göç
    /// "column already exists" ile patlıyordu. O tabloya ait üretimler
    /// buradan çıkarıldı; model yeniden tanımlandığı için anlık görüntü
    /// (snapshot) bundan sonra doğru.
    /// </summary>
    public partial class AddInventoryItemDetailsAndPurchaseStockLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InventoryItemId",
                table: "purchase_order_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "inventory_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImagePath",
                table: "inventory_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPurchaseDate",
                table: "inventory_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LastPurchasePrice",
                table: "inventory_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreferredSupplierCurrentAccountId",
                table: "inventory_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "inventory_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_items_InventoryItemId",
                table: "purchase_order_items",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_PreferredSupplierCurrentAccountId",
                table: "inventory_items",
                column: "PreferredSupplierCurrentAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_items_current_accounts_PreferredSupplierCurrentAc~",
                table: "inventory_items",
                column: "PreferredSupplierCurrentAccountId",
                principalTable: "current_accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_order_items_inventory_items_InventoryItemId",
                table: "purchase_order_items",
                column: "InventoryItemId",
                principalTable: "inventory_items",
                principalColumn: "Id");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_inventory_items_current_accounts_PreferredSupplierCurrentAc~",
                table: "inventory_items");

            migrationBuilder.DropForeignKey(
                name: "FK_purchase_order_items_inventory_items_InventoryItemId",
                table: "purchase_order_items");

            migrationBuilder.DropIndex(
                name: "IX_purchase_order_items_InventoryItemId",
                table: "purchase_order_items");

            migrationBuilder.DropIndex(
                name: "IX_inventory_items_PreferredSupplierCurrentAccountId",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "InventoryItemId",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "ImagePath",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "LastPurchaseDate",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "LastPurchasePrice",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "PreferredSupplierCurrentAccountId",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "inventory_items");
        }
    }
}
