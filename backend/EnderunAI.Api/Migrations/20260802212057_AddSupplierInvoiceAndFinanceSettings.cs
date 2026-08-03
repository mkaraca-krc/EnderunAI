using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierInvoiceAndFinanceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "purchase_orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "purchase_orders",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // NOT: current_accounts.Payable/ReceivableAccountingAccountId
            // kolonları, indeksleri ve FK'leri 20260724083553 migration'ında
            // zaten oluşturuldu; model bu migration'la şemayı geriye dönük
            // sahiplendiği için burada duplicate op üretilmedi.

            migrationBuilder.CreateTable(
                name: "company_finance_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    GmApprovalThresholdTry = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ThreeWayTolerancePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    DefaultVatRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    VatInAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    VatOutAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    SalesAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpenseAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayablesAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivablesAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    FactoringExpenseAccountId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_company_finance_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_company_finance_settings_accounting_accounts_ExpenseAccount~",
                        column: x => x.ExpenseAccountId,
                        principalTable: "accounting_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_company_finance_settings_accounting_accounts_FactoringExpen~",
                        column: x => x.FactoringExpenseAccountId,
                        principalTable: "accounting_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_company_finance_settings_accounting_accounts_PayablesAccoun~",
                        column: x => x.PayablesAccountId,
                        principalTable: "accounting_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_company_finance_settings_accounting_accounts_ReceivablesAcc~",
                        column: x => x.ReceivablesAccountId,
                        principalTable: "accounting_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_company_finance_settings_accounting_accounts_SalesAccountId",
                        column: x => x.SalesAccountId,
                        principalTable: "accounting_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_company_finance_settings_accounting_accounts_VatInAccountId",
                        column: x => x.VatInAccountId,
                        principalTable: "accounting_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_company_finance_settings_accounting_accounts_VatOutAccountId",
                        column: x => x.VatOutAccountId,
                        principalTable: "accounting_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_company_finance_settings_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierCurrentAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    GoodsReceiptId = table.Column<Guid>(type: "uuid", nullable: true),
                    InternalNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InvoiceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MatchStatus = table.Column<int>(type: "integer", nullable: false),
                    MatchDifferenceAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MatchNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequiresGmApproval = table.Column<bool>(type: "boolean", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_supplier_invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_invoices_accounting_vouchers_AccountingVoucherId",
                        column: x => x.AccountingVoucherId,
                        principalTable: "accounting_vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_invoices_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_invoices_current_accounts_SupplierCurrentAccountId",
                        column: x => x.SupplierCurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_invoices_goods_receipts_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "goods_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_invoices_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_invoices_purchase_orders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "purchase_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_invoice_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    LineSubtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PurchaseOrderItemId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_supplier_invoice_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_invoice_items_purchase_order_items_PurchaseOrderIt~",
                        column: x => x.PurchaseOrderItemId,
                        principalTable: "purchase_order_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_invoice_items_supplier_invoices_SupplierInvoiceId",
                        column: x => x.SupplierInvoiceId,
                        principalTable: "supplier_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_CompanyId",
                table: "company_finance_settings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_ExpenseAccountId",
                table: "company_finance_settings",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_FactoringExpenseAccountId",
                table: "company_finance_settings",
                column: "FactoringExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_PayablesAccountId",
                table: "company_finance_settings",
                column: "PayablesAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_ReceivablesAccountId",
                table: "company_finance_settings",
                column: "ReceivablesAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_SalesAccountId",
                table: "company_finance_settings",
                column: "SalesAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_VatInAccountId",
                table: "company_finance_settings",
                column: "VatInAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_company_finance_settings_VatOutAccountId",
                table: "company_finance_settings",
                column: "VatOutAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_items_PurchaseOrderItemId",
                table: "supplier_invoice_items",
                column: "PurchaseOrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_items_SupplierInvoiceId_LineNumber",
                table: "supplier_invoice_items",
                columns: new[] { "SupplierInvoiceId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoices_AccountingVoucherId",
                table: "supplier_invoices",
                column: "AccountingVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoices_CompanyId_InternalNumber",
                table: "supplier_invoices",
                columns: new[] { "CompanyId", "InternalNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoices_CompanyId_Status",
                table: "supplier_invoices",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoices_GoodsReceiptId",
                table: "supplier_invoices",
                column: "GoodsReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoices_ProjectId",
                table: "supplier_invoices",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoices_PurchaseOrderId",
                table: "supplier_invoices",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoices_SupplierCurrentAccountId_InvoiceNumber",
                table: "supplier_invoices",
                columns: new[] { "SupplierCurrentAccountId", "InvoiceNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // current_accounts kolonları/FK'leri bu migration'da
            // oluşturulmadığı için burada da düşürülmüyor (bkz. Up notu).
            migrationBuilder.DropTable(
                name: "company_finance_settings");

            migrationBuilder.DropTable(
                name: "supplier_invoice_items");

            migrationBuilder.DropTable(
                name: "supplier_invoices");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "purchase_orders");
        }
    }
}
