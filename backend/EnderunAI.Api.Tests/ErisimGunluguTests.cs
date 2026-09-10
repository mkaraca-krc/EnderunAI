using System.Net.Http.Headers;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ERİŞİM GÜNLÜĞÜ GERÇEKTEN YAZIYOR MU (GÜNLÜK/1).
///
/// ═══ NEDEN ÇAĞIRARAK ═══
///
/// 9 Eylül'de bir kapının YAZILMIŞ ama HİÇ KOŞMAMIŞ olduğu ölçüldü
/// (`duzen-testi.sh`'in üçüncü sonuç yolu, `set -e` altında ulaşılamaz
/// kod). Ders: bir kapı, en az bir kez KIRMIZI yanmadan var sayılmaz.
///
/// Bu sınıf günlüğün SATIR ÜRETTİĞİNİ gösteriyor — kodun varlığını
/// değil.
/// </summary>
public sealed class ErisimGunluguTests(ITestOutputHelper cikti)
{
    /// <summary>Yazılan satırları toplayan sahte günlükçü.</summary>
    private sealed class ToplayiciGunlukcu : ILogger
    {
        public readonly List<string> Satirlar = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Satirlar.Add(formatter(state, exception));
    }

    [Fact]
    public void Ret_SatirYaziyor_VeSorguDizesiniAtiyor()
    {
        var g = new ToplayiciGunlukcu();
        var kullanici = Guid.NewGuid();

        ErisimGunlugu.Ret(
            g,
            ErisimRetSebebi.HesapPasif,
            kullanici,
            "/api/muhasebe/fisler?token=GIZLI-DEGER&sayfa=2");

        foreach (var s in g.Satirlar) cikti.WriteLine(s);

        Assert.True(g.Satirlar.Count == 1, "Ret satırı YAZILMADI — günlük ölü.");
        Assert.Contains("HesapPasif", g.Satirlar[0]);
        Assert.Contains(kullanici.ToString(), g.Satirlar[0]);
        Assert.Contains("/api/muhasebe/fisler", g.Satirlar[0]);

        // SORGU DİZESİ YAZILMAZ: orada kimlik/kişisel veri taşınabilir.
        Assert.DoesNotContain("GIZLI-DEGER", g.Satirlar[0]);
        Assert.DoesNotContain("?", g.Satirlar[0]);
    }

    [Fact]
    public void SelBaskini_TekSatirdaToplaniyor()
    {
        var g = new ToplayiciGunlukcu();
        var kullanici = Guid.NewGuid();

        /*
         * Giriş döngüsü kusurunda 10 SANİYEDE 862 istek ölçülmüştü.
         * Burada 50 tekrar üretiliyor: ilk olay hemen yazılmalı,
         * kalan 49 TEK SATIRA toplanmalı.
         */
        for (var i = 0; i < 50; i++)
        {
            ErisimGunlugu.Ret(g, ErisimRetSebebi.JetonYok, kullanici, "/api/auth/me");
        }

        cikti.WriteLine($"50 tekrar -> {g.Satirlar.Count} satır");
        foreach (var s in g.Satirlar) cikti.WriteLine(s);

        Assert.True(
            g.Satirlar.Count == 1,
            $"SEL TOPLANMADI: 50 olay {g.Satirlar.Count} satır üretti. " +
            "Giriş döngüsü sınıfı bir kusur günlüğü doldurur.");

        // Pencere kapanınca biriken tekrar bildirilmeli.
        var dagilim = ErisimGunlugu.PencereleriKapat(g);
        Assert.True(dagilim.ContainsKey(ErisimRetSebebi.JetonYok));
    }

    [Fact]
    public void Ozet_SifirdaBileDagilimDondurmeli()
    {
        var g = new ToplayiciGunlukcu();

        // Hiç olay yokken de çağrılabilmeli ve patlamamalı:
        // "sessizlik" ile "ölçüm ölmüş" ayrımı buna dayanıyor.
        var dagilim = ErisimGunlugu.PencereleriKapat(g);

        Assert.NotNull(dagilim);
    }
}
