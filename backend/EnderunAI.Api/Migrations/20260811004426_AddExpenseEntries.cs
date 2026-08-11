using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expense_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CenterType = table.Column<int>(type: "integer", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectSiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpenseCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    SupplierCurrentAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecurringTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    PeriodYear = table.Column<int>(type: "integer", nullable: true),
                    PeriodMonth = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_expense_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_expense_entries_branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expense_entries_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_expense_entries_current_accounts_SupplierCurrentAccountId",
                        column: x => x.SupplierCurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expense_entries_expense_categories_ExpenseCategoryId",
                        column: x => x.ExpenseCategoryId,
                        principalTable: "expense_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expense_entries_project_sites_ProjectSiteId",
                        column: x => x.ProjectSiteId,
                        principalTable: "project_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expense_entries_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expense_entries_BranchId",
                table: "expense_entries",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_expense_entries_CompanyId_ExpenseDate",
                table: "expense_entries",
                columns: new[] { "CompanyId", "ExpenseDate" });

            migrationBuilder.CreateIndex(
                name: "IX_expense_entries_ExpenseCategoryId",
                table: "expense_entries",
                column: "ExpenseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_expense_entries_ProjectId",
                table: "expense_entries",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_expense_entries_ProjectSiteId",
                table: "expense_entries",
                column: "ProjectSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_expense_entries_SupplierCurrentAccountId",
                table: "expense_entries",
                column: "SupplierCurrentAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expense_entries");
        }
    }
}
