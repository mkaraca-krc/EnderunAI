using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRehireAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RehireCode",
                table: "personnel_terminations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RehireMarkedAtUtc",
                table: "personnel_terminations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RehireMarkedByUserId",
                table: "personnel_terminations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RehireNote",
                table: "personnel_terminations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RehireCode",
                table: "personnel_terminations");

            migrationBuilder.DropColumn(
                name: "RehireMarkedAtUtc",
                table: "personnel_terminations");

            migrationBuilder.DropColumn(
                name: "RehireMarkedByUserId",
                table: "personnel_terminations");

            migrationBuilder.DropColumn(
                name: "RehireNote",
                table: "personnel_terminations");
        }
    }
}
