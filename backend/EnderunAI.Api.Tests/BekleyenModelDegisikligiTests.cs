using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// MODELDE GÖÇE YANSIMAMIŞ DEĞİŞİKLİK VARSA KIRMIZI (KURULUM/1 · D1).
///
/// "Model değiştirildi ama göç yazılmadı" hâli hiçbir yerde
/// yakalanmıyordu. Sonuç sessizdir: derleme geçer, testler geçer,
/// yayın "BAŞARILI" der — ve veritabanında olmayan bir sütunu bekleyen
/// kod canlıya çıkar. Hata kullanıcıya, üstelik en olmadık ekranda
/// görünür.
///
/// İKİ BAĞLAM DA KAPSANIYOR. `HrDbContext` bu kod tabanında uzun süre
/// gözden kaçtı; kapının tek bağlamı koruması, kaçanın yine aynı
/// bağlam olması demek olurdu.
///
/// UCUZ: veritabanına bağlanmıyor, yalnız modeli anlık görüntüyle
/// karşılaştırıyor.
/// </summary>
[Collection("Integration")]
public sealed class BekleyenModelDegisikligiTests(DatabaseFixture fixture)
{
    /*
     * EF 8'İN KENDİ HÜKMÜ KULLANILIYOR: `HasPendingModelChanges()`.
     *
     * İlk yazımda `IMigrationsModelDiffer` ile elle karşılaştırma
     * kurmuştum — anlık görüntüyü yeniden başlat, ilişkisel modeli
     * çıkar, farkı al. Uzun, kırılgan ve EF'in iç API'lerine bağlı.
     * Aynı soruyu EF zaten cevaplıyor.
     *
     * VERİTABANINA BAĞLANMIYOR: model ile göç anlık görüntüsünü
     * karşılaştırır, sorgu atmaz.
     */
    /// <summary>
    /// AppDbContext — modelde göçe yansımamış değişiklik olmamalı.
    /// </summary>
    [Fact]
    public void AppDbContext_BekleyenModelDegisikligiYok()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(db.Database.HasPendingModelChanges(),
            "AppDbContext modelinde göçe yansımamış değişiklik var. " +
            "`dotnet ef migrations add <ad> --context AppDbContext` çalıştırın.");
    }

    /// <summary>
    /// HrDbContext — aynı kapı, ikinci bağlam için.
    /// </summary>
    [Fact]
    public void HrDbContext_BekleyenModelDegisikligiYok()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        Assert.False(db.Database.HasPendingModelChanges(),
            "HrDbContext modelinde göçe yansımamış değişiklik var. " +
            "`dotnet ef migrations add <ad> --context HrDbContext " +
            "--output-dir Migrations/HumanResources` çalıştırın.");
    }
}
