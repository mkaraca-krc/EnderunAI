using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHakedisDeductionCumulativeAndBarter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AllRiskInsuranceRate",
                table: "projects",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BarterRate",
                table: "projects",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BarterRate",
                table: "project_sites",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CumulativeAmount",
                table: "progress_payment_deductions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CumulativeBaseAmount",
                table: "progress_payment_deductions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PreviousAmount",
                table: "progress_payment_deductions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "barter_ledger_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectSiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProgressPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntryType = table.Column<int>(type: "integer", nullable: false),
                    EntryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_barter_ledger_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_barter_ledger_entries_progress_payments_ProgressPaymentId",
                        column: x => x.ProgressPaymentId,
                        principalTable: "progress_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_barter_ledger_entries_project_sites_ProjectSiteId",
                        column: x => x.ProjectSiteId,
                        principalTable: "project_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_barter_ledger_entries_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "progress_payment_deduction_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgressPaymentDeductionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_progress_payment_deduction_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_progress_payment_deduction_lines_progress_payment_deduction~",
                        column: x => x.ProgressPaymentDeductionId,
                        principalTable: "progress_payment_deductions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_barter_ledger_entries_ProgressPaymentId",
                table: "barter_ledger_entries",
                column: "ProgressPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_barter_ledger_entries_ProjectId_EntryDate",
                table: "barter_ledger_entries",
                columns: new[] { "ProjectId", "EntryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_barter_ledger_entries_ProjectSiteId",
                table: "barter_ledger_entries",
                column: "ProjectSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_deduction_lines_ProgressPaymentDeductionId~",
                table: "progress_payment_deduction_lines",
                columns: new[] { "ProgressPaymentDeductionId", "LineNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "barter_ledger_entries");

            migrationBuilder.DropTable(
                name: "progress_payment_deduction_lines");

            migrationBuilder.DropColumn(
                name: "AllRiskInsuranceRate",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "BarterRate",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "BarterRate",
                table: "project_sites");

            migrationBuilder.DropColumn(
                name: "CumulativeAmount",
                table: "progress_payment_deductions");

            migrationBuilder.DropColumn(
                name: "CumulativeBaseAmount",
                table: "progress_payment_deductions");

            migrationBuilder.DropColumn(
                name: "PreviousAmount",
                table: "progress_payment_deductions");
        }
    }
}
