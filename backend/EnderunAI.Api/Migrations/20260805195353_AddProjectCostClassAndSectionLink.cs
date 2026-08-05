using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectCostClassAndSectionLink : Migration
    {
        /// <summary>
        /// Maliyet sınıfı ve icmal kısmı bağlantısı.
        ///
        /// CostClass kullanıcıdan alınmaz, kaynaktan türetilir. Mevcut
        /// kayıtlar da aynı mantıkla geriye dönük sınıflanır (aşağıdaki
        /// SQL): stok sarfı malzeme, tedarikçi faturası kalemin gider
        /// hesabına göre, elle giriş kullanıcının seçtiği türe göre.
        /// Sınıflama yapılmasaydı geçmiş maliyetlerin tamamı varsayılan
        /// değerde (Malzeme) kalır ve karşılaştırma ilk günden yanlış
        /// olurdu.
        ///
        /// ProjectHakedisSectionId her yerde OPSİYONEL: kısımsız kayıt
        /// proje geneline yazılır ve analizde "Genel" satırında toplanır.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectHakedisSectionId",
                table: "stock_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CostClass",
                table: "ProjectCostTransactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectHakedisSectionId",
                table: "ProjectCostTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectHakedisSectionId",
                table: "hr_project_labor_costs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectHakedisSectionId",
                table: "attendance_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ProjectHakedisSectionId",
                table: "stock_movements",
                column: "ProjectHakedisSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCostTransactions_ProjectHakedisSectionId",
                table: "ProjectCostTransactions",
                column: "ProjectHakedisSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCostTransactions_ProjectId_CostClass",
                table: "ProjectCostTransactions",
                columns: new[] { "ProjectId", "CostClass" });

            migrationBuilder.CreateIndex(
                name: "IX_hr_project_labor_costs_ProjectHakedisSectionId",
                table: "hr_project_labor_costs",
                column: "ProjectHakedisSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_records_ProjectHakedisSectionId",
                table: "attendance_records",
                column: "ProjectHakedisSectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_attendance_records_project_hakedis_sections_ProjectHakedisS~",
                table: "attendance_records",
                column: "ProjectHakedisSectionId",
                principalTable: "project_hakedis_sections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectCostTransactions_project_hakedis_sections_ProjectHak~",
                table: "ProjectCostTransactions",
                column: "ProjectHakedisSectionId",
                principalTable: "project_hakedis_sections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_project_hakedis_sections_ProjectHakedisSect~",
                table: "stock_movements",
                column: "ProjectHakedisSectionId",
                principalTable: "project_hakedis_sections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // --- Geriye dönük sınıflama ---
            // 0 Malzeme, 1 İşçilik, 2 İşçilik (Taşeron), 3 Genel Gider.

            // Depo sarfı her zaman malzemedir.
            migrationBuilder.Sql(
                "UPDATE \"ProjectCostTransactions\" SET \"CostClass\" = 0 " +
                "WHERE \"ReferenceType\" = 'StockMovement';");

            // Tedarikçi faturası: ALIŞ (stok) faturası malzeme, GİDER
            // faturası kalemlerinin gider hesabına göre. Bir faturanın
            // kalemleri farklı sınıflara düşüyorsa EN BÜYÜK PAYA sahip
            // sınıf yazılır: geçmiş kayıtlar tek satır halinde tutulduğu
            // için bölünemiyor. Bundan sonraki faturalar sınıf başına
            // ayrı maliyet satırı üretiyor.
            migrationBuilder.Sql(
                "WITH baskin AS ( " +
                "  SELECT pct.\"Id\" AS maliyet_id, " +
                "    CASE " +
                "      WHEN si.\"InvoiceType\" = 0 THEN 0 " +
                "      WHEN aa.\"Code\" LIKE '740.03.11%' THEN 2 " +
                "      WHEN aa.\"Code\" LIKE '740.01%' OR aa.\"Code\" LIKE '770.01%' " +
                "        OR aa.\"Code\" LIKE '720%' THEN 1 " +
                "      ELSE 3 " +
                "    END AS sinif, " +
                "    ROW_NUMBER() OVER ( " +
                "      PARTITION BY pct.\"Id\" " +
                "      ORDER BY COALESCE(SUM(sii.\"LineSubtotal\"), 0) DESC " +
                "    ) AS sira " +
                "  FROM \"ProjectCostTransactions\" pct " +
                "  JOIN supplier_invoices si ON si.\"Id\" = pct.\"ReferenceId\" " +
                "  LEFT JOIN supplier_invoice_items sii " +
                "    ON sii.\"SupplierInvoiceId\" = si.\"Id\" AND sii.\"IsDeleted\" = FALSE " +
                "  LEFT JOIN accounting_accounts aa ON aa.\"Id\" = sii.\"ExpenseAccountId\" " +
                "  WHERE pct.\"ReferenceType\" = 'SupplierInvoice' " +
                "  GROUP BY pct.\"Id\", si.\"InvoiceType\", aa.\"Code\" " +
                ") " +
                "UPDATE \"ProjectCostTransactions\" pct " +
                "SET \"CostClass\" = baskin.sinif " +
                "FROM baskin " +
                "WHERE pct.\"Id\" = baskin.maliyet_id AND baskin.sira = 1;");

            // Elle girilen kayıtlar kullanıcının seçtiği türden eşlenir:
            // Malzeme→Malzeme, İşçilik→İşçilik, Taşeron→Taşeron işçiliği,
            // Ekipman/Genel/Diğer→Genel Gider.
            migrationBuilder.Sql(
                "UPDATE \"ProjectCostTransactions\" " +
                "SET \"CostClass\" = CASE \"CostType\" " +
                "  WHEN 0 THEN 0 WHEN 1 THEN 1 WHEN 3 THEN 2 ELSE 3 END " +
                "WHERE \"ReferenceType\" IS NULL " +
                "   OR \"ReferenceType\" NOT IN ('StockMovement', 'SupplierInvoice');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_attendance_records_project_hakedis_sections_ProjectHakedisS~",
                table: "attendance_records");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectCostTransactions_project_hakedis_sections_ProjectHak~",
                table: "ProjectCostTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_project_hakedis_sections_ProjectHakedisSect~",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_ProjectHakedisSectionId",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_ProjectCostTransactions_ProjectHakedisSectionId",
                table: "ProjectCostTransactions");

            migrationBuilder.DropIndex(
                name: "IX_ProjectCostTransactions_ProjectId_CostClass",
                table: "ProjectCostTransactions");

            migrationBuilder.DropIndex(
                name: "IX_hr_project_labor_costs_ProjectHakedisSectionId",
                table: "hr_project_labor_costs");

            migrationBuilder.DropIndex(
                name: "IX_attendance_records_ProjectHakedisSectionId",
                table: "attendance_records");

            migrationBuilder.DropColumn(
                name: "ProjectHakedisSectionId",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "CostClass",
                table: "ProjectCostTransactions");

            migrationBuilder.DropColumn(
                name: "ProjectHakedisSectionId",
                table: "ProjectCostTransactions");

            migrationBuilder.DropColumn(
                name: "ProjectHakedisSectionId",
                table: "hr_project_labor_costs");

            migrationBuilder.DropColumn(
                name: "ProjectHakedisSectionId",
                table: "attendance_records");
        }
    }
}
