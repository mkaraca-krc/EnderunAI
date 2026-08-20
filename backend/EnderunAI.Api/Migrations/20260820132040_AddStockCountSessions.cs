using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStockCountSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StockCountShortageAccountId",
                table: "company_finance_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StockCountSurplusAccountId",
                table: "company_finance_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "stock_count_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CountDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecisionReason = table.Column<string>(type: "text", nullable: true),
                    AccountingVoucherId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_stock_count_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_count_sessions_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_stock_count_sessions_warehouse_zones_WarehouseZoneId",
                        column: x => x.WarehouseZoneId,
                        principalTable: "warehouse_zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_count_sessions_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_count_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockCountSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemQuantity = table.Column<decimal>(type: "numeric", nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    UnitCostAtCount = table.Column<decimal>(type: "numeric", nullable: false),
                    VarianceReason = table.Column<int>(type: "integer", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_stock_count_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_count_lines_inventory_items_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "inventory_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_count_lines_stock_count_sessions_StockCountSessionId",
                        column: x => x.StockCountSessionId,
                        principalTable: "stock_count_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_StockCountShortageAccountId",
                table: "company_finance_settings",
                column: "StockCountShortageAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_StockCountSurplusAccountId",
                table: "company_finance_settings",
                column: "StockCountSurplusAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_lines_InventoryItemId",
                table: "stock_count_lines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_lines_StockCountSessionId_InventoryItemId",
                table: "stock_count_lines",
                columns: new[] { "StockCountSessionId", "InventoryItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_sessions_CompanyId_DocumentNumber",
                table: "stock_count_sessions",
                columns: new[] { "CompanyId", "DocumentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_sessions_WarehouseId_Status",
                table: "stock_count_sessions",
                columns: new[] { "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_sessions_WarehouseZoneId",
                table: "stock_count_sessions",
                column: "WarehouseZoneId");

            migrationBuilder.AddForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_StockCountShor~",
                table: "company_finance_settings",
                column: "StockCountShortageAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_StockCountSurp~",
                table: "company_finance_settings",
                column: "StockCountSurplusAccountId",
                principalTable: "accounting_accounts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_StockCountShor~",
                table: "company_finance_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_company_finance_settings_accounting_accounts_StockCountSurp~",
                table: "company_finance_settings");

            migrationBuilder.DropTable(
                name: "stock_count_lines");

            migrationBuilder.DropTable(
                name: "stock_count_sessions");

            migrationBuilder.DropIndex(
                name: "IX_company_finance_settings_StockCountShortageAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropIndex(
                name: "IX_company_finance_settings_StockCountSurplusAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropColumn(
                name: "StockCountShortageAccountId",
                table: "company_finance_settings");

            migrationBuilder.DropColumn(
                name: "StockCountSurplusAccountId",
                table: "company_finance_settings");
        }
    }
}
