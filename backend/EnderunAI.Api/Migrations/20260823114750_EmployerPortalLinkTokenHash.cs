using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class EmployerPortalLinkTokenHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "employer_portal_links",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenPrefix",
                table: "employer_portal_links",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            /*
             * ESKİ BENZERSİZ İNDEKS DÜŞÜYOR.
             *
             * `Token` alanı yeni kayıtlarda boş kalıyor; benzersizlik
             * korunsaydı ikinci bağlantı boş dizgiyle çakışır ve
             * "duplicate key" hatası verirdi. Benzersizlik anlamlı
             * olduğu yere, `TokenHash` üzerine taşındı.
             */
            migrationBuilder.DropIndex(
                name: "IX_employer_portal_links_Token",
                table: "employer_portal_links");

            migrationBuilder.CreateIndex(
                name: "IX_employer_portal_links_TokenHash",
                table: "employer_portal_links",
                column: "TokenHash",
                unique: true,
                filter: "\"TokenHash\" IS NOT NULL");

            /*
             * ESKİ SATIRLARIN ÖNEKİ DOLDURULUYOR.
             *
             * 2026-08-23 öncesi doğmuş 7 bağlantının tokenı gitti
             * (iptal edilip karartıldılar), özetleri üretilemez —
             * TokenHash null kalıyor ve hiçbir istekle eşleşmiyorlar.
             * Zaten hepsi iptal edilmiş durumda.
             *
             * Ama İZLENEBİLİRLİKLERİ korunmalı: karartılmış değerin
             * ilk 8 karakteri hâlâ o bağlantıyı tanıtıyor
             * ("Gwp8WSfk***-fe44400e" -> "Gwp8WSfk"). Ekranda ve
             * denetim kaydında hangi bağlantıdan söz edildiği
             * anlaşılsın diye önek alanına taşınıyor.
             */
            migrationBuilder.Sql("""
                UPDATE employer_portal_links
                SET "TokenPrefix" = left("Token", 8)
                WHERE "TokenPrefix" IS NULL AND "Token" <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_employer_portal_links_TokenHash",
                table: "employer_portal_links");

            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "employer_portal_links");

            migrationBuilder.DropColumn(
                name: "TokenPrefix",
                table: "employer_portal_links");

            migrationBuilder.CreateIndex(
                name: "IX_employer_portal_links_Token",
                table: "employer_portal_links",
                column: "Token",
                unique: true);
        }
    }
}
