using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectScheduleGantt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    WorkWeek = table.Column<int>(type: "integer", nullable: false),
                    BaselineRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    BaselineSetAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BaselineSetByUserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_project_schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_schedules_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "schedule_activities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentActivityId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectHakedisSectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectBoqItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PlannedStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BaselineStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BaselineEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ManualProgressRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
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
                    table.PrimaryKey("PK_schedule_activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_schedule_activities_project_boq_items_ProjectBoqItemId",
                        column: x => x.ProjectBoqItemId,
                        principalTable: "project_boq_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_schedule_activities_project_hakedis_sections_ProjectHakedis~",
                        column: x => x.ProjectHakedisSectionId,
                        principalTable: "project_hakedis_sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_schedule_activities_project_schedules_ProjectScheduleId",
                        column: x => x.ProjectScheduleId,
                        principalTable: "project_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_schedule_activities_schedule_activities_ParentActivityId",
                        column: x => x.ParentActivityId,
                        principalTable: "schedule_activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "schedule_baseline_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    SetAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SetByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActivityCount = table.Column<int>(type: "integer", nullable: false),
                    PlannedStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PlannedEndDate = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_schedule_baseline_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_schedule_baseline_revisions_project_schedules_ProjectSchedu~",
                        column: x => x.ProjectScheduleId,
                        principalTable: "project_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "schedule_holidays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_schedule_holidays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_schedule_holidays_project_schedules_ProjectScheduleId",
                        column: x => x.ProjectScheduleId,
                        principalTable: "project_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "schedule_dependencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PredecessorActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    SuccessorActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    LagWorkDays = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_schedule_dependencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_schedule_dependencies_project_schedules_ProjectScheduleId",
                        column: x => x.ProjectScheduleId,
                        principalTable: "project_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_schedule_dependencies_schedule_activities_PredecessorActivi~",
                        column: x => x.PredecessorActivityId,
                        principalTable: "schedule_activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_schedule_dependencies_schedule_activities_SuccessorActivity~",
                        column: x => x.SuccessorActivityId,
                        principalTable: "schedule_activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "schedule_resource_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PersonnelId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubcontractorContractId = table.Column<Guid>(type: "uuid", nullable: true),
                    Role = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_schedule_resource_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_schedule_resource_assignments_personnel_PersonnelId",
                        column: x => x.PersonnelId,
                        principalTable: "personnel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_schedule_resource_assignments_schedule_activities_ScheduleA~",
                        column: x => x.ScheduleActivityId,
                        principalTable: "schedule_activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_schedule_resource_assignments_subcontractor_contracts_Subco~",
                        column: x => x.SubcontractorContractId,
                        principalTable: "subcontractor_contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_project_schedules_ProjectId",
                table: "project_schedules",
                column: "ProjectId",
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_activities_ParentActivityId",
                table: "schedule_activities",
                column: "ParentActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_activities_ProjectBoqItemId",
                table: "schedule_activities",
                column: "ProjectBoqItemId");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_activities_ProjectHakedisSectionId",
                table: "schedule_activities",
                column: "ProjectHakedisSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_activities_ProjectScheduleId",
                table: "schedule_activities",
                column: "ProjectScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_baseline_revisions_ProjectScheduleId_RevisionNumber",
                table: "schedule_baseline_revisions",
                columns: new[] { "ProjectScheduleId", "RevisionNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_dependencies_PredecessorActivityId",
                table: "schedule_dependencies",
                column: "PredecessorActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_dependencies_ProjectScheduleId_PredecessorActivity~",
                table: "schedule_dependencies",
                columns: new[] { "ProjectScheduleId", "PredecessorActivityId", "SuccessorActivityId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_dependencies_SuccessorActivityId",
                table: "schedule_dependencies",
                column: "SuccessorActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_holidays_ProjectScheduleId_Date",
                table: "schedule_holidays",
                columns: new[] { "ProjectScheduleId", "Date" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_resource_assignments_PersonnelId",
                table: "schedule_resource_assignments",
                column: "PersonnelId");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_resource_assignments_ScheduleActivityId",
                table: "schedule_resource_assignments",
                column: "ScheduleActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_resource_assignments_SubcontractorContractId",
                table: "schedule_resource_assignments",
                column: "SubcontractorContractId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schedule_baseline_revisions");

            migrationBuilder.DropTable(
                name: "schedule_dependencies");

            migrationBuilder.DropTable(
                name: "schedule_holidays");

            migrationBuilder.DropTable(
                name: "schedule_resource_assignments");

            migrationBuilder.DropTable(
                name: "schedule_activities");

            migrationBuilder.DropTable(
                name: "project_schedules");
        }
    }
}
