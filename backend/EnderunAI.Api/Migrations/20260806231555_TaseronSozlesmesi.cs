using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class TaseronSozlesmesi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subcontractor_contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectSiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContractNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    WorkDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContractType = table.Column<int>(type: "integer", nullable: false),
                    ContractAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RetentionRate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    WithholdingNumerator = table.Column<int>(type: "integer", nullable: false),
                    WithholdingDenominator = table.Column<int>(type: "integer", nullable: false),
                    MealResponsibility = table.Column<int>(type: "integer", nullable: false),
                    AccommodationResponsibility = table.Column<int>(type: "integer", nullable: false),
                    SocialSecurityResponsibility = table.Column<int>(type: "integer", nullable: false),
                    MaterialResponsibility = table.Column<int>(type: "integer", nullable: false),
                    OhsResponsibility = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_subcontractor_contracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subcontractor_contracts_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subcontractor_contracts_current_accounts_CurrentAccountId",
                        column: x => x.CurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subcontractor_contracts_project_sites_ProjectSiteId",
                        column: x => x.ProjectSiteId,
                        principalTable: "project_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subcontractor_contracts_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subcontractor_contract_sections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubcontractorContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectHakedisSectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_subcontractor_contract_sections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subcontractor_contract_sections_project_hakedis_sections_Pr~",
                        column: x => x.ProjectHakedisSectionId,
                        principalTable: "project_hakedis_sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subcontractor_contract_sections_subcontractor_contracts_Sub~",
                        column: x => x.SubcontractorContractId,
                        principalTable: "subcontractor_contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_contract_sections_ProjectHakedisSectionId",
                table: "subcontractor_contract_sections",
                column: "ProjectHakedisSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_contract_sections_SubcontractorContractId_Pro~",
                table: "subcontractor_contract_sections",
                columns: new[] { "SubcontractorContractId", "ProjectHakedisSectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_contracts_CompanyId_ContractNumber",
                table: "subcontractor_contracts",
                columns: new[] { "CompanyId", "ContractNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_contracts_CurrentAccountId",
                table: "subcontractor_contracts",
                column: "CurrentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_contracts_ProjectId_Status",
                table: "subcontractor_contracts",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_contracts_ProjectSiteId",
                table: "subcontractor_contracts",
                column: "ProjectSiteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subcontractor_contract_sections");

            migrationBuilder.DropTable(
                name: "subcontractor_contracts");
        }
    }
}
