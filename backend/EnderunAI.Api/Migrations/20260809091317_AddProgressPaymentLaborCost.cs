using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressPaymentLaborCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ProgressPaymentCompensationCost",
                table: "hr_project_labor_costs",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProgressPaymentCost",
                table: "hr_project_labor_costs",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Mevcut satırlar: hakedişe yansıyan maliyet bugüne kadar
            // TotalLaborCost'un kendisiydi. Sıfır bırakmak, geçmiş
            // hakediş kârını bir gecede sıfır maliyetli göstermek
            // olurdu.
            migrationBuilder.Sql(
                """
                UPDATE hr_project_labor_costs
                SET "ProgressPaymentCost" = "TotalLaborCost",
                    "ProgressPaymentCompensationCost" = "CompensationCost";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProgressPaymentCompensationCost",
                table: "hr_project_labor_costs");

            migrationBuilder.DropColumn(
                name: "ProgressPaymentCost",
                table: "hr_project_labor_costs");
        }
    }
}
