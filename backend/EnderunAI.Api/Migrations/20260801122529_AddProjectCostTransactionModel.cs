using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectCostTransactionModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ProjectCostTransactions table itself already exists (created by
            // the earlier orphaned AddProjectCostTransaction migration, whose
            // model was never wired into AppDbContext, so the snapshot never
            // knew about it). This migration only adds the new ProjectSiteId
            // column that the model now defines — it must not recreate the
            // table.
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectSiteId",
                table: "ProjectCostTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCostTransactions_ProjectSiteId",
                table: "ProjectCostTransactions",
                column: "ProjectSiteId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectCostTransactions_project_sites_ProjectSiteId",
                table: "ProjectCostTransactions",
                column: "ProjectSiteId",
                principalTable: "project_sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectCostTransactions_project_sites_ProjectSiteId",
                table: "ProjectCostTransactions");

            migrationBuilder.DropIndex(
                name: "IX_ProjectCostTransactions_ProjectSiteId",
                table: "ProjectCostTransactions");

            migrationBuilder.DropColumn(
                name: "ProjectSiteId",
                table: "ProjectCostTransactions");
        }
    }
}
