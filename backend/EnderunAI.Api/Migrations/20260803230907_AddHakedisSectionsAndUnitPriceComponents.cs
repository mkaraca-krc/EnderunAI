using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHakedisSectionsAndUnitPriceComponents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CumulativeAdvanceMaterialAmount",
                table: "progress_payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CumulativeWorkAmount",
                table: "progress_payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IncomeTaxWithholdingAmount",
                table: "progress_payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IncomeTaxWithholdingRate",
                table: "progress_payments",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LaborAmount",
                table: "progress_payment_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LaborUnitPrice",
                table: "progress_payment_items",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaterialAmount",
                table: "progress_payment_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaterialUnitPrice",
                table: "progress_payment_items",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OverheadAmount",
                table: "progress_payment_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OverheadUnitPrice",
                table: "progress_payment_items",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "ProgressPaymentSectionId",
                table: "progress_payment_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "progress_payment_sections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgressPaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectHakedisSectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    MaterialAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LaborAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OverheadAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrentAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PreviousAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CumulativeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_progress_payment_sections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_progress_payment_sections_progress_payments_ProgressPayment~",
                        column: x => x.ProgressPaymentId,
                        principalTable: "progress_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_hakedis_sections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
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
                    table.PrimaryKey("PK_project_hakedis_sections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_hakedis_sections_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_items_ProgressPaymentSectionId",
                table: "progress_payment_items",
                column: "ProgressPaymentSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_sections_ProgressPaymentId_Order",
                table: "progress_payment_sections",
                columns: new[] { "ProgressPaymentId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_project_hakedis_sections_ProjectId_Order",
                table: "project_hakedis_sections",
                columns: new[] { "ProjectId", "Order" });

            migrationBuilder.AddForeignKey(
                name: "FK_progress_payment_items_progress_payment_sections_ProgressPa~",
                table: "progress_payment_items",
                column: "ProgressPaymentSectionId",
                principalTable: "progress_payment_sections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_progress_payment_items_progress_payment_sections_ProgressPa~",
                table: "progress_payment_items");

            migrationBuilder.DropTable(
                name: "progress_payment_sections");

            migrationBuilder.DropTable(
                name: "project_hakedis_sections");

            migrationBuilder.DropIndex(
                name: "IX_progress_payment_items_ProgressPaymentSectionId",
                table: "progress_payment_items");

            migrationBuilder.DropColumn(
                name: "CumulativeAdvanceMaterialAmount",
                table: "progress_payments");

            migrationBuilder.DropColumn(
                name: "CumulativeWorkAmount",
                table: "progress_payments");

            migrationBuilder.DropColumn(
                name: "IncomeTaxWithholdingAmount",
                table: "progress_payments");

            migrationBuilder.DropColumn(
                name: "IncomeTaxWithholdingRate",
                table: "progress_payments");

            migrationBuilder.DropColumn(
                name: "LaborAmount",
                table: "progress_payment_items");

            migrationBuilder.DropColumn(
                name: "LaborUnitPrice",
                table: "progress_payment_items");

            migrationBuilder.DropColumn(
                name: "MaterialAmount",
                table: "progress_payment_items");

            migrationBuilder.DropColumn(
                name: "MaterialUnitPrice",
                table: "progress_payment_items");

            migrationBuilder.DropColumn(
                name: "OverheadAmount",
                table: "progress_payment_items");

            migrationBuilder.DropColumn(
                name: "OverheadUnitPrice",
                table: "progress_payment_items");

            migrationBuilder.DropColumn(
                name: "ProgressPaymentSectionId",
                table: "progress_payment_items");
        }
    }
}
