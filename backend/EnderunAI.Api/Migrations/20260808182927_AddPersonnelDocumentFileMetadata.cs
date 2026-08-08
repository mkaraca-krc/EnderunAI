using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <summary>
    /// Özlük belgesi dosya künyesi.
    ///
    /// NOT: <c>hr_personnel_documents</c> tablosu canlıda ZATEN VARDI —
    /// modeli, ucu ve ekranı olmayan, terk edilmiş bir tasarımdan kalma
    /// ve boş. EF'in göç geçmişinde olmadığı için üretilen göç tabloyu
    /// "yeniden oluştur" diye yazmıştı; o kısım elle çıkarıldı. Göç
    /// yalnızca eksik üç kolonu ekliyor.
    ///
    /// Kolonlar eklendi çünkü mevcut tasarımda yalnızca FilePath vardı;
    /// indirirken kullanıcıya dosyanın özgün adını ve doğru içerik
    /// tipini vermek için künye gerekiyor.
    /// </summary>
    public partial class AddPersonnelDocumentFileMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE hr_personnel_documents " +
                "ADD COLUMN IF NOT EXISTS \"OriginalName\" character varying(300);");

            migrationBuilder.Sql(
                "ALTER TABLE hr_personnel_documents " +
                "ADD COLUMN IF NOT EXISTS \"ContentType\" character varying(200);");

            migrationBuilder.Sql(
                "ALTER TABLE hr_personnel_documents " +
                "ADD COLUMN IF NOT EXISTS \"FileSize\" bigint NOT NULL DEFAULT 0;");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS " +
                "\"IX_hr_personnel_documents_PersonnelId_DocumentType\" " +
                "ON hr_personnel_documents (\"PersonnelId\", \"DocumentType\");");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS " +
                "\"IX_hr_personnel_documents_CompanyId_ExpiryDate\" " +
                "ON hr_personnel_documents (\"CompanyId\", \"ExpiryDate\");");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS " +
                "\"IX_hr_personnel_documents_CompanyId_ExpiryDate\";");

            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS " +
                "\"IX_hr_personnel_documents_PersonnelId_DocumentType\";");

            migrationBuilder.Sql(
                "ALTER TABLE hr_personnel_documents " +
                "DROP COLUMN IF EXISTS \"FileSize\", " +
                "DROP COLUMN IF EXISTS \"ContentType\", " +
                "DROP COLUMN IF EXISTS \"OriginalName\";");
        }
    }
}
