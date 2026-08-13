using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteAwareUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_project_measurement_items_ProjectMeasurementId_LineNumber",
                table: "project_measurement_items");

            migrationBuilder.DropIndex(
                name: "IX_project_measurement_items_ProjectMeasurementId_ProjectBoqIt~",
                table: "project_measurement_items");

            migrationBuilder.DropIndex(
                name: "IX_progress_payment_payment_plans_ProgressPaymentId_LineNumber",
                table: "progress_payment_payment_plans");

            migrationBuilder.DropIndex(
                name: "IX_progress_payment_items_ProgressPaymentId_LineNumber",
                table: "progress_payment_items");

            migrationBuilder.DropIndex(
                name: "IX_progress_payment_deductions_ProgressPaymentId_LineNumber",
                table: "progress_payment_deductions");

            migrationBuilder.DropIndex(
                name: "IX_progress_payment_deduction_lines_ProgressPaymentDeductionId~",
                table: "progress_payment_deduction_lines");

            migrationBuilder.DropIndex(
                name: "IX_progress_payment_advance_materials_ProgressPaymentId_LineNu~",
                table: "progress_payment_advance_materials");

            migrationBuilder.CreateIndex(
                name: "IX_project_measurement_items_ProjectMeasurementId_LineNumber",
                table: "project_measurement_items",
                columns: new[] { "ProjectMeasurementId", "LineNumber" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_project_measurement_items_ProjectMeasurementId_ProjectBoqIt~",
                table: "project_measurement_items",
                columns: new[] { "ProjectMeasurementId", "ProjectBoqItemId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_payment_plans_ProgressPaymentId_LineNumber",
                table: "progress_payment_payment_plans",
                columns: new[] { "ProgressPaymentId", "LineNumber" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_items_ProgressPaymentId_LineNumber",
                table: "progress_payment_items",
                columns: new[] { "ProgressPaymentId", "LineNumber" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_deductions_ProgressPaymentId_LineNumber",
                table: "progress_payment_deductions",
                columns: new[] { "ProgressPaymentId", "LineNumber" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_deduction_lines_ProgressPaymentDeductionId~",
                table: "progress_payment_deduction_lines",
                columns: new[] { "ProgressPaymentDeductionId", "LineNumber" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_advance_materials_ProgressPaymentId_LineNu~",
                table: "progress_payment_advance_materials",
                columns: new[] { "ProgressPaymentId", "LineNumber" },
                unique: true,
                filter: "NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_project_measurement_items_ProjectMeasurementId_LineNumber",
                table: "project_measurement_items");

            migrationBuilder.DropIndex(
                name: "IX_project_measurement_items_ProjectMeasurementId_ProjectBoqIt~",
                table: "project_measurement_items");

            migrationBuilder.DropIndex(
                name: "IX_progress_payment_payment_plans_ProgressPaymentId_LineNumber",
                table: "progress_payment_payment_plans");

            migrationBuilder.DropIndex(
                name: "IX_progress_payment_items_ProgressPaymentId_LineNumber",
                table: "progress_payment_items");

            migrationBuilder.DropIndex(
                name: "IX_progress_payment_deductions_ProgressPaymentId_LineNumber",
                table: "progress_payment_deductions");

            migrationBuilder.DropIndex(
                name: "IX_progress_payment_deduction_lines_ProgressPaymentDeductionId~",
                table: "progress_payment_deduction_lines");

            migrationBuilder.DropIndex(
                name: "IX_progress_payment_advance_materials_ProgressPaymentId_LineNu~",
                table: "progress_payment_advance_materials");

            migrationBuilder.CreateIndex(
                name: "IX_project_measurement_items_ProjectMeasurementId_LineNumber",
                table: "project_measurement_items",
                columns: new[] { "ProjectMeasurementId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_measurement_items_ProjectMeasurementId_ProjectBoqIt~",
                table: "project_measurement_items",
                columns: new[] { "ProjectMeasurementId", "ProjectBoqItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_payment_plans_ProgressPaymentId_LineNumber",
                table: "progress_payment_payment_plans",
                columns: new[] { "ProgressPaymentId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_items_ProgressPaymentId_LineNumber",
                table: "progress_payment_items",
                columns: new[] { "ProgressPaymentId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_deductions_ProgressPaymentId_LineNumber",
                table: "progress_payment_deductions",
                columns: new[] { "ProgressPaymentId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_deduction_lines_ProgressPaymentDeductionId~",
                table: "progress_payment_deduction_lines",
                columns: new[] { "ProgressPaymentDeductionId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_advance_materials_ProgressPaymentId_LineNu~",
                table: "progress_payment_advance_materials",
                columns: new[] { "ProgressPaymentId", "LineNumber" },
                unique: true);
        }
    }
}
