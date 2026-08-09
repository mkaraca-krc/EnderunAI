using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompensationExemptionCaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsInKindBenefit",
                table: "hr_compensation_components",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MealIncomeTaxExemptionDailyCap",
                table: "company_payroll_settings",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MealSgkExemptionDailyCap",
                table: "company_payroll_settings",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TravelIncomeTaxExemptionDailyCap",
                table: "company_payroll_settings",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TravelSgkExemptionDailyCap",
                table: "company_payroll_settings",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsInKindBenefit",
                table: "hr_compensation_components");

            migrationBuilder.DropColumn(
                name: "MealIncomeTaxExemptionDailyCap",
                table: "company_payroll_settings");

            migrationBuilder.DropColumn(
                name: "MealSgkExemptionDailyCap",
                table: "company_payroll_settings");

            migrationBuilder.DropColumn(
                name: "TravelIncomeTaxExemptionDailyCap",
                table: "company_payroll_settings");

            migrationBuilder.DropColumn(
                name: "TravelSgkExemptionDailyCap",
                table: "company_payroll_settings");
        }
    }
}
