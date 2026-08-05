using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteReportBoqLinkAndFieldQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectBoqItemId",
                table: "project_site_daily_report_work_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CumulativeFieldQuantity",
                table: "progress_payment_items",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FieldQuantity",
                table: "progress_payment_items",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectBoqItemId",
                table: "progress_payment_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_site_daily_report_work_items_ProjectBoqItemId",
                table: "project_site_daily_report_work_items",
                column: "ProjectBoqItemId");

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_items_ProjectBoqItemId",
                table: "progress_payment_items",
                column: "ProjectBoqItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_progress_payment_items_project_boq_items_ProjectBoqItemId",
                table: "progress_payment_items",
                column: "ProjectBoqItemId",
                principalTable: "project_boq_items",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_project_site_daily_report_work_items_project_boq_items_Proj~",
                table: "project_site_daily_report_work_items",
                column: "ProjectBoqItemId",
                principalTable: "project_boq_items",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_progress_payment_items_project_boq_items_ProjectBoqItemId",
                table: "progress_payment_items");

            migrationBuilder.DropForeignKey(
                name: "FK_project_site_daily_report_work_items_project_boq_items_Proj~",
                table: "project_site_daily_report_work_items");

            migrationBuilder.DropIndex(
                name: "IX_project_site_daily_report_work_items_ProjectBoqItemId",
                table: "project_site_daily_report_work_items");

            migrationBuilder.DropIndex(
                name: "IX_progress_payment_items_ProjectBoqItemId",
                table: "progress_payment_items");

            migrationBuilder.DropColumn(
                name: "ProjectBoqItemId",
                table: "project_site_daily_report_work_items");

            migrationBuilder.DropColumn(
                name: "CumulativeFieldQuantity",
                table: "progress_payment_items");

            migrationBuilder.DropColumn(
                name: "FieldQuantity",
                table: "progress_payment_items");

            migrationBuilder.DropColumn(
                name: "ProjectBoqItemId",
                table: "progress_payment_items");
        }
    }
}
