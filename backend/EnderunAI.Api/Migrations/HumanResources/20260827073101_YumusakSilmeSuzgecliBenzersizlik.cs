using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations.HumanResources
{
    /// <inheritdoc />
    public partial class YumusakSilmeSuzgecliBenzersizlik : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
             * ELLE YAZILMIŞ MÜKERRER İKİZLER KALDIRILIYOR.
             *
             * Bu üç indeks `20260727023000_AddHrMasterData` ve
             * `20260727093000_AlignHrPositionsLegacySchema` içinde ham
             * SQL ile kurulmuştu ve zaten SÜZGEÇLİYDİ. Modelin
             * ürettiği süzgeçsiz ikizleri onları sessizce eziyordu:
             * süzgeçsiz olan daha KATI olduğu için silinmiş kaydın
             * kodu yine rehin kalıyordu.
             *
             * Model artık süzgeci kendisi taşıdığı için ham SQL kopyası
             * gereksiz. EF onları TANIMIYOR (ham SQL anlık görüntüye
             * girmez), bu yüzden elle düşürülüyorlar — bırakılsalardı
             * aynı kısıt iki indeksle korunur, biri modelin biri
             * kimsenin sahipliğinde olurdu.
             */
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_hr_departments_Company_Code\";");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_hr_positions_Company_Code\";");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_hr_positions_Department_Code\";");

            /*
             * MODELİN ADLARI DA KOŞULLU DÜŞÜRÜLÜYOR — `DropIndex` DEĞİL.
             *
             * `migrationBuilder.DropIndex` indeksin VAR OLDUĞUNU
             * varsayar. Burada varsayamayız: EF'in anlık görüntüsü
             * `IX_hr_positions_DepartmentId_Code`in var olduğunu
             * söylüyordu ama o ad hiçbir veritabanında YOK — aynı
             * sütunlar elle yazılmış `IX_hr_positions_Department_Code`
             * adıyla duruyordu. Göç `DropIndex` ile yazıldığında
             * "42704: index does not exist" ile düştü.
             *
             * Anlık görüntü ile fiziksel şemanın ayrışması D1 testinin
             * göremediği bir sınıftır: D1 modeli ANLIK GÖRÜNTÜYLE
             * karşılaştırır, veritabanıyla değil.
             */
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_hr_departments_CompanyId_Code\";");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_hr_positions_CompanyId_Code\";");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_hr_positions_DepartmentId_Code\";");

            migrationBuilder.CreateIndex(
                name: "IX_hr_positions_CompanyId_Code",
                table: "hr_positions",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_hr_positions_DepartmentId_Code",
                table: "hr_positions",
                columns: new[] { "DepartmentId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_hr_departments_CompanyId_Code",
                table: "hr_departments",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // GERİ ALINABİLİR: ham SQL ikizleri eski hâlleriyle
            // (süzgeçli) yeniden kuruluyor.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_hr_departments_Company_Code\" " +
                "ON hr_departments (\"CompanyId\", \"Code\") WHERE \"IsDeleted\" = FALSE;");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_hr_positions_Company_Code\" " +
                "ON hr_positions (\"CompanyId\", \"Code\") WHERE \"IsDeleted\" = FALSE;");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_hr_positions_Department_Code\" " +
                "ON hr_positions (\"DepartmentId\", \"Code\") WHERE \"IsDeleted\" = FALSE;");

            migrationBuilder.CreateIndex(
                name: "IX_hr_positions_CompanyId_Code",
                table: "hr_positions",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_hr_positions_DepartmentId_Code",
                table: "hr_positions",
                columns: new[] { "DepartmentId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_hr_departments_CompanyId_Code",
                table: "hr_departments",
                columns: new[] { "CompanyId", "Code" },
                unique: true);
        }
    }
}
