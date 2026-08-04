using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class PersonelGorevYeri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkLocationType",
                table: "personnel",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_personnel_CompanyId_WorkLocationType",
                table: "personnel",
                columns: new[] { "CompanyId", "WorkLocationType" });

            // Halihazırda aktif şantiye ataması olan personel zaten
            // şantiyede çalışıyor; hepsini "atama bekliyor" göstermek
            // ilk açılışta sahte bir uyarı yığını üretirdi.
            //
            // Ataması olmayanlar Unassigned (0) kalır — merkezde
            // olduklarını VARSAYMIYORUZ; şube idari bir bilgi, fiili
            // görev yeri değil. İK bunları ekrandan işaretleyecek.
            migrationBuilder.Sql(@"
                UPDATE personnel p
                   SET ""WorkLocationType"" = 2
                 WHERE EXISTS (
                     SELECT 1 FROM project_site_assignments a
                      WHERE a.""PersonnelId"" = p.""Id""
                        AND a.""IsActive"" = true
                        AND a.""IsDeleted"" = false
                        AND a.""EndDate"" IS NULL
                 );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_personnel_CompanyId_WorkLocationType",
                table: "personnel");

            migrationBuilder.DropColumn(
                name: "WorkLocationType",
                table: "personnel");
        }
    }
}
