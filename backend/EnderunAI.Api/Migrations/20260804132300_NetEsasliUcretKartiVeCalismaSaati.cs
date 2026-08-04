using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <summary>
    /// Net esaslı ücret kartı ve günlük çalışma süresi parametresi.
    ///
    /// hr_salary_definitions tablosu HrDbContext'e ait olduğu ve
    /// AppDbContext modeline girmediği için o kolonlar elle yazıldı —
    /// modülün mevcut deseni budur
    /// (bkz. 20260803125421_AddPayrollCalculationColumns).
    /// </summary>
    public partial class NetEsasliUcretKartiVeCalismaSaati : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Günlük normal çalışma süresi. Daha önce 225 (30 × 7,5)
            // olarak koda iki ayrı yere gömülüydü; biri güncellenip
            // diğeri unutulabilirdi.
            migrationBuilder.AddColumn<decimal>(
                name: "DailyWorkHours",
                table: "company_payroll_settings",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 7.5m);

            // Mevcut satırlar da yasal varsayılana çekilir; sıfır kalsaydı
            // saatlik ücret hesabı bozulurdu.
            migrationBuilder.Sql(@"
                UPDATE company_payroll_settings
                   SET ""DailyWorkHours"" = 7.5
                 WHERE ""DailyWorkHours"" <= 0;
            ");

            // Ücret kartı esası. Varsayılan 0 = brüt esaslı: mevcut
            // kartların tamamı bugünkü davranışı aynen sürdürür.
            migrationBuilder.Sql(@"
                ALTER TABLE hr_salary_definitions
                    ADD COLUMN IF NOT EXISTS ""SalaryBasis"" integer NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS ""TargetNetSalary"" numeric(18,2) NOT NULL DEFAULT 0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE hr_salary_definitions
                    DROP COLUMN IF EXISTS ""SalaryBasis"",
                    DROP COLUMN IF EXISTS ""TargetNetSalary"";
            ");

            migrationBuilder.DropColumn(
                name: "DailyWorkHours",
                table: "company_payroll_settings");
        }
    }
}
