using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// URL YOLUNDAKİ SIR MASKELENİYOR MU.
///
/// İşveren portalı bağlantısı sırrı yolun kendisinde taşıyor
/// (`/api/portal/{token}`). Yolu olduğu gibi yazan her nokta o
/// anahtarı düz metin olarak diske koyar. 2026-08-23'te bu üç yerde
/// bulunup kapatıldı (nginx erişim kaydı, denetim kesicisi,
/// PortalTokenRejected); dördüncüsü — GlobalExceptionHandler'ın
/// yazdığı `Path=` — henüz sızmamıştı ama sızmaya hazırdı.
///
/// BURADA MASKELEYİCİNİN KENDİSİ SINANIYOR. Uçtan uca hata üretip
/// günlüğü okumak yerine doğrudan kuralı sınamak, testi hem hızlı
/// hem de günlük altyapısından bağımsız yapıyor: günlük sağlayıcısı
/// değişse bile kural aynı kalmalı.
/// </summary>
public sealed class SensitivePathMaskingTests
{
    /*
     * TEST VERİSİ UYDURMADIR — GERÇEK BİR TOKEN DEĞİL.
     *
     * İlk sürümde buraya canlıdaki bağlantının tokenı yazılmıştı.
     * O token o sırada geçerliydi; yani bu paketin bütün konusu olan
     * hatayı testin kendisi tekrarlıyordu: sır, kaynak koda ve
     * oradan git geçmişine düz metin olarak girmişti.
     *
     * Test verisi asla gerçek sırdan türetilmez. Biçimin doğru
     * olması yeter: 43 karakterlik URL-safe base64.
     */
    [Theory]
    // Gerçek biçim: 43 karakterlik URL-safe base64.
    [InlineData("/api/portal/TEST-UCAvFXVun49KKN9VCoMF9ReiYItde-Oy1imc09",
                "/api/portal/***")]
    [InlineData("/api/portal/TEST-UCAvFXVun49KKN9VCoMF9ReiYItde-Oy1imc09/reports",
                "/api/portal/***/reports")]
    [InlineData("/portal/TEST-UCAvFXVun49KKN9VCoMF9ReiYItde-Oy1imc09",
                "/portal/***")]
    [InlineData("/api/portal/abc/photos/8f14e45f-ceea-467a-9b3d-2b6c8c7a1111",
                "/api/portal/***/photos/8f14e45f-ceea-467a-9b3d-2b6c8c7a1111")]
    // Gelecekteki bağlantıyla-erişim akışları: uç henüz yok, desen hazır.
    [InlineData("/api/auth/reset/gizli-anahtar-123", "/api/auth/reset/***")]
    [InlineData("/api/auth/invite/gizli-anahtar-123", "/api/auth/invite/***")]
    public void SirTasiyanYol_Maskelenir(string yol, string beklenen)
    {
        Assert.Equal(beklenen, SensitivePathMasker.Mask(yol));
    }

    /// <summary>
    /// TEŞHİS GÜCÜ KAYBOLMAMALI. Yolu hiç yazmamak da bir seçenekti
    /// ama o zaman hatanın hangi uçta olduğu kaybolurdu. Sır
    /// taşımayan yollar olduğu gibi kalmalı — kayıt kimliği (Guid)
    /// dahil: kaydın kimliği sır değildir ve teşhis için gereklidir.
    /// </summary>
    [Theory]
    [InlineData("/api/finance/dashboard")]
    [InlineData("/api/perakende/raporlar/gun-sonu")]
    [InlineData("/api/projects/8f14e45f-ceea-467a-9b3d-2b6c8c7a1111/documents")]
    [InlineData("/api/health")]
    public void SirTasimayanYol_AynenKalir(string yol)
    {
        Assert.Equal(yol, SensitivePathMasker.Mask(yol));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BosYol_PatlamAz(string? yol)
    {
        Assert.Equal(string.Empty, SensitivePathMasker.Mask(yol));
    }

    /// <summary>
    /// TOKEN HİÇBİR PARÇASIYLA KALMAMALI: maskeleme yalnız sonu
    /// kesseydi ("QAp5r7…") kalan önek yine de kabul edilemez bir
    /// ipucu olurdu.
    /// </summary>
    [Fact]
    public void MaskelenmisYolda_TokenHicGecmez()
    {
        const string token = "TEST-UCAvFXVun49KKN9VCoMF9ReiYItde-Oy1imc09";

        var maskeli = SensitivePathMasker.Mask($"/api/portal/{token}/ilerleme");

        Assert.DoesNotContain(token, maskeli);
        Assert.DoesNotContain(token[..8], maskeli);
        Assert.Contains("***", maskeli);
    }
}

/// <summary>
/// HANDLER MASKELEYİCİYİ GERÇEKTEN ÇAĞIRIYOR MU.
///
/// Yukarıdaki testler KURALI sınıyor. Bu test KULLANIMI sınıyor:
/// kural doğru olsa bile handler onu çağırmazsa token yine günlüğe
/// düşer. İkisi ayrı şeydir ve ikisi de gerekli.
///
/// Günlük satırı sahte bir ILogger ile yakalanıyor; gerçek günlük
/// altyapısını okumak testi hem yavaş hem de sağlayıcıya bağımlı
/// yapardı.
/// </summary>
public sealed class GlobalExceptionHandlerMaskingTests
{
    private sealed class YakalayanLogger : ILogger<GlobalExceptionHandler>
    {
        public readonly List<string> Satirlar = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Hem biçimlenmiş metin hem de tek tek değerler kaydediliyor:
            // yapılandırılmış günlükte değerler ayrı alanlara gider ve
            // token orada da geçmemeli.
            Satirlar.Add(formatter(state, exception));

            if (state is IEnumerable<KeyValuePair<string, object?>> ciftler)
            {
                foreach (var cift in ciftler)
                    Satirlar.Add($"{cift.Key}={cift.Value}");
            }
        }
    }

    [Fact]
    public async Task PortalUcundaIslenmeyenHata_TokenGunlugeYazilmaz()
    {
        const string token = "TEST-UCAvFXVun49KKN9VCoMF9ReiYItde-Oy1imc09";

        var logger = new YakalayanLogger();
        var handler = new GlobalExceptionHandler(logger);

        var context = new DefaultHttpContext();
        context.Request.Path = $"/api/portal/{token}/reports";
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        // BİLEREK İŞLENMEYEN HATA.
        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("sonda: portal ucunda beklenmeyen hata"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

        Assert.NotEmpty(logger.Satirlar);

        // TOKEN GÜNLÜKTE 0 KEZ — ne tam hâliyle ne de öneğiyle.
        foreach (var satir in logger.Satirlar)
        {
            Assert.DoesNotContain(token, satir);
            Assert.DoesNotContain(token[..8], satir);
        }

        // Ama teşhis gücü duruyor: hangi uç olduğu hâlâ okunabiliyor.
        Assert.Contains(logger.Satirlar, x => x.Contains("/api/portal/***"));
    }
}
