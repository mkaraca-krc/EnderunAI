using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStockReservationAndDeadFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "ReservedQuantity",
                table: "warehouse_stocks");

            migrationBuilder.DropColumn(
                name: "AllRiskInsuranceRate",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "BarterRate",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "HealthReason",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "BarterRate",
                table: "project_sites");

            migrationBuilder.DropColumn(
                name: "ActualLeaveGross",
                table: "personnel_terminations");

            migrationBuilder.DropColumn(
                name: "ActualNoticeGross",
                table: "personnel_terminations");

            migrationBuilder.DropColumn(
                name: "ActualSeveranceGross",
                table: "personnel_terminations");

            migrationBuilder.DropColumn(
                name: "CurrentCompany",
                table: "hr_job_candidates");

            migrationBuilder.DropColumn(
                name: "CvFilePath",
                table: "hr_job_candidates");

            migrationBuilder.DropColumn(
                name: "InterviewerUserId",
                table: "hr_candidate_interviews");

            migrationBuilder.DropColumn(
                name: "Strengths",
                table: "hr_candidate_interviews");

            migrationBuilder.DropColumn(
                name: "Weaknesses",
                table: "hr_candidate_interviews");

            migrationBuilder.DropColumn(
                name: "InventoryQuantity",
                table: "hr_asset_assignments");

            migrationBuilder.DropColumn(
                name: "IssuedUnitCost",
                table: "hr_asset_assignments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReservedQuantity",
                table: "warehouse_stocks",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AllRiskInsuranceRate",
                table: "projects",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BarterRate",
                table: "projects",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "HealthReason",
                table: "projects",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BarterRate",
                table: "project_sites",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualLeaveGross",
                table: "personnel_terminations",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualNoticeGross",
                table: "personnel_terminations",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualSeveranceGross",
                table: "personnel_terminations",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CurrentCompany",
                table: "hr_job_candidates",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CvFilePath",
                table: "hr_job_candidates",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InterviewerUserId",
                table: "hr_candidate_interviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Strengths",
                table: "hr_candidate_interviews",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Weaknesses",
                table: "hr_candidate_interviews",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InventoryQuantity",
                table: "hr_asset_assignments",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IssuedUnitCost",
                table: "hr_asset_assignments",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "stock_reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseRequestItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    ReservationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReservationNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_reservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_reservations_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_reservations_inventory_items_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "inventory_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_reservations_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_reservations_purchase_request_items_PurchaseRequestIt~",
                        column: x => x.PurchaseRequestItemId,
                        principalTable: "purchase_request_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_reservations_purchase_requests_PurchaseRequestId",
                        column: x => x.PurchaseRequestId,
                        principalTable: "purchase_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_reservations_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_CompanyId",
                table: "stock_reservations",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_InventoryItemId",
                table: "stock_reservations",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_ProjectId",
                table: "stock_reservations",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_PurchaseRequestId_PurchaseRequestItemId",
                table: "stock_reservations",
                columns: new[] { "PurchaseRequestId", "PurchaseRequestItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_PurchaseRequestItemId",
                table: "stock_reservations",
                column: "PurchaseRequestItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_ReservationNumber",
                table: "stock_reservations",
                column: "ReservationNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_WarehouseId_InventoryItemId_Status",
                table: "stock_reservations",
                columns: new[] { "WarehouseId", "InventoryItemId", "Status" });
        }
    }
}
