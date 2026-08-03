using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceProjectSite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_attendance_records_CompanyId_PersonnelId_WorkDate",
                table: "attendance_records");

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectSiteId",
                table: "attendance_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_attendance_records_CompanyId_PersonnelId_WorkDate",
                table: "attendance_records",
                columns: new[] { "CompanyId", "PersonnelId", "WorkDate" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_records_ProjectSiteId_WorkDate",
                table: "attendance_records",
                columns: new[] { "ProjectSiteId", "WorkDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_attendance_records_project_sites_ProjectSiteId",
                table: "attendance_records",
                column: "ProjectSiteId",
                principalTable: "project_sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_attendance_records_project_sites_ProjectSiteId",
                table: "attendance_records");

            migrationBuilder.DropIndex(
                name: "IX_attendance_records_CompanyId_PersonnelId_WorkDate",
                table: "attendance_records");

            migrationBuilder.DropIndex(
                name: "IX_attendance_records_ProjectSiteId_WorkDate",
                table: "attendance_records");

            migrationBuilder.DropColumn(
                name: "ProjectSiteId",
                table: "attendance_records");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_records_CompanyId_PersonnelId_WorkDate",
                table: "attendance_records",
                columns: new[] { "CompanyId", "PersonnelId", "WorkDate" },
                unique: true);
        }
    }
}
