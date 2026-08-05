using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierInvoiceTypeAndStockLinks : Migration
    {
        /// <summary>
        /// Alış/gider fatura tipi, kalem bazında stok kartı + depo
        /// (alış) ve gider hesabı + masraf merkezi (gider).
        ///
        /// SupplierInvoice.ProjectId ZORUNLUDAN OPSİYONELE dönüyor:
        /// ofis elektriği, kira, müşavirlik gibi giderlerin gerçekten
        /// projesi yoktur. Zorunlu kalsaydı kullanıcı bunları rastgele
        /// bir projeye yazmak zorunda kalır ve o projenin maliyeti
        /// olduğundan yüksek görünürdü.
        ///
        /// Mevcut faturalar InvoiceType = 0 (Alış) ile açılır ve
        /// bugünkü davranışlarını korur.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectId",
                table: "supplier_invoices",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "CostCenterCode",
                table: "supplier_invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceType",
                table: "supplier_invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "supplier_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostCenterCode",
                table: "supplier_invoice_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExpenseAccountId",
                table: "supplier_invoice_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryItemId",
                table: "supplier_invoice_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "supplier_invoice_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryAccountId",
                table: "company_finance_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoices_WarehouseId",
                table: "supplier_invoices",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_items_ExpenseAccountId",
                table: "supplier_invoice_items",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_items_InventoryItemId",
                table: "supplier_invoice_items",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_items_WarehouseId",
                table: "supplier_invoice_items",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_InventoryAccountId",
                table: "company_finance_settings",
                column: "InventoryAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_InventoryAccou~",
                table: "company_finance_settings",
                column: "InventoryAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_invoice_items_accounting_accounts_ExpenseAccountId",
                table: "supplier_invoice_items",
                column: "ExpenseAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_invoice_items_inventory_items_InventoryItemId",
                table: "supplier_invoice_items",
                column: "InventoryItemId",
                principalTable: "inventory_items",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_invoice_items_warehouses_WarehouseId",
                table: "supplier_invoice_items",
                column: "WarehouseId",
                principalTable: "warehouses",
                principalColumn: "Id");

            // Stok hesabı varsayılanı: 153 Ticari Mallar. Ayar
            // yapılmamış şirkette alış faturası onayı kilitlenmesin
            // diye şimdiden dolduruluyor; kullanıcı ayarlardan
            // değiştirebilir.
            migrationBuilder.Sql("""
                UPDATE company_finance_settings s
                SET "InventoryAccountId" = a."Id"
                FROM accounting_accounts a
                WHERE a."CompanyId" = s."CompanyId"
                  AND a."Code" = '153'
                  AND a."IsDeleted" = false
                  AND s."InventoryAccountId" IS NULL;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_invoices_warehouses_WarehouseId",
                table: "supplier_invoices",
                column: "WarehouseId",
                principalTable: "warehouses",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_InventoryAccou~",
                table: "company_finance_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_supplier_invoice_items_accounting_accounts_ExpenseAccountId",
                table: "supplier_invoice_items");

            migrationBuilder.DropForeignKey(
                name: "FK_supplier_invoice_items_inventory_items_InventoryItemId",
                table: "supplier_invoice_items");

            migrationBuilder.DropForeignKey(
                name: "FK_supplier_invoice_items_warehouses_WarehouseId",
                table: "supplier_invoice_items");

            migrationBuilder.DropForeignKey(
                name: "FK_supplier_invoices_warehouses_WarehouseId",
                table: "supplier_invoices");

            migrationBuilder.DropIndex(
                name: "IX_supplier_invoices_WarehouseId",
                table: "supplier_invoices");

            migrationBuilder.DropIndex(
                name: "IX_supplier_invoice_items_ExpenseAccountId",
                table: "supplier_invoice_items");

            migrationBuilder.DropIndex(
                name: "IX_supplier_invoice_items_InventoryItemId",
                table: "supplier_invoice_items");

            migrationBuilder.DropIndex(
                name: "IX_supplier_invoice_items_WarehouseId",
                table: "supplier_invoice_items");

            migrationBuilder.DropIndex(
                name: "IX_company_finance_settings_InventoryAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropColumn(
                name: "CostCenterCode",
                table: "supplier_invoices");

            migrationBuilder.DropColumn(
                name: "InvoiceType",
                table: "supplier_invoices");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "supplier_invoices");

            migrationBuilder.DropColumn(
                name: "CostCenterCode",
                table: "supplier_invoice_items");

            migrationBuilder.DropColumn(
                name: "ExpenseAccountId",
                table: "supplier_invoice_items");

            migrationBuilder.DropColumn(
                name: "InventoryItemId",
                table: "supplier_invoice_items");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "supplier_invoice_items");

            migrationBuilder.DropColumn(
                name: "InventoryAccountId",
                table: "company_finance_settings");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectId",
                table: "supplier_invoices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
