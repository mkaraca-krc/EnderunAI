using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class TurkishFoldFunction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
             * TÜRKÇE ARAMA KATLAMASI — TEK KAYNAK, VERİTABANI TARAFI.
             *
             * `lib/search/fold.ts` ve `Search.TurkishSearch.Fold` ile
             * AYNI kural: küçült, sonra Türkçe harfleri ASCII
             * karşılığına katla. Üçü ayrışırsa aynı arama bir yerde
             * kaydı bulur, ötekinde bulamaz — canlıda tam olarak bu
             * yaşandı ("insaat" yazan "İnşaat"ı bulamıyordu).
             *
             * IMMUTABLE: ifade indeksine konu olabilmesi için şart.
             * STRICT: NULL girdi NULL döner, sarmalayan sorgu karar
             * verir.
             */
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enderun_fold(text)
                RETURNS text
                LANGUAGE sql
                IMMUTABLE
                STRICT
                PARALLEL SAFE
                AS $$
                    SELECT translate(lower($1), 'ışğüöçâîû', 'isguocaiu')
                $$;
                """);


        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fonksiyonu düşürmeden önce ona bağlı indeksler düşmeli;
            // bu migration indeks kurmuyor, kuranlar kendi Down'unda
            // temizliyor. CASCADE kullanılmıyor: sessizce başka nesne
            // düşürmek, geri alma işlemini öngörülemez yapardı.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS enderun_fold(text);");


        }
    }
}
