using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrentAccountAccountingLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InventoryItemId",
                table: "purchase_request_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IssuedQuantity",
                table: "purchase_request_items",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReservedQuantity",
                table: "purchase_request_items",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "PayableAccountingAccountId",
                table: "current_accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceivableAccountingAccountId",
                table: "current_accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "stock_reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseRequestItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ConsumedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ReservationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_reservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_reservations_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_reservations_inventory_items_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "inventory_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_reservations_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_reservations_purchase_request_items_PurchaseRequestIt~",
                        column: x => x.PurchaseRequestItemId,
                        principalTable: "purchase_request_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_reservations_purchase_requests_PurchaseRequestId",
                        column: x => x.PurchaseRequestId,
                        principalTable: "purchase_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_reservations_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_request_items_InventoryItemId",
                table: "purchase_request_items",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_current_accounts_PayableAccountingAccountId",
                table: "current_accounts",
                column: "PayableAccountingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_current_accounts_ReceivableAccountingAccountId",
                table: "current_accounts",
                column: "ReceivableAccountingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_CompanyId",
                table: "stock_reservations",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_InventoryItemId",
                table: "stock_reservations",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_ProjectId",
                table: "stock_reservations",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_PurchaseRequestId_PurchaseRequestItemId",
                table: "stock_reservations",
                columns: new[] { "PurchaseRequestId", "PurchaseRequestItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_PurchaseRequestItemId",
                table: "stock_reservations",
                column: "PurchaseRequestItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_ReservationNumber",
                table: "stock_reservations",
                column: "ReservationNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_WarehouseId_InventoryItemId_Status",
                table: "stock_reservations",
                columns: new[] { "WarehouseId", "InventoryItemId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_current_accounts_accounting_accounts_PayableAccountingAccou~",
                table: "current_accounts",
                column: "PayableAccountingAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_current_accounts_accounting_accounts_ReceivableAccountingAc~",
                table: "current_accounts",
                column: "ReceivableAccountingAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_request_items_inventory_items_InventoryItemId",
                table: "purchase_request_items",
                column: "InventoryItemId",
                principalTable: "inventory_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_current_accounts_accounting_accounts_PayableAccountingAccou~",
                table: "current_accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_current_accounts_accounting_accounts_ReceivableAccountingAc~",
                table: "current_accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_purchase_request_items_inventory_items_InventoryItemId",
                table: "purchase_request_items");

            migrationBuilder.DropTable(
                name: "stock_reservations");

            migrationBuilder.DropIndex(
                name: "IX_purchase_request_items_InventoryItemId",
                table: "purchase_request_items");

            migrationBuilder.DropIndex(
                name: "IX_current_accounts_PayableAccountingAccountId",
                table: "current_accounts");

            migrationBuilder.DropIndex(
                name: "IX_current_accounts_ReceivableAccountingAccountId",
                table: "current_accounts");

            migrationBuilder.DropColumn(
                name: "InventoryItemId",
                table: "purchase_request_items");

            migrationBuilder.DropColumn(
                name: "IssuedQuantity",
                table: "purchase_request_items");

            migrationBuilder.DropColumn(
                name: "ReservedQuantity",
                table: "purchase_request_items");

            migrationBuilder.DropColumn(
                name: "PayableAccountingAccountId",
                table: "current_accounts");

            migrationBuilder.DropColumn(
                name: "ReceivableAccountingAccountId",
                table: "current_accounts");
        }
    }
}
