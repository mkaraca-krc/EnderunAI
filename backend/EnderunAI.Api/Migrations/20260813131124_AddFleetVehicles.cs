using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFleetVehicles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VehicleId",
                table: "expense_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlateNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Ownership = table.Column<int>(type: "integer", nullable: false),
                    Brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ChassisNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ModelYear = table.Column<int>(type: "integer", nullable: true),
                    FuelType = table.Column<int>(type: "integer", nullable: true),
                    LessorCurrentAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    RentAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    RentPeriod = table.Column<int>(type: "integer", nullable: true),
                    RentDueDay = table.Column<int>(type: "integer", nullable: true),
                    PurchaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PurchaseCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    InspectionDueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InsuranceRenewalDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CascoRenewalDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MotorTaxDueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextMaintenanceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vehicles_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_vehicles_current_accounts_LessorCurrentAccountId",
                        column: x => x.LessorCurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectSiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverPersonnelId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReferenceKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_vehicle_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vehicle_assignments_personnel_DriverPersonnelId",
                        column: x => x.DriverPersonnelId,
                        principalTable: "personnel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_vehicle_assignments_project_sites_ProjectSiteId",
                        column: x => x.ProjectSiteId,
                        principalTable: "project_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vehicle_assignments_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vehicle_assignments_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expense_entries_VehicleId",
                table: "expense_entries",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_assignments_DriverPersonnelId",
                table: "vehicle_assignments",
                column: "DriverPersonnelId");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_assignments_open_per_vehicle",
                table: "vehicle_assignments",
                column: "VehicleId",
                unique: true,
                filter: "\"EndDate\" IS NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_assignments_ProjectId",
                table: "vehicle_assignments",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_assignments_ProjectSiteId",
                table: "vehicle_assignments",
                column: "ProjectSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_assignments_VehicleId_ReferenceKey",
                table: "vehicle_assignments",
                columns: new[] { "VehicleId", "ReferenceKey" },
                unique: true,
                filter: "\"ReferenceKey\" IS NOT NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_assignments_VehicleId_StartDate",
                table: "vehicle_assignments",
                columns: new[] { "VehicleId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_CompanyId_PlateNumber",
                table: "vehicles",
                columns: new[] { "CompanyId", "PlateNumber" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_LessorCurrentAccountId",
                table: "vehicles",
                column: "LessorCurrentAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_expense_entries_vehicles_VehicleId",
                table: "expense_entries",
                column: "VehicleId",
                principalTable: "vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_expense_entries_vehicles_VehicleId",
                table: "expense_entries");

            migrationBuilder.DropTable(
                name: "vehicle_assignments");

            migrationBuilder.DropTable(
                name: "vehicles");

            migrationBuilder.DropIndex(
                name: "IX_expense_entries_VehicleId",
                table: "expense_entries");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "expense_entries");
        }
    }
}
