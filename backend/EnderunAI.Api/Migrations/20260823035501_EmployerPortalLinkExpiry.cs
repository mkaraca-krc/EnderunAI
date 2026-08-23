using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class EmployerPortalLinkExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccessCount",
                table: "employer_portal_links",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            /*
             * MEVCUT BAĞLANTILAR GERİYE DÖNÜK HESAPLANMIYOR.
             *
             * Varsayılan `0001-01-01` olsaydı migration uygulandığı
             * anda MEVCUT BÜTÜN BAĞLANTILAR — aktif olan dahil —
             * süresi geçmiş sayılır ve portal 404 dönmeye başlardı;
             * işveren hiçbir uyarı almadan çalışan bağlantısını
             * kaybederdi.
             *
             * `now() + interval '6 months'`: eski kayıtlar oluşturma
             * tarihinden değil, MIGRATION tarihinden süre alıyor.
             */
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "employer_portal_links",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now() + interval '6 months'");

            migrationBuilder.AddColumn<int>(
                name: "ExtensionCount",
                table: "employer_portal_links",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAccessedAtUtc",
                table: "employer_portal_links",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastExtendedAtUtc",
                table: "employer_portal_links",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastExtendedByUserId",
                table: "employer_portal_links",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessCount",
                table: "employer_portal_links");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "employer_portal_links");

            migrationBuilder.DropColumn(
                name: "ExtensionCount",
                table: "employer_portal_links");

            migrationBuilder.DropColumn(
                name: "LastAccessedAtUtc",
                table: "employer_portal_links");

            migrationBuilder.DropColumn(
                name: "LastExtendedAtUtc",
                table: "employer_portal_links");

            migrationBuilder.DropColumn(
                name: "LastExtendedByUserId",
                table: "employer_portal_links");
        }
    }
}
