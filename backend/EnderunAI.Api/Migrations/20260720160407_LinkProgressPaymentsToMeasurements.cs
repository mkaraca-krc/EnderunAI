using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class LinkProgressPaymentsToMeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectMeasurementId",
                table: "progress_payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_progress_payments_ProjectMeasurementId",
                table: "progress_payments",
                column: "ProjectMeasurementId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_progress_payments_project_measurements_ProjectMeasurementId",
                table: "progress_payments",
                column: "ProjectMeasurementId",
                principalTable: "project_measurements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_progress_payments_project_measurements_ProjectMeasurementId",
                table: "progress_payments");

            migrationBuilder.DropIndex(
                name: "IX_progress_payments_ProjectMeasurementId",
                table: "progress_payments");

            migrationBuilder.DropColumn(
                name: "ProjectMeasurementId",
                table: "progress_payments");
        }
    }
}
