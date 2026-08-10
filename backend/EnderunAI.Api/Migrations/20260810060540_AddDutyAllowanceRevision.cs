using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDutyAllowanceRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AllowanceRevisedAtUtc",
                table: "personnel_duties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AllowanceRevisedByUserId",
                table: "personnel_duties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowanceRevisionNote",
                table: "personnel_duties",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowanceRevisedAtUtc",
                table: "personnel_duties");

            migrationBuilder.DropColumn(
                name: "AllowanceRevisedByUserId",
                table: "personnel_duties");

            migrationBuilder.DropColumn(
                name: "AllowanceRevisionNote",
                table: "personnel_duties");
        }
    }
}
