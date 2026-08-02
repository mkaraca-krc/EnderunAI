using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileProjectRateColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IncreaseRate/CashRetentionRate/WithholdingTaxRate/MaterialDeductionRate
            // Project.cs modelinde ve AppDbContextModelSnapshot.cs'de zaten vardı,
            // ama hiçbir migration bunları "projects" tablosuna eklemiyordu — canlı
            // veritabanına migration dışı, elle eklenmişlerdi. Bu migration, model ile
            // migration geçmişini gerçekten senkronize eder (sıfırdan kurulum da aynı
            // şemayı üretsin diye); canlıda kolonlar zaten var olduğu için no-op'tur.
            migrationBuilder.Sql(
                """
                ALTER TABLE "projects" ADD COLUMN IF NOT EXISTS "IncreaseRate" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE "projects" ADD COLUMN IF NOT EXISTS "CashRetentionRate" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE "projects" ADD COLUMN IF NOT EXISTS "WithholdingTaxRate" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE "projects" ADD COLUMN IF NOT EXISTS "MaterialDeductionRate" numeric(18,2) NOT NULL DEFAULT 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "projects" DROP COLUMN IF EXISTS "MaterialDeductionRate";
                ALTER TABLE "projects" DROP COLUMN IF EXISTS "WithholdingTaxRate";
                ALTER TABLE "projects" DROP COLUMN IF EXISTS "CashRetentionRate";
                ALTER TABLE "projects" DROP COLUMN IF EXISTS "IncreaseRate";
                """);
        }
    }
}
