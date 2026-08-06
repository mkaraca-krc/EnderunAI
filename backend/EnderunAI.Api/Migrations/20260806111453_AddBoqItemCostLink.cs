using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBoqItemCostLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectBoqItemId",
                table: "supplier_invoice_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectBoqItemId",
                table: "ProjectCostTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectBoqItemId",
                table: "hr_project_labor_costs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_items_ProjectBoqItemId",
                table: "supplier_invoice_items",
                column: "ProjectBoqItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCostTransactions_ProjectBoqItemId",
                table: "ProjectCostTransactions",
                column: "ProjectBoqItemId");

            migrationBuilder.CreateIndex(
                name: "IX_hr_project_labor_costs_ProjectBoqItemId",
                table: "hr_project_labor_costs",
                column: "ProjectBoqItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_hr_project_labor_costs_project_boq_items_ProjectBoqItemId",
                table: "hr_project_labor_costs",
                column: "ProjectBoqItemId",
                principalTable: "project_boq_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectCostTransactions_project_boq_items_ProjectBoqItemId",
                table: "ProjectCostTransactions",
                column: "ProjectBoqItemId",
                principalTable: "project_boq_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_invoice_items_project_boq_items_ProjectBoqItemId",
                table: "supplier_invoice_items",
                column: "ProjectBoqItemId",
                principalTable: "project_boq_items",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_hr_project_labor_costs_project_boq_items_ProjectBoqItemId",
                table: "hr_project_labor_costs");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectCostTransactions_project_boq_items_ProjectBoqItemId",
                table: "ProjectCostTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_supplier_invoice_items_project_boq_items_ProjectBoqItemId",
                table: "supplier_invoice_items");

            migrationBuilder.DropIndex(
                name: "IX_supplier_invoice_items_ProjectBoqItemId",
                table: "supplier_invoice_items");

            migrationBuilder.DropIndex(
                name: "IX_ProjectCostTransactions_ProjectBoqItemId",
                table: "ProjectCostTransactions");

            migrationBuilder.DropIndex(
                name: "IX_hr_project_labor_costs_ProjectBoqItemId",
                table: "hr_project_labor_costs");

            migrationBuilder.DropColumn(
                name: "ProjectBoqItemId",
                table: "supplier_invoice_items");

            migrationBuilder.DropColumn(
                name: "ProjectBoqItemId",
                table: "ProjectCostTransactions");

            migrationBuilder.DropColumn(
                name: "ProjectBoqItemId",
                table: "hr_project_labor_costs");
        }
    }
}
