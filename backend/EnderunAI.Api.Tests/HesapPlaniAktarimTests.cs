using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// HESAP PLANI AKTARIMI — EKLER YA DA ATLAR, ASLA DEĞİŞTİRMEZ.
/// </summary>
[Collection("Integration")]
public sealed class HesapPlaniAktarimTests(DatabaseFixture fixture)
{
    private async Task<Guid> SirketKurAsync(string ek)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, ek);
        await TestDataFactory.EnsureStockAccountsAsync(db, company.Id);
        return company.Id;
    }

    private sealed record Hata(int RowNumber, string? AccountCode, string Message);

    private sealed record Sonuc(
        bool Preview, int TotalRowCount, int ValidRowCount,
        int CreatedCount, int UpdatedCount, int UnchangedCount,
        int SkippedCount, int ErrorCount, List<Hata> Errors, string Message);

    /// <summary>
    /// Testin kendi Excel'ini üretmesi şart: dosya olarak sabitlenmiş
    /// bir örnek, sütun sırası değişince sessizce eskir ve test neyi
    /// sınadığını kaybeder.
    /// </summary>
    private static MultipartFormDataContent Dosya(
        Guid companyId, bool preview, params (string Kod, string Ad)[] satirlar)
    {
        using var kitap = new XLWorkbook();
        var sayfa = kitap.AddWorksheet("Hesaplar");

        sayfa.Cell(1, 1).Value = "Hesap Kodu";
        sayfa.Cell(1, 2).Value = "Hesap Adı";

        for (var i = 0; i < satirlar.Length; i++)
        {
            sayfa.Cell(i + 2, 1).Value = satirlar[i].Kod;
            sayfa.Cell(i + 2, 2).Value = satirlar[i].Ad;
        }

        var bellek = new MemoryStream();
        kitap.SaveAs(bellek);
        bellek.Position = 0;

        var icerik = new MultipartFormDataContent
        {
            { new StringContent(companyId.ToString()), "companyId" },
            { new StringContent(preview ? "true" : "false"), "preview" }
        };

        var dosya = new StreamContent(bellek);
        dosya.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        icerik.Add(dosya, "file", "hesap-plani.xlsx");

        return icerik;
    }

    /// <summary>
    /// MEVCUT HESAP KODU GÜNCELLENMEZ — ATLANIR VE RAPORLANIR.
    ///
    /// İddia iki parçalı: satır "zaten var" listesinde görünmeli VE
    /// veritabanındaki ad DEĞİŞMEMİŞ olmalı. Yalnız listeye bakmak,
    /// "raporladı ama yine de güncelledi" hâlini kaçırırdı.
    /// </summary>
    [Fact]
    public async Task MevcutKod_Guncellenmez_RaporlanirAtlanir()
    {
        var ek = $"ha{DateTime.UtcNow:ffffff}";
        var companyId = await SirketKurAsync(ek);
        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, ek, ["Finans Sorumlusu"], companyId);

        var cevap = await client.PostAsync("/api/accounting-accounts/import",
            Dosya(companyId, preview: false, ("150", "BAŞKA BİR AD")));

        Assert.Equal(HttpStatusCode.OK, cevap.StatusCode);
        var sonuc = await cevap.Content.ReadFromJsonAsync<Sonuc>();

        Assert.Equal(0, sonuc!.CreatedCount);
        Assert.Equal(0, sonuc.UpdatedCount);
        Assert.Equal(1, sonuc.UnchangedCount);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ad = await db.AccountingAccounts
            .Where(x => x.CompanyId == companyId && x.Code == "150")
            .Select(x => x.Name).SingleAsync();

        Assert.Equal("İlk Madde ve Malzeme", ad);
        Assert.NotEqual("BAŞKA BİR AD", ad);
    }

    /// <summary>
    /// ÜST HESAP YOKSA OLUŞTURULMAZ — HATA VERİLİR, SATIR ATLANIR.
    ///
    /// "999.01" isteniyor ama "999" yok. Sessizce üretilseydi,
    /// üretilen ara hesabın borç/alacak karakteri tahmin edilmiş olur
    /// ve mali tabloda yanlış yerde toplanırdı.
    /// </summary>
    [Fact]
    public async Task UstHesapYok_OlusturulmazHataVerir()
    {
        var ek = $"hu{DateTime.UtcNow:ffffff}";
        var companyId = await SirketKurAsync(ek);
        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, ek, ["Finans Sorumlusu"], companyId);

        var cevap = await client.PostAsync("/api/accounting-accounts/import",
            Dosya(companyId, preview: false, ("999.01", "Öksüz Hesap")));

        var sonuc = await cevap.Content.ReadFromJsonAsync<Sonuc>();

        Assert.Equal(0, sonuc!.CreatedCount);
        Assert.Single(sonuc.Errors);
        Assert.Contains("999", sonuc.Errors[0].Message);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // NE ÖKSÜZ HESAP NE DE ÜRETİLMİŞ ÜST HESAP OLMALI.
        var uretilen = await db.AccountingAccounts
            .CountAsync(x => x.CompanyId == companyId && (x.Code == "999" || x.Code == "999.01"));

        Assert.Equal(0, uretilen);
    }

    /// <summary>
    /// AYNI DOSYADAKİ ÜST HESAP ÖNCE İŞLENİR.
    ///
    /// Satırlar sırasız gönderiliyor: alt hesap üstte. Koda göre
    /// sıralanmasaydı "998.01" önce gelip "üst hesap yok" hatası
    /// alırdı — oysa üstü aynı dosyada.
    /// </summary>
    [Fact]
    public async Task AyniDosyadakiUstHesap_OnceIslenir()
    {
        var ek = $"hs{DateTime.UtcNow:ffffff}";
        var companyId = await SirketKurAsync(ek);
        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, ek, ["Finans Sorumlusu"], companyId);

        var cevap = await client.PostAsync("/api/accounting-accounts/import",
            Dosya(companyId, preview: false, ("998.01", "Alt Hesap"), ("998", "Üst Hesap")));

        var sonuc = await cevap.Content.ReadFromJsonAsync<Sonuc>();

        Assert.Equal(2, sonuc!.CreatedCount);
        Assert.Empty(sonuc.Errors);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var alt = await db.AccountingAccounts
            .Where(x => x.CompanyId == companyId && x.Code == "998.01")
            .Select(x => new { x.ParentAccountId, x.Level }).SingleAsync();

        Assert.NotNull(alt.ParentAccountId);
        Assert.Equal(2, alt.Level);
    }

    /// <summary>
    /// chart.import OLMAYAN 403 ALIR.
    ///
    /// Ön Muhasebe rolü seçildi: `accounting.create` ve
    /// `accounting.manage` VAR ama `chart.import` YOK. Yani test
    /// "yetkisiz kullanıcı" değil, "muhasebe yetkisi olan ama toplu
    /// aktarım yetkisi olmayan" kullanıcıyı sınıyor — ayrı anahtarın
    /// varlık sebebi tam olarak bu.
    /// </summary>
    [Fact]
    public async Task ChartImportYok_403Alir()
    {
        var ek = $"hy{DateTime.UtcNow:ffffff}";
        var companyId = await SirketKurAsync(ek);
        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, ek, ["Ön Muhasebe"], companyId);

        var cevap = await client.PostAsync("/api/accounting-accounts/import",
            Dosya(companyId, preview: false, ("997", "Deneme")));

        Assert.Equal(HttpStatusCode.Forbidden, cevap.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var eklendi = await db.AccountingAccounts
            .AnyAsync(x => x.CompanyId == companyId && x.Code == "997");

        Assert.False(eklendi);
    }
}
