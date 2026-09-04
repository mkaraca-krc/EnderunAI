using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <summary>
    /// GÖREV TÜRÜ: KORUMAK YERİNE İMKÂNSIZ KILMAK.
    ///
    /// `Belirsiz = 0` bugün de reddediliyor — ama reddi YALNIZ uygulama
    /// kodu yapıyor (`GorevAtamaKurali.Dogrula`, üç yazma yolunun üçü de
    /// çağırıyor). Depolama katmanı sıfıra hâlâ izin veriyor. Bu göç
    /// izni kaldırıyor: sıfır artık veritabanına YAZILAMAZ.
    ///
    /// ── NEDEN BUGÜN GÜVENLİ, 3 EYLÜL'DE DEĞİLDİ ──
    ///
    /// Aynı kısıt İŞEMRİ/2 göçüyle birlikte konsaydı CANLIYI KIRARDI.
    /// `safe-deploy`ın göç kapısı uygulanmamış göç varken yayını
    /// reddediyor; yani şema her zaman koddan ÖNCE gidiyor ve arada
    /// eski kodun yeni şemaya yazdığı bir pencere açılıyor (ölçüldü:
    /// 2026-09-04 koşusunda 2594s). O pencerede koşan kod `Kind`
    /// sütununu HİÇ tanımıyordu; INSERT'inde sütun yoktu, varsayılan
    /// 0 devreye girerdi ve CHECK o yazmayı reddederdi — görev
    /// oluşturma canlıda kırılırdı.
    ///
    /// Bugün pencerede koşacak kod bir ÖNCEKİ sürüm, o da 3 Eylül'den
    /// beri `Kind`ı açıkça yazıyor. Kısıtı bugün güvenli kılan şey
    /// sütunun DOĞUŞ PENCERESİNİN GERİDE KALMASI.
    ///
    /// BİR KORUMANIN DOĞRU OLMASI, HER AN DOĞRU OLDUĞU ANLAMINA GELMEZ.
    ///
    /// ── VARSAYILANIN DÜŞÜRÜLMESİ NEDEN AYRI VE KÜÇÜK BİR İŞ ──
    ///
    /// Varsayılan tehlikeli olduğu için değil. ÖLÇÜLDÜ: model anlık
    /// görüntüsünde `Kind` için `HasDefaultValue` YOK — varsayılan
    /// yalnız veritabanında yaşıyor. EF her INSERT'te sütunu açıkça
    /// gönderiyor, "atla da varsayılan gelsin" yolunu hiç kullanmıyor.
    /// Yani işi CHECK yapıyor; varsayılanın düşürülmesi yalnız HAM SQL
    /// yolunu kapatıyor (psql'den elle yazan, göçün kendi geri
    /// doldurması). İkisi birlikte yapılıyor çünkü ikisi aynı kapının
    /// iki yarısı — biri EF'i, diğeri EF'siz yazanı karşılıyor.
    ///
    /// ── SIRA ÖNEMLİ ──
    ///
    /// Önce varsayılan düşer, sonra kısıt eklenir. Ters sırada, kısıt
    /// eklendikten sonra varsayılanı düşürmek de çalışırdı; ama bu sıra
    /// "sütunun sessiz 0 üretme yolu artık yok" halini kısıt eklenmeden
    /// ÖNCE kuruyor.
    ///
    /// TERSİ ALINABİLİR: `Down` kısıtı düşürüp varsayılanı geri koyuyor.
    /// Veri kaybı yok — bu göç hiçbir satırı değiştirmiyor.
    ///
    /// ÖLÇÜLDÜ (2026-09-04, canlı): `Kind = 0` olan satır YOK
    /// (toplam 3 görev, üçü de `Kind = 1`). Kısıt eklenirken
    /// PostgreSQL mevcut satırları da sınıyor; sıfır olan bir satır
    /// olsaydı bu göç PATLARDI — sessizce geçmezdi.
    /// </summary>
    public partial class GorevTuruBelirsizYasak : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"alter table ""WorkTasks"" alter column ""Kind"" drop default;");

            migrationBuilder.Sql(
                @"alter table ""WorkTasks""
                  add constraint ""CK_WorkTasks_Kind_Belirsiz_Degil""
                  check (""Kind"" <> 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"alter table ""WorkTasks""
                  drop constraint if exists ""CK_WorkTasks_Kind_Belirsiz_Degil"";");

            migrationBuilder.Sql(
                @"alter table ""WorkTasks"" alter column ""Kind"" set default 0;");
        }
    }
}
