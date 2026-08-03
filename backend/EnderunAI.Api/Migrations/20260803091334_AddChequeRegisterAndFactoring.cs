using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChequeRegisterAndFactoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cheques",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    InternalNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ChequeNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BankName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    BankBranch = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Drawer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CurrentAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IssueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProgressPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CashAccountId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_cheques", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cheques_cash_accounts_CashAccountId",
                        column: x => x.CashAccountId,
                        principalTable: "cash_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cheques_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cheques_current_accounts_CurrentAccountId",
                        column: x => x.CurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cheques_progress_payments_ProgressPaymentId",
                        column: x => x.ProgressPaymentId,
                        principalTable: "progress_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cheques_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cheques_supplier_invoices_SupplierInvoiceId",
                        column: x => x.SupplierInvoiceId,
                        principalTable: "supplier_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cheque_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChequeId = table.Column<Guid>(type: "uuid", nullable: false),
                    MovementDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: true),
                    ToStatus = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CashAccountId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_cheque_movements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cheque_movements_accounting_vouchers_AccountingVoucherId",
                        column: x => x.AccountingVoucherId,
                        principalTable: "accounting_vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cheque_movements_cash_accounts_CashAccountId",
                        column: x => x.CashAccountId,
                        principalTable: "cash_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cheque_movements_cheques_ChequeId",
                        column: x => x.ChequeId,
                        principalTable: "cheques",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "factoring_transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    InternalNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ChequeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FactoringCurrentAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CashAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransactionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ChequeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CommissionRate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BsmvRate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    BsmvAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpenseAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDeductionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AccountingVoucherId = table.Column<Guid>(type: "uuid", nullable: true),
                    CashTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_factoring_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_factoring_transactions_accounting_vouchers_AccountingVouche~",
                        column: x => x.AccountingVoucherId,
                        principalTable: "accounting_vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_factoring_transactions_cash_accounts_CashAccountId",
                        column: x => x.CashAccountId,
                        principalTable: "cash_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_factoring_transactions_cash_transactions_CashTransactionId",
                        column: x => x.CashTransactionId,
                        principalTable: "cash_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_factoring_transactions_cheques_ChequeId",
                        column: x => x.ChequeId,
                        principalTable: "cheques",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_factoring_transactions_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_factoring_transactions_current_accounts_FactoringCurrentAcc~",
                        column: x => x.FactoringCurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_factoring_transactions_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cheque_movements_AccountingVoucherId",
                table: "cheque_movements",
                column: "AccountingVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_cheque_movements_CashAccountId",
                table: "cheque_movements",
                column: "CashAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_cheque_movements_ChequeId_MovementDate",
                table: "cheque_movements",
                columns: new[] { "ChequeId", "MovementDate" });

            migrationBuilder.CreateIndex(
                name: "IX_cheques_CashAccountId",
                table: "cheques",
                column: "CashAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_cheques_CompanyId_Direction_Status",
                table: "cheques",
                columns: new[] { "CompanyId", "Direction", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_cheques_CompanyId_InternalNumber",
                table: "cheques",
                columns: new[] { "CompanyId", "InternalNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cheques_CurrentAccountId",
                table: "cheques",
                column: "CurrentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_cheques_DueDate",
                table: "cheques",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_cheques_ProgressPaymentId",
                table: "cheques",
                column: "ProgressPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_cheques_ProjectId",
                table: "cheques",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_cheques_SupplierInvoiceId",
                table: "cheques",
                column: "SupplierInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_factoring_transactions_AccountingVoucherId",
                table: "factoring_transactions",
                column: "AccountingVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_factoring_transactions_CashAccountId",
                table: "factoring_transactions",
                column: "CashAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_factoring_transactions_CashTransactionId",
                table: "factoring_transactions",
                column: "CashTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_factoring_transactions_ChequeId",
                table: "factoring_transactions",
                column: "ChequeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_factoring_transactions_CompanyId_InternalNumber",
                table: "factoring_transactions",
                columns: new[] { "CompanyId", "InternalNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_factoring_transactions_FactoringCurrentAccountId",
                table: "factoring_transactions",
                column: "FactoringCurrentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_factoring_transactions_ProjectId",
                table: "factoring_transactions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_factoring_transactions_TransactionDate",
                table: "factoring_transactions",
                column: "TransactionDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cheque_movements");

            migrationBuilder.DropTable(
                name: "factoring_transactions");

            migrationBuilder.DropTable(
                name: "cheques");
        }
    }
}
