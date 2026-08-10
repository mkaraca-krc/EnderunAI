using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyReportAndOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SurveyOutcome",
                table: "projects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SurveyOutcomeAtUtc",
                table: "projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SurveyOutcomeByUserId",
                table: "projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SurveyOutcomeNote",
                table: "projects",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "duty_survey_reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DutyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Summary = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    SiteConditions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    AccessNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Risks = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RecommendBid = table.Column<bool>(type: "boolean", nullable: true),
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
                    table.PrimaryKey("PK_duty_survey_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_duty_survey_reports_personnel_duties_DutyId",
                        column: x => x.DutyId,
                        principalTable: "personnel_duties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_duty_survey_reports_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "duty_survey_measurements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SurveyReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_duty_survey_measurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_duty_survey_measurements_duty_survey_reports_SurveyReportId",
                        column: x => x.SurveyReportId,
                        principalTable: "duty_survey_reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "duty_survey_photos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SurveyReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoredFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    OriginalName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_duty_survey_photos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_duty_survey_photos_duty_survey_reports_SurveyReportId",
                        column: x => x.SurveyReportId,
                        principalTable: "duty_survey_reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_duty_survey_measurements_SurveyReportId",
                table: "duty_survey_measurements",
                column: "SurveyReportId");

            migrationBuilder.CreateIndex(
                name: "IX_duty_survey_photos_SurveyReportId",
                table: "duty_survey_photos",
                column: "SurveyReportId");

            migrationBuilder.CreateIndex(
                name: "IX_duty_survey_reports_DutyId",
                table: "duty_survey_reports",
                column: "DutyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_duty_survey_reports_ProjectId",
                table: "duty_survey_reports",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "duty_survey_measurements");

            migrationBuilder.DropTable(
                name: "duty_survey_photos");

            migrationBuilder.DropTable(
                name: "duty_survey_reports");

            migrationBuilder.DropColumn(
                name: "SurveyOutcome",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "SurveyOutcomeAtUtc",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "SurveyOutcomeByUserId",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "SurveyOutcomeNote",
                table: "projects");
        }
    }
}
