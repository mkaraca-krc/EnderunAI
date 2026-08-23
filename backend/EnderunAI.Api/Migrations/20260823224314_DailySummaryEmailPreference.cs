using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class DailySummaryEmailPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
             * VARSAYILAN `true` — EF `false` ÖNERMİŞTİ.
             *
             * Model varsayılanı `true` (herkes özet alır, isteyen
             * kapatır) ama EF migration'a `false` yazdı: bool'un CLR
             * varsayılanı o. Düzeltilmeseydi mevcut ve yeni bütün
             * kullanıcılar SESSİZCE kapalı başlar, özellik açıldığı
             * gün kimseye e-posta gitmez ve sebebi aranırdı.
             *
             * ETKİ SAYISI (kural 21): canlıda ve testte
             * `user_ui_preferences` ŞU AN 0 SATIR — ölçüldü. Yani bu
             * migration hiçbir mevcut satıra dokunmuyor; varsayılan
             * yalnız BUNDAN SONRA açılacak satırlar için geçerli.
             * Doğrulanacak bir etki sayısı olmadığı için RAISE
             * kontrolü de konmadı: koşulsuz bir kontrol her zaman
             * 0 görüp patlardı.
             */
            migrationBuilder.AddColumn<bool>(
                name: "DailySummaryEmailEnabled",
                table: "user_ui_preferences",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DailySummaryEmailEnabled",
                table: "user_ui_preferences");
        }
    }
}
