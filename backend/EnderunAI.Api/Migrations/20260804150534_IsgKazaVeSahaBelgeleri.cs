using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class IsgKazaVeSahaBelgeleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "isg_incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectSiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    PersonnelId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncidentDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IncidentType = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RootCause = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ActionTaken = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LostWorkDays = table.Column<int>(type: "integer", nullable: false),
                    SgkNotified = table.Column<bool>(type: "boolean", nullable: false),
                    SgkNotificationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SgkNotificationNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReportedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosureNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_isg_incidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_isg_incidents_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_isg_incidents_personnel_PersonnelId",
                        column: x => x.PersonnelId,
                        principalTable: "personnel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_isg_incidents_project_sites_ProjectSiteId",
                        column: x => x.ProjectSiteId,
                        principalTable: "project_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_isg_incidents_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "isg_site_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectSiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    StoredFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_isg_site_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_isg_site_documents_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_isg_site_documents_project_sites_ProjectSiteId",
                        column: x => x.ProjectSiteId,
                        principalTable: "project_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_isg_site_documents_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_isg_incidents_CompanyId_IncidentDateTime",
                table: "isg_incidents",
                columns: new[] { "CompanyId", "IncidentDateTime" });

            migrationBuilder.CreateIndex(
                name: "IX_isg_incidents_CompanyId_SgkNotified",
                table: "isg_incidents",
                columns: new[] { "CompanyId", "SgkNotified" });

            migrationBuilder.CreateIndex(
                name: "IX_isg_incidents_CompanyId_Status",
                table: "isg_incidents",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_isg_incidents_PersonnelId",
                table: "isg_incidents",
                column: "PersonnelId");

            migrationBuilder.CreateIndex(
                name: "IX_isg_incidents_ProjectId",
                table: "isg_incidents",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_isg_incidents_ProjectSiteId",
                table: "isg_incidents",
                column: "ProjectSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_isg_site_documents_CompanyId_ValidUntil",
                table: "isg_site_documents",
                columns: new[] { "CompanyId", "ValidUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_isg_site_documents_ProjectId_DocumentType",
                table: "isg_site_documents",
                columns: new[] { "ProjectId", "DocumentType" });

            migrationBuilder.CreateIndex(
                name: "IX_isg_site_documents_ProjectSiteId",
                table: "isg_site_documents",
                column: "ProjectSiteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "isg_incidents");

            migrationBuilder.DropTable(
                name: "isg_site_documents");
        }
    }
}
