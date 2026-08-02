using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHrProjectLaborCostSiteBreakdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // hr_project_labor_costs already exists (created by the earlier
            // orphaned AddHumanResourcesWorkManagementPackage migration,
            // whose model was never wired into AppDbContext, so the snapshot
            // never knew about it). This migration only adds the new
            // ProjectSiteId column that the model now defines.
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectSiteId",
                table: "hr_project_labor_costs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_hr_project_labor_costs_ProjectSiteId",
                table: "hr_project_labor_costs",
                column: "ProjectSiteId");

            migrationBuilder.AddForeignKey(
                name: "FK_hr_project_labor_costs_project_sites_ProjectSiteId",
                table: "hr_project_labor_costs",
                column: "ProjectSiteId",
                principalTable: "project_sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_hr_project_labor_costs_project_sites_ProjectSiteId",
                table: "hr_project_labor_costs");

            migrationBuilder.DropIndex(
                name: "IX_hr_project_labor_costs_ProjectSiteId",
                table: "hr_project_labor_costs");

            migrationBuilder.DropColumn(
                name: "ProjectSiteId",
                table: "hr_project_labor_costs");
        }
    }
}
