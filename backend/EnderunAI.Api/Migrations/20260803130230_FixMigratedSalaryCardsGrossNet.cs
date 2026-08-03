using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <summary>
    /// Bir önceki migration, Personnel.MonthlySalary alanını BRÜT kabul
    /// edip ücret kartına öyle taşımıştı. Canlı veri bunun yanlış
    /// olduğunu gösterdi: 80 personelin 75'inde değer tam olarak net
    /// asgari ücret (28.075,50). Alan NET tutuyor.
    ///
    /// Yanlış varsayımla bırakılsaydı bu 75 kişinin bordrosu asgari
    /// ücretin altında bir net üretirdi. Burada taşınan kartların brüt
    /// ve net alanları düzeltiliyor: eldeki değer net kabul edilip brüt
    /// geri hesaplanıyor.
    ///
    /// Brüt = net / 0,85 — asgari ücretlide tam doğru (gelir ve damga
    /// vergisi istisnası nedeniyle yalnızca %14 SGK + %1 işsizlik
    /// kesilir), asgari ücret üstünde yaklaşıktır. Bu yüzden kayıtlar
    /// "doğrulanmalı" notunu korur.
    /// </summary>
    public partial class FixMigratedSalaryCardsGrossNet : Migration
    {
        private const string MigratedNote =
            "Personel kartındaki maaştan taşındı - brüt/net doğrulanmalı";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE hr_salary_definitions
                   SET ""NetSalary""   = ""GrossSalary"",
                       ""GrossSalary"" = ROUND(""GrossSalary"" / 0.85, 2),
                       ""DailyRate""   = ROUND((""GrossSalary"" / 0.85) / 30, 2),
                       ""HourlyRate""  = ROUND((""GrossSalary"" / 0.85) / 225, 2)
                 WHERE ""Description"" = '{MigratedNote}';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE hr_salary_definitions
                   SET ""GrossSalary"" = ""NetSalary"",
                       ""NetSalary""   = ROUND(""NetSalary"" * 0.85, 2),
                       ""DailyRate""   = ROUND(""NetSalary"" / 30, 2),
                       ""HourlyRate""  = ROUND(""NetSalary"" / 225, 2)
                 WHERE ""Description"" = '{MigratedNote}';
            ");
        }
    }
}
