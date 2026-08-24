using EnderunAI.Api.Services.Notifications;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ÜRETİM KAPSAYICISININ KENDİSİ SINANIYOR.
///
/// `DailySummaryModeGatingTests` sahtelerle koşuyor: hangi adımın
/// hangi modda çağrıldığını kanıtlıyor ama üretimde o adımların
/// GERÇEKTEN ÇÖZÜLEBİLDİĞİNİ kanıtlamıyor. İkisi ayrı sorular ve
/// ikincisi sessizce bozulabilir: `IDailySummaryRunner` kaydı
/// unutulsa, sahtelerle koşan test yine yeşil kalır; üretimde ise
/// tur her gece `GetRequiredService` ile fırlar ve
/// `ExecuteAsync:38`'deki `catch (Exception)` bunu yutar. Yani arıza
/// kimseye görünmeden günlerce sürebilirdi.
///
/// Bu dosya o boşluğu kapatıyor.
/// </summary>
[Collection("Integration")]
public sealed class DailyNotificationWiringTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Günlük turun dokunduğu HER ŞEY üretim kapsayıcısından
    /// çözülebiliyor — `DailySummaryService` dahil.
    /// </summary>
    [Fact]
    public void UretimKapsayicisi_GunlukTurunTumBagimliliklariniCozer()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var p = scope.ServiceProvider;

        Assert.NotNull(p.GetRequiredService<ITaskDueNotificationScanner>());
        Assert.NotNull(p.GetRequiredService<IScopeDeferralWatchdog>());
        Assert.NotNull(p.GetRequiredService<IDailySummaryRunner>());

        Assert.NotNull(p.GetRequiredService<TaskDueNotificationScanner>());
        Assert.NotNull(p.GetRequiredService<ScopeDeferralWatchdog>());
        Assert.NotNull(p.GetRequiredService<DailySummaryService>());

        // Zamanlayıcının kendisi de barındırılan servis olarak kayıtlı.
        Assert.Contains(
            fixture.Factory.Services.GetServices<IHostedService>(),
            x => x is DailySummaryBackgroundService);
    }

    /// <summary>
    /// ARAYÜZ, SOMUT SINIFIN AYNI ÖRNEĞİNE BAĞLI.
    ///
    /// NEDEN ÖNEMLİ: `AddScoped&lt;IArayuz, Somut&gt;()` yazmak
    /// derlenir ve testlerin çoğu yeşil kalır, ama tek bir scope
    /// içinde İKİ AYRI örnek doğurur. Burada zararı şudur: tarayıcı
    /// ve nöbetçi `AppDbContext` üzerinden iş görüyor; ikinci bir
    /// örnek, aynı turda ikinci bir değişiklik izleyicisi ve ikinci
    /// bir yazma yolu demek. Sessiz çift-yazma ya da yarı-yazılmış
    /// tur, ancak canlıda fark edilirdi.
    /// </summary>
    [Fact]
    public void Arayuzler_SomutSiniflarlaAyniOrnegeBagli()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var p = scope.ServiceProvider;

        Assert.Same(
            p.GetRequiredService<TaskDueNotificationScanner>(),
            p.GetRequiredService<ITaskDueNotificationScanner>());

        Assert.Same(
            p.GetRequiredService<ScopeDeferralWatchdog>(),
            p.GetRequiredService<IScopeDeferralWatchdog>());

        Assert.Same(
            p.GetRequiredService<DailySummaryService>(),
            p.GetRequiredService<IDailySummaryRunner>());
    }

    /// <summary>
    /// AYRI SCOPE'LAR AYRI ÖRNEK ALIR — yukarıdaki testin
    /// "her şey singleton olmuş" gibi bir kazayla yeşil kalmadığını
    /// gösterir.
    /// </summary>
    [Fact]
    public void AyriScopelar_AyriOrnekAlir()
    {
        using var birinci = fixture.Factory.Services.CreateScope();
        using var ikinci = fixture.Factory.Services.CreateScope();

        Assert.NotSame(
            birinci.ServiceProvider.GetRequiredService<ITaskDueNotificationScanner>(),
            ikinci.ServiceProvider.GetRequiredService<ITaskDueNotificationScanner>());
    }
}
