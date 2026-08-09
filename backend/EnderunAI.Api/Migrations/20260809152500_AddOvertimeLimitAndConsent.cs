using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <summary>
    /// Yıllık fazla mesai sınırı parametresi ve mesai muvafakati.
    /// İkisi de yalnızca EKLEME; veri kaybı riski taşımaz.
    /// </summary>
    public partial class AddOvertimeLimitAndConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AnnualOvertimeHourLimit",
                table: "company_payroll_settings",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OvertimeConsentYear",
                table: "personnel",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OvertimeConsentDate",
                table: "personnel",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OvertimeConsentDate",
                table: "personnel");

            migrationBuilder.DropColumn(
                name: "OvertimeConsentYear",
                table: "personnel");

            migrationBuilder.DropColumn(
                name: "AnnualOvertimeHourLimit",
                table: "company_payroll_settings");
        }
    }
}
