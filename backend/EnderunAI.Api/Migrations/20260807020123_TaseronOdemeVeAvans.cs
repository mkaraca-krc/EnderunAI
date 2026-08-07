using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class TaseronOdemeVeAvans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subcontractor_cash_ledger_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubcontractorContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubcontractorProgressPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    EntryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_subcontractor_cash_ledger_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subcontractor_cash_ledger_entries_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subcontractor_cash_ledger_entries_subcontractor_contracts_S~",
                        column: x => x.SubcontractorContractId,
                        principalTable: "subcontractor_contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subcontractor_cash_ledger_entries_subcontractor_progress_pa~",
                        column: x => x.SubcontractorProgressPaymentId,
                        principalTable: "subcontractor_progress_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subcontractor_ledger_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubcontractorContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubcontractorProgressPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    EntryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    WithholdingAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PayableAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    SupplierInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_subcontractor_ledger_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subcontractor_ledger_entries_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subcontractor_ledger_entries_subcontractor_contracts_Subcon~",
                        column: x => x.SubcontractorContractId,
                        principalTable: "subcontractor_contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subcontractor_ledger_entries_subcontractor_progress_payment~",
                        column: x => x.SubcontractorProgressPaymentId,
                        principalTable: "subcontractor_progress_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_cash_ledger_entries_CompanyId",
                table: "subcontractor_cash_ledger_entries",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_cash_ledger_entries_SubcontractorContractId_K~",
                table: "subcontractor_cash_ledger_entries",
                columns: new[] { "SubcontractorContractId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_cash_ledger_entries_SubcontractorProgressPaym~",
                table: "subcontractor_cash_ledger_entries",
                column: "SubcontractorProgressPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_ledger_entries_CompanyId",
                table: "subcontractor_ledger_entries",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_ledger_entries_SubcontractorContractId_Kind",
                table: "subcontractor_ledger_entries",
                columns: new[] { "SubcontractorContractId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_ledger_entries_SubcontractorProgressPaymentId",
                table: "subcontractor_ledger_entries",
                column: "SubcontractorProgressPaymentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subcontractor_cash_ledger_entries");

            migrationBuilder.DropTable(
                name: "subcontractor_ledger_entries");
        }
    }
}
