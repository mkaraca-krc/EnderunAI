using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixPayrollSettingsWorkWeekDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Kolon 0 varsayılanıyla eklenmişti; MEVCUT satırlar "hiçbir
            // gün çalışılmıyor" durumunda kaldı. Ekran boş bir hafta
            // gösterir ve süre hesabı yapılamazdı. Önce veri düzeltiliyor,
            // sonra kolon varsayılanı Pazartesi–Cumartesi'ye çekiliyor.
            migrationBuilder.Sql(
                "UPDATE company_payroll_settings SET \"WorkWeek\" = 63 " +
                "WHERE \"WorkWeek\" <= 0;");

            migrationBuilder.AlterColumn<int>(
                name: "WorkWeek",
                table: "company_payroll_settings",
                type: "integer",
                nullable: false,
                defaultValue: 63,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "WorkWeek",
                table: "company_payroll_settings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 63);
        }
    }
}
