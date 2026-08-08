using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations.HumanResources
{
    /// <summary>
    /// Avans kesinti defteri.
    ///
    /// NOT: bu, HrDbContext için model anlık görüntüsü (snapshot) olan
    /// ilk göç. Önceki HR göçleri elle yazılmış ve snapshot
    /// bırakmamıştı; bu yüzden EF, üretilen göçe MEVCUT sekiz tabloyu
    /// da "yeniden oluştur" diye eklemişti. O kısım elle çıkarıldı —
    /// göç yalnızca yeni tabloyu oluşturuyor. Snapshot dosyası
    /// korunuyor: bundan sonraki göçler doğru bir temele göre
    /// üretilecek.
    /// </summary>
    public partial class AddAdvanceDeductionLedger : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_advance_deductions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvanceRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonnelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ScheduledAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hr_advance_deductions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_hr_advance_deductions_AdvanceRequestId_Year_Month",
                table: "hr_advance_deductions",
                columns: new[] { "AdvanceRequestId", "Year", "Month" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_hr_advance_deductions_CompanyId_PersonnelId_Year_Month",
                table: "hr_advance_deductions",
                columns: new[] { "CompanyId", "PersonnelId", "Year", "Month" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "hr_advance_deductions");
        }
    }
}
