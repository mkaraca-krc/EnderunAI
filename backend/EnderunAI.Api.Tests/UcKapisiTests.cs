using EnderunAI.Api.Security;
using EnderunAI.Api.Security.UcKapisi;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// UÇ KAPISI — HER UCUN BİR BEYANI VAR MI.
///
/// KARDEŞ KAPI: <see cref="AuthorizeGuardTests"/> sınıf düzeyinde KİMLİĞİ
/// denetler ([Authorize] var mı). Bu kapı uç düzeyinde YETKİ BEYANINI
/// denetler. Biri diğerinin yerini tutmaz: kimliği olan ama beyanı
/// olmayan bir uç, giriş yapmış HERKESE açıktır.
///
/// BU TESTLER NEYİ KANITLAR, NEYİ KANITLAMAZ.
///
/// Asıl zorlama AÇILIŞTADIR: beyansız uç varsa uygulama hiç başlamaz.
/// Bu yüzden "canlı tabloda beyansız uç yok" diye bir test yazmak
/// TOTOLOJİDİR — fabrika ayağa kalkabiliyorsa o test zorunlu olarak
/// yeşildir, kalkamıyorsa hiç koşamaz. Hiçbir şey kanıtlamaz.
///
/// Bu yüzden ısırma SENTETİK uçlarla kanıtlanıyor: elle kurulmuş bir
/// beyansız uç tarayıcıya verilir ve bulunması beklenir. Tarayıcı
/// açılışta çağrılanla AYNI tarayıcıdır.
///
/// Canlı tabloya bakan tek test yüzeyin BOŞ OLMADIĞINI ölçer — çünkü
/// boş bir yüzey her iddiayı doğrular ve hiçbir şeyi kanıtlamaz.
/// </summary>
/// NEDEN FABRİKADAN BAĞIMSIZ: bu sınıftaki testlerin hiçbiri
/// veritabanına ya da ayakta bir uygulamaya ihtiyaç duymuyor. Integration
/// koleksiyonuna konsalardı, bir AMBALAJ HATASI (liste yayın çıktısında
/// yok) uygulamayı açılamaz hale getirir ve bu testler kendi
/// teşhislerini veremeden fabrika hatasıyla düşerdi — yani ambalaj
/// hatasının teşhisini verecek test, tam da o hatada susardı.
/// Canlı yüzeye bakan tek test ayrı sınıfta: <see cref="UcKapisiCanliYuzeyTests"/>.
public sealed class UcKapisiTests
{
    // ------------------------------------------------------------------
    // 1) AMBALAJ — liste yayın çıktısında mı
    // ------------------------------------------------------------------

    /// <summary>
    /// Açılışta durduran bir muhafız KENDİ ambalaj hatasıyla duramamalı.
    /// Bu test listenin derlenmiş çıktının İÇİNDE olduğunu ölçer; dosya
    /// sistemine hiç bakmaz — canlıda okunan şey de dosya değil, manifesttir.
    ///
    /// SONDA: .csproj'daki EmbeddedResource girdisini kaldır → KIRMIZI.
    /// </summary>
    [Fact]
    public void MuafiyetListesi_YayinCiktisindadir()
    {
        var derleme = typeof(MuafiyetListesi).Assembly;
        var kaynaklar = derleme.GetManifestResourceNames();

        Assert.True(
            MuafiyetListesi.GomuluKaynakVar(derleme),
            $"Muaf uç listesi ('{MuafiyetListesi.KaynakAdi}') derlenmiş " +
            "çıktıda YOK. Uygulama canlıda açılmayacaktır. " +
            "EnderunAI.Api.csproj içindeki EmbeddedResource girdisini kontrol " +
            "edin.\nBulunan kaynaklar:\n  " +
            (kaynaklar.Length == 0 ? "(hiç yok)" : string.Join("\n  ", kaynaklar)));
    }

    [Fact]
    public void MuafiyetListesi_HerSatirKategoriliVeGerekceli()
    {
        var muafiyetler = MuafiyetListesi.Oku();

        // POZİTİF KONTROL: boş liste her iddiayı doğrular.
        Assert.NotEmpty(muafiyetler);

        foreach (var muafiyet in muafiyetler)
        {
            Assert.False(string.IsNullOrWhiteSpace(muafiyet.Kategori));
            Assert.False(string.IsNullOrWhiteSpace(muafiyet.Gerekce));
        }
    }

    // ------------------------------------------------------------------
    // 2) ISIRMA — sentetik uçlarla
    // ------------------------------------------------------------------

    [Fact]
    public void Beyansiz_Uc_BULUNUR()
    {
        var bulunan = UcKapisiDenetimi.BeyansizlariBul(
            [Uc("api/sonda/beyansiz")],
            new HashSet<string>(StringComparer.Ordinal));

        Assert.Single(bulunan);
        Assert.Equal("api/sonda/beyansiz", bulunan[0].Anahtar);
    }

    [Fact]
    public void NitelikliUc_BULUNMAZ_POZITIF_KONTROL()
    {
        var bulunan = UcKapisiDenetimi.BeyansizlariBul(
            [Uc("api/sonda/nitelikli",
                new RequirePermissionAttribute(PermissionCatalog.Keys.TasksView))],
            new HashSet<string>(StringComparer.Ordinal));

        Assert.Empty(bulunan);
    }

