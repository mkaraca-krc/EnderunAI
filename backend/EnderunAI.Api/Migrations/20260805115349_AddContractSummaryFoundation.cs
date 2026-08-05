using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddContractSummaryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UsesContractSummary",
                table: "projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "AmendmentDate",
                table: "project_boqs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AmendmentNumber",
                table: "project_boqs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevisionReason",
                table: "project_boqs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersededBoqId",
                table: "project_boqs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LaborUnitPrice",
                table: "project_boq_items",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaterialUnitPrice",
                table: "project_boq_items",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OverheadUnitPrice",
                table: "project_boq_items",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            // Mevcut kalemlerin tek birim fiyatı malzemeye taşınıyor.
            // Bu, yeni kalemler için de geçerli olan kuralın aynısı:
            // bileşen verilmemişse tek fiyat malzeme kabul edilir.
            // Yapılmasaydı eski kalemler "malzeme 0, montaj 0, GG&K 0"
            // görünürken UnitPrice dolu kalır, icmal kendi içinde
            // tutarsız okunurdu.
            migrationBuilder.Sql("""
                UPDATE project_boq_items
                SET "MaterialUnitPrice" = "UnitPrice"
                WHERE "MaterialUnitPrice" = 0
                  AND "LaborUnitPrice" = 0
                  AND "OverheadUnitPrice" = 0
                  AND "UnitPrice" <> 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UsesContractSummary",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "AmendmentDate",
                table: "project_boqs");

            migrationBuilder.DropColumn(
                name: "AmendmentNumber",
                table: "project_boqs");

            migrationBuilder.DropColumn(
                name: "RevisionReason",
                table: "project_boqs");

            migrationBuilder.DropColumn(
                name: "SupersededBoqId",
                table: "project_boqs");

            migrationBuilder.DropColumn(
                name: "LaborUnitPrice",
                table: "project_boq_items");

            migrationBuilder.DropColumn(
                name: "MaterialUnitPrice",
                table: "project_boq_items");

            migrationBuilder.DropColumn(
                name: "OverheadUnitPrice",
                table: "project_boq_items");
        }
    }
}
