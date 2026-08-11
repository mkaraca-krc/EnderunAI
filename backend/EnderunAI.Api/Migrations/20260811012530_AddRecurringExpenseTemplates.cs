using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringExpenseTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recurring_expense_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CenterType = table.Column<int>(type: "integer", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectSiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpenseCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EstimatedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: false),
                    SupplierCurrentAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartYear = table.Column<int>(type: "integer", nullable: false),
                    StartMonth = table.Column<int>(type: "integer", nullable: false),
                    EndYear = table.Column<int>(type: "integer", nullable: true),
                    EndMonth = table.Column<int>(type: "integer", nullable: true),
                    PaymentDay = table.Column<int>(type: "integer", nullable: false),
                    IsStopped = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_recurring_expense_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recurring_expense_templates_branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recurring_expense_templates_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_recurring_expense_templates_current_accounts_SupplierCurren~",
                        column: x => x.SupplierCurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recurring_expense_templates_expense_categories_ExpenseCateg~",
                        column: x => x.ExpenseCategoryId,
                        principalTable: "expense_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recurring_expense_templates_project_sites_ProjectSiteId",
                        column: x => x.ProjectSiteId,
                        principalTable: "project_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recurring_expense_templates_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recurring_expense_templates_BranchId",
                table: "recurring_expense_templates",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_expense_templates_CompanyId_IsStopped",
                table: "recurring_expense_templates",
                columns: new[] { "CompanyId", "IsStopped" });

            migrationBuilder.CreateIndex(
                name: "IX_recurring_expense_templates_ExpenseCategoryId",
                table: "recurring_expense_templates",
                column: "ExpenseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_expense_templates_ProjectId",
                table: "recurring_expense_templates",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_expense_templates_ProjectSiteId",
                table: "recurring_expense_templates",
                column: "ProjectSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_expense_templates_SupplierCurrentAccountId",
                table: "recurring_expense_templates",
                column: "SupplierCurrentAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurring_expense_templates");
        }
    }
}