    [Fact]
    public void AnonimUc_BULUNMAZ_POZITIF_KONTROL()
    {
        var bulunan = UcKapisiDenetimi.BeyansizlariBul(
            [Uc("api/sonda/anonim", new AllowAnonymousAttribute())],
            new HashSet<string>(StringComparer.Ordinal));

        Assert.Empty(bulunan);
    }

    [Fact]
    public void MuafUc_BULUNMAZ_POZITIF_KONTROL()
    {
        var bulunan = UcKapisiDenetimi.BeyansizlariBul(
            [Uc("api/sonda/muaf")],
            new HashSet<string>(["api/sonda/muaf"], StringComparer.Ordinal));

        Assert.Empty(bulunan);
    }

    /// <summary>
    /// YÜZEY DIŞI KALMASIN: `api/` dışındaki çerçeve uçları denetlenmez.
    /// Bu bir muafiyet değil, denetimin sınırıdır — ve sınırın kendisi
    /// de sınanmalıdır, yoksa yarın yüzey sessizce daralabilir.
    /// </summary>
    [Fact]
    public void YuzeyDisiUc_DENETLENMEZ()
    {
        var bulunan = UcKapisiDenetimi.BeyansizlariBul(
            [Uc("_framework/blazor.js")],
            new HashSet<string>(StringComparer.Ordinal));

        Assert.Empty(bulunan);
    }

    [Fact]
    public void OluMuafiyet_BULUNUR()
    {
        var olu = UcKapisiDenetimi.OluMuafiyetler(
            [Uc("api/var/olan")],
            new HashSet<string>(["api/var/olan", "api/artik/yok"], StringComparer.Ordinal));

        Assert.Equal(["api/artik/yok"], olu);
    }

    /// <summary>
    /// BELİRSİZ MUAFİYET DURDURUR. Aynı adı taşıyan iki eylem varsa tek
    /// muafiyet satırı ikisini birden affederdi; yazan kişi bunu bilmez.
    /// </summary>
    [Fact]
    public void BelirsizMuafiyet_BULUNUR()
    {
        var belirsiz = UcKapisiDenetimi.BelirsizMuafiyetler(
            [UcAdli("api/a/bir", "Ayni", "Metot"),
             UcAdli("api/a/iki", "Ayni", "Metot")],
            new HashSet<string>(["Ayni.Metot"], StringComparer.Ordinal));

        Assert.Single(belirsiz);
        Assert.Contains("Ayni.Metot", belirsiz[0]);
    }

    [Fact]
    public void TekUcaKarsilikGelenMuafiyet_BULUNMAZ_POZITIF_KONTROL()
    {
        var belirsiz = UcKapisiDenetimi.BelirsizMuafiyetler(
            [UcAdli("api/a/bir", "Tekil", "Metot")],
            new HashSet<string>(["Tekil.Metot"], StringComparer.Ordinal));

        Assert.Empty(belirsiz);
    }

    // ------------------------------------------------------------------

    private static Endpoint UcAdli(string sablon, string denetleyici, string eylem) =>
        Uc(sablon, new ControllerActionDescriptor
        {
            ControllerName = denetleyici,
            ActionName = eylem,
        });

    private static Endpoint Uc(string sablon, params object[] ustVeri) =>
        new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(sablon),
            order: 0,
            new EndpointMetadataCollection(ustVeri),
            displayName: sablon);
}

/// <summary>
/// CANLI YÜZEY — tarayıcı gerçek yönlendirme tablosunu görüyor mu.
///
/// Ayrı sınıf, çünkü tek fabrika gerektiren test bu. Aynı dosyadaki
/// diğer testler fabrikadan bağımsız kalsın ki bir ambalaj hatasında
/// kendi teşhislerini verebilsinler.
/// </summary>
[Collection("Integration")]
public sealed class UcKapisiCanliYuzeyTests(DatabaseFixture fixture)
{
    /// <summary>
    /// TARAYICI GERÇEK TABLOYU GÖRÜYOR MU.
    ///
    /// Bu testin ölçtüğü şey "beyansız uç yok" DEĞİL — onu açılış denetimi
    /// zaten zorluyor ve fabrika ayaktaysa cevap zorunlu olarak evettir.
    /// Ölçtüğü şey, tarayıcının baktığı yüzeyin boş olmadığıdır: yüzey
    /// sessizce boşalırsa muhafız yeşil kalarak işlevsizleşir.
    ///
    /// TABAN yalnız YÜKSELİR. Uç silmek bilinçli bir karardır; tabanı
    /// düşüren kişi bunu görerek düşürsün.
    /// </summary>
    [Fact]
    public void CanliYuzey_BosDegil()
    {
        var uclar = fixture.Factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints;

        var apiUclari = uclar.Count(uc =>
            uc is RouteEndpoint yol &&
            (yol.RoutePattern.RawText ?? string.Empty).TrimStart('/')
                .StartsWith("api/", StringComparison.OrdinalIgnoreCase));

        Assert.True(
            apiUclari >= UcTabani,
            $"`api/` altındaki uç sayısı {apiUclari}, taban {UcTabani}. " +
            "Yüzey daralmışsa uç kapısı sessizce işlevsizleşir. Uçlar " +
            "bilerek silindiyse tabanı bu commit'te düşürün.");
    }

    /// <summary>Ölçüldü (KAPI/1, 2026-09-04). Yalnız bilinçli olarak düşürülür.</summary>
    private const int UcTabani = 800;
}
