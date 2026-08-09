using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations.HumanResources
{
    /// <inheritdoc />
    public partial class OvertimeAttendanceBridge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsNightWork",
                table: "hr_overtime_requests");

            migrationBuilder.AddColumn<Guid>(
                name: "AttendanceRecordId",
                table: "hr_overtime_requests",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttendanceRecordId",
                table: "hr_overtime_requests");

            migrationBuilder.AddColumn<bool>(
                name: "IsNightWork",
                table: "hr_overtime_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
