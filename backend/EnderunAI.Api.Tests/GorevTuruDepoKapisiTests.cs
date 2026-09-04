using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// GÖREV TÜRÜ — DEPOLAMA KATMANININ KAPISI.
///
/// `IsEmriTuruKapisiTests` uygulama kapısını sınıyor: POST/PUT/Hızır
/// `Belirsiz`i reddediyor mu. Bu dosya AYRI bir iddiayı sınıyor:
/// uygulama kapısı ATLANSA BİLE sıfır veritabanına yazılamaz.
///
/// NEDEN AYRI TEST GEREKİYOR: uygulama kapısı üç yazma yolunu
/// kapatıyor, ama dördüncü bir yol her zaman var — psql'den elle
/// yazan, bir göç betiği, ileride eklenecek ham SQL. Bu kod tabanının
/// tekrar eden yarası kuralın SESSİZCE çağrılmaz hâle gelmesi
/// (`2d90c946`). Kısıt veritabanında durduğu sürece o yol da kapalı.
///
/// KISIT: `CK_WorkTasks_Kind_Belirsiz_Degil` — göç
/// `20260904202822_GorevTuruBelirsizYasak`.
/// </summary>
[Collection("Integration")]
public sealed class GorevTuruDepoKapisiTests(DatabaseFixture fixture)
{
    /// <summary>
    /// İDDİA: geçerli türle yazılmış bir satır, ham SQL ile sıfıra
    /// ÇEVRİLEMEZ.
    ///
    /// POZİTİF KONTROL AYNI TESTİN İÇİNDE. Önce geçerli satır
    /// yazılıyor; o yazma da düşseydi (bağlantı, zorunlu alan, kapsam)
    /// ikinci adımın kırmızısı kısıtı DEĞİL, kurulumu ölçerdi.
    /// Kural 48: boş sonuç yokluğun kanıtı değildir.
    /// </summary>
    [Fact]
    public async Task SifirTur_HamSqlIleDeYazilamaz()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var proje = await TestDataFactory.CreateProjectAsync(db, "TUR-DEPO");

        var gorev = new WorkTask
        {
            CompanyId = proje.CompanyId,
            ProjectId = proje.Id,
            TaskNumber = $"TEST-DEPO-{Guid.NewGuid():N}"[..20],
            Title = "Depo kapısı testi",
            Kind = WorkTaskKind.IsEmri,
            Status = WorkTaskStatus.Open
        };

        db.WorkTasks.Add(gorev);

        // POZİTİF KONTROL: geçerli tür yazılabiliyor.
        await db.SaveChangesAsync();

        // ASIL İDDİA: sıfıra çevirmek veritabanı tarafından reddedilir.
        var hata = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                @"update ""WorkTasks"" set ""Kind"" = 0 where ""Id"" = {0};",
                gorev.Id));

        // 23514 = check_violation. Kod ile ad birlikte sınanıyor:
        // yalnız koda bakmak, BAŞKA bir kısıtın patlamasını da
        // "geçti" sayardı.
        Assert.Equal("23514", hata.SqlState);
        Assert.Equal("CK_WorkTasks_Kind_Belirsiz_Degil", hata.ConstraintName);

        // Satır DEĞİŞMEDİ: reddedilen yazma geri alındı.
        var kalan = await db.WorkTasks
            .AsNoTracking()
            .Where(x => x.Id == gorev.Id)
            .Select(x => x.Kind)
            .SingleAsync();

        Assert.Equal(WorkTaskKind.IsEmri, kalan);
    }

    /// <summary>
    /// İDDİA: `Kind` sütununun VARSAYILANI YOK.
    ///
    /// Bu bir şema iddiası, davranış iddiası değil — bilerek. EF
    /// varsayılana hiç güvenmiyor: model anlık görüntüsünde
    /// `HasDefaultValue` yok ve her INSERT sütunu açıkça gönderiyor.
    /// Yani varsayılanın düşürülmesi EF yolunda ÖLÇÜLEBİLİR bir
    /// davranış değiştirmiyor; kapattığı şey sütunu HİÇ yazmayan ham
    /// SQL yolu.
    ///
    /// O yolu davranışla sınamak, testin kendi INSERT'ine bugünkü
    /// zorunlu sütunların tam listesini gömmek demekti; sütun eklendiği
    /// gün test kısıt yüzünden değil, kendi eksikliği yüzünden kırmızı
    /// verirdi. Katalogdan okumak aynı şeyi kırılgan olmadan söylüyor.
    /// </summary>
    [Fact]
    public async Task TurSutununVarsayilaniYok()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        /*
         * SORGUNUN İKİ KABUK KURALI — İKİSİ DE ÖLÇÜLEREK ÖĞRENİLDİ:
         *
         *   1. Sonda NOKTALI VİRGÜL YOK. `SqlQuery` bu metni ALT SORGU
         *      olarak sarmalıyor; `;` sözdizimini bozuyor
         *      (42601 syntax error at or near ";").
         *   2. Skaler sütunun adı `Value` OLMALI — `SqlQuery<T>` sonucu
         *      bu adla arıyor (42703 column t.Value does not exist).
         */
        var varsayilan = await db.Database
            .SqlQuery<string?>($@"
                select column_default as ""Value""
                from information_schema.columns
                where table_name = 'WorkTasks' and column_name = 'Kind'")
            .SingleAsync();

        Assert.Null(varsayilan);
    }
}
