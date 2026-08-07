using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class TaseronHakedisiVeEkip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubcontractorContractId",
                table: "personnel",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subcontractor_progress_payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubcontractorContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgressPaymentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PeriodNumber = table.Column<int>(type: "integer", nullable: false),
                    PeriodStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProgressPaymentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ContractAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PreviousAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrentAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CumulativeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDeductionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossPayableAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetPayableAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_subcontractor_progress_payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subcontractor_progress_payments_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subcontractor_progress_payments_subcontractor_contracts_Sub~",
                        column: x => x.SubcontractorContractId,
                        principalTable: "subcontractor_contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subcontractor_progress_payment_deductions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubcontractorProgressPaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    DeductionType = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    CumulativeBaseAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PreviousAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CumulativeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsManualAmount = table.Column<bool>(type: "boolean", nullable: false),
                    SuggestionBasis = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_subcontractor_progress_payment_deductions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subcontractor_progress_payment_deductions_subcontractor_pro~",
                        column: x => x.SubcontractorProgressPaymentId,
                        principalTable: "subcontractor_progress_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subcontractor_progress_payment_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubcontractorProgressPaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectHakedisSectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectBoqItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    PositionCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ContractQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PreviousQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SuggestedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AgreedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CurrentQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PreviousAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrentAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CumulativeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_subcontractor_progress_payment_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subcontractor_progress_payment_items_project_boq_items_Proj~",
                        column: x => x.ProjectBoqItemId,
                        principalTable: "project_boq_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subcontractor_progress_payment_items_project_hakedis_sectio~",
                        column: x => x.ProjectHakedisSectionId,
                        principalTable: "project_hakedis_sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subcontractor_progress_payment_items_subcontractor_progress~",
                        column: x => x.SubcontractorProgressPaymentId,
                        principalTable: "subcontractor_progress_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subcontractor_progress_payment_sections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubcontractorProgressPaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectHakedisSectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    SectionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PreviousProgressRate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    SuggestedProgressRate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    AgreedProgressRate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    PreviousAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrentAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CumulativeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_subcontractor_progress_payment_sections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subcontractor_progress_payment_sections_project_hakedis_sec~",
                        column: x => x.ProjectHakedisSectionId,
                        principalTable: "project_hakedis_sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subcontractor_progress_payment_sections_subcontractor_progr~",
                        column: x => x.SubcontractorProgressPaymentId,
                        principalTable: "subcontractor_progress_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_personnel_SubcontractorContractId",
                table: "personnel",
                column: "SubcontractorContractId");

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_progress_payment_deductions_SubcontractorProg~",
                table: "subcontractor_progress_payment_deductions",
                columns: new[] { "SubcontractorProgressPaymentId", "LineNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_progress_payment_items_ProjectBoqItemId",
                table: "subcontractor_progress_payment_items",
                column: "ProjectBoqItemId");

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_progress_payment_items_ProjectHakedisSectionId",
                table: "subcontractor_progress_payment_items",
                column: "ProjectHakedisSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_progress_payment_items_SubcontractorProgressP~",
                table: "subcontractor_progress_payment_items",
                columns: new[] { "SubcontractorProgressPaymentId", "LineNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_progress_payment_sections_ProjectHakedisSecti~",
                table: "subcontractor_progress_payment_sections",
                column: "ProjectHakedisSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_progress_payment_sections_SubcontractorProgre~",
                table: "subcontractor_progress_payment_sections",
                columns: new[] { "SubcontractorProgressPaymentId", "ProjectHakedisSectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_progress_payments_CompanyId_ProgressPaymentNu~",
                table: "subcontractor_progress_payments",
                columns: new[] { "CompanyId", "ProgressPaymentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_progress_payments_SubcontractorContractId_Per~",
                table: "subcontractor_progress_payments",
                columns: new[] { "SubcontractorContractId", "PeriodNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_personnel_subcontractor_contracts_SubcontractorContractId",
                table: "personnel",
                column: "SubcontractorContractId",
                principalTable: "subcontractor_contracts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_personnel_subcontractor_contracts_SubcontractorContractId",
                table: "personnel");

            migrationBuilder.DropTable(
                name: "subcontractor_progress_payment_deductions");

            migrationBuilder.DropTable(
                name: "subcontractor_progress_payment_items");

            migrationBuilder.DropTable(
                name: "subcontractor_progress_payment_sections");

            migrationBuilder.DropTable(
                name: "subcontractor_progress_payments");

            migrationBuilder.DropIndex(
                name: "IX_personnel_SubcontractorContractId",
                table: "personnel");

            migrationBuilder.DropColumn(
                name: "SubcontractorContractId",
                table: "personnel");
        }
    }
}
