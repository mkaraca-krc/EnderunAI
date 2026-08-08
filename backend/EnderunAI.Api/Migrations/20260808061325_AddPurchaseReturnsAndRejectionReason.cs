using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseReturnsAndRejectionReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "goods_receipt_items",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "purchase_returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoodsReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierCurrentAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReturnDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_purchase_returns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_purchase_returns_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_returns_current_accounts_SupplierCurrentAccountId",
                        column: x => x.SupplierCurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_returns_goods_receipts_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "goods_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_returns_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_returns_purchase_orders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "purchase_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_return_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoodsReceiptItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    MaterialDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReasonKind = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
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
                    table.PrimaryKey("PK_purchase_return_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_purchase_return_items_goods_receipt_items_GoodsReceiptItemId",
                        column: x => x.GoodsReceiptItemId,
                        principalTable: "goods_receipt_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_return_items_purchase_returns_PurchaseReturnId",
                        column: x => x.PurchaseReturnId,
                        principalTable: "purchase_returns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_return_items_GoodsReceiptItemId",
                table: "purchase_return_items",
                column: "GoodsReceiptItemId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_return_items_PurchaseReturnId_LineNumber",
                table: "purchase_return_items",
                columns: new[] { "PurchaseReturnId", "LineNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_CompanyId_ReturnNumber",
                table: "purchase_returns",
                columns: new[] { "CompanyId", "ReturnNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_CompanyId_Status",
                table: "purchase_returns",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_GoodsReceiptId",
                table: "purchase_returns",
                column: "GoodsReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_ProjectId",
                table: "purchase_returns",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_PurchaseOrderId",
                table: "purchase_returns",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_SupplierCurrentAccountId",
                table: "purchase_returns",
                column: "SupplierCurrentAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_return_items");

            migrationBuilder.DropTable(
                name: "purchase_returns");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "goods_receipt_items");
        }
    }
}
