using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddContractTypeAndBoqBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContractType",
                table: "projects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "DeviationAlertThresholdRate",
                table: "projects",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ContractType",
                table: "project_hakedis_sections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsContractBaseline",
                table: "project_boqs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryItemId",
                table: "project_boq_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectHakedisSectionId",
                table: "project_boq_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_boqs_ProjectId",
                table: "project_boqs",
                column: "ProjectId",
                unique: true,
                filter: "\"IsDeleted\" = false AND \"IsContractBaseline\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_project_boq_items_InventoryItemId",
                table: "project_boq_items",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_project_boq_items_ProjectHakedisSectionId",
                table: "project_boq_items",
                column: "ProjectHakedisSectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_project_boq_items_inventory_items_InventoryItemId",
                table: "project_boq_items",
                column: "InventoryItemId",
                principalTable: "inventory_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_project_boq_items_project_hakedis_sections_ProjectHakedisSe~",
                table: "project_boq_items",
                column: "ProjectHakedisSectionId",
                principalTable: "project_hakedis_sections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_project_boq_items_inventory_items_InventoryItemId",
                table: "project_boq_items");

            migrationBuilder.DropForeignKey(
                name: "FK_project_boq_items_project_hakedis_sections_ProjectHakedisSe~",
                table: "project_boq_items");

            migrationBuilder.DropIndex(
                name: "IX_project_boqs_ProjectId",
                table: "project_boqs");

            migrationBuilder.DropIndex(
                name: "IX_project_boq_items_InventoryItemId",
                table: "project_boq_items");

            migrationBuilder.DropIndex(
                name: "IX_project_boq_items_ProjectHakedisSectionId",
                table: "project_boq_items");

            migrationBuilder.DropColumn(
                name: "ContractType",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "DeviationAlertThresholdRate",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "ContractType",
                table: "project_hakedis_sections");

            migrationBuilder.DropColumn(
                name: "IsContractBaseline",
                table: "project_boqs");

            migrationBuilder.DropColumn(
                name: "InventoryItemId",
                table: "project_boq_items");

            migrationBuilder.DropColumn(
                name: "ProjectHakedisSectionId",
                table: "project_boq_items");
        }
    }
}
