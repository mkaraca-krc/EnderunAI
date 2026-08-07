using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddToolAssetsAndService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ToolAssetId",
                table: "hr_asset_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tool_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Brand = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Model = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PurchaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PurchaseCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    WarrantyEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LocationType = table.Column<int>(type: "integer", nullable: false),
                    ProjectSiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedPersonnelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_tool_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tool_assets_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tool_assets_personnel_AssignedPersonnelId",
                        column: x => x.AssignedPersonnelId,
                        principalTable: "personnel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tool_assets_project_sites_ProjectSiteId",
                        column: x => x.ProjectSiteId,
                        principalTable: "project_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tool_service_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToolAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectSiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FaultDescription = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Urgency = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    DecisionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ServiceProviderName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ServiceCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ProjectCostTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReplacementPurchaseRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransferredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_tool_service_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tool_service_requests_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tool_service_requests_project_sites_ProjectSiteId",
                        column: x => x.ProjectSiteId,
                        principalTable: "project_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tool_service_requests_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tool_service_requests_tool_assets_ToolAssetId",
                        column: x => x.ToolAssetId,
                        principalTable: "tool_assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_hr_asset_assignments_ToolAssetId_Status",
                table: "hr_asset_assignments",
                columns: new[] { "ToolAssetId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_tool_assets_AssignedPersonnelId",
                table: "tool_assets",
                column: "AssignedPersonnelId");

            migrationBuilder.CreateIndex(
                name: "IX_tool_assets_CompanyId_Code",
                table: "tool_assets",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_tool_assets_CompanyId_SerialNumber",
                table: "tool_assets",
                columns: new[] { "CompanyId", "SerialNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"SerialNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tool_assets_ProjectSiteId",
                table: "tool_assets",
                column: "ProjectSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_tool_service_requests_CompanyId_RequestNumber",
                table: "tool_service_requests",
                columns: new[] { "CompanyId", "RequestNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_tool_service_requests_ProjectId_RequestDate",
                table: "tool_service_requests",
                columns: new[] { "ProjectId", "RequestDate" });

            migrationBuilder.CreateIndex(
                name: "IX_tool_service_requests_ProjectSiteId",
                table: "tool_service_requests",
                column: "ProjectSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_tool_service_requests_ToolAssetId_Status",
                table: "tool_service_requests",
                columns: new[] { "ToolAssetId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_hr_asset_assignments_tool_assets_ToolAssetId",
                table: "hr_asset_assignments",
                column: "ToolAssetId",
                principalTable: "tool_assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_hr_asset_assignments_tool_assets_ToolAssetId",
                table: "hr_asset_assignments");

            migrationBuilder.DropTable(
                name: "tool_service_requests");

            migrationBuilder.DropTable(
                name: "tool_assets");

            migrationBuilder.DropIndex(
                name: "IX_hr_asset_assignments_ToolAssetId_Status",
                table: "hr_asset_assignments");

            migrationBuilder.DropColumn(
                name: "ToolAssetId",
                table: "hr_asset_assignments");
        }
    }
}
