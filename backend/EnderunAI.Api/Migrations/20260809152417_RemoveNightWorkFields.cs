using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <summary>
    /// Gece çalışması alanının kaldırılması.
    ///
    /// AYRI migration: bu bir KOLON DÜŞÜRME. Enderun'da gece
    /// çalışması/gece zammı yok; alan hiç yazılmadı (canlıda gece
    /// saati taşıyan tek puantaj kaydı bile yok) ve ücrete hiç
    /// dönüşmedi. Yeni alan ekleyen migration'la aynı dosyada olmasın
    /// diye ayrıldı: düşürme geri alınırken ekleme geri alınmasın.
    /// </summary>
    public partial class RemoveNightWorkFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NightShiftHours",
                table: "attendance_records");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "NightShiftHours",
                table: "attendance_records",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
