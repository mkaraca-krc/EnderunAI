using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Accounting;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// E4 / E5 — FİŞ BOYUT ZORUNLULUĞU: TEK ORTAK KONTROL.
///
/// ═══ ÖLÇÜLEN KUSUR (2026-09-13, uçtan uca prova) ═══
///
/// Canlı hesap planında 7'li sınıfın 89 hesabı MASRAF MERKEZİ, 485
/// hesap da PROJE zorunlu tutuyor. Fiş üreticileri bu boyutları
/// geçmiyordu ve stok→muhasebe hattının TAMAMI patlıyordu:
///
///   · mal kabul    → "150 hesabında proje seçimi zorunludur."
///   · projesiz çıkış → "770 hesabında masraf merkezi zorunludur."
///   · projesiz sayım → "150 hesabında proje seçimi zorunludur."
///
/// Canlıda `accounting_voucher_lines` içinde 150/153/770/740.03.09/
/// 379.01 hesaplarına ait TEK SATIR YOK — hat hiç çalışmamış.
///
/// ═══ NEDEN MEVCUT TESTLER BUNU YAKALAMADI (Kural 81) ═══
///
/// `TestDataFactory.EnsureStockAccountsAsync` hesapları
/// `RequiresProject` / `RequiresCostCenter` BAYRAKLARI OLMADAN kuruyor.
/// Yani zemin üretimi taklit etmiyordu: stok muhasebesi testleri
/// yeşilken üretim hiç çalışmıyordu. Bu sonda bayrakları BİLEREK
/// üretimdeki gibi kurar.
///
/// ═══ NEDEN HER ÜRETİCİ İÇİN DEĞİL, TEK KONTROL ═══
///
/// Kontrol zaten tek yerde: `AccountingVoucherService`in satır
/// doğrulaması. Üretici başına sonda yazmak, yarın eklenecek onuncu
/// üreticiyi kapsamazdı. Bu sonda kapının KENDİSİNİ ölçüyor.
/// </summary>
[Collection("Integration")]
public sealed class FisBoyutZorunluluguTests(DatabaseFixture fixture)
{
    private sealed record Zemin(Guid CompanyId, Guid ProjeliHesap, Guid MerkezliHesap, Guid SadeHesap, Guid ProjectId, string ProjeKodu);

    private async Task<Zemin> ZeminKurAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ek = Guid.NewGuid().ToString("N")[..8];
        var proje = await TestDataFactory.CreateProjectAsync(db, ek);

        AccountingAccount Hesap(string kod, string ad, bool projeZorunlu, bool merkezZorunlu) =>
            new()
            {
                CompanyId = proje.CompanyId,
                Code = kod,
                Name = ad,
                Level = 3,
                Nature = AccountingAccountNature.Debit,
                IsPostingAllowed = true,
                RequiresProject = projeZorunlu,
                RequiresCostCenter = merkezZorunlu,
            };

        // ÜRETİMDEKİ GİBİ: 150 proje zorunlu, 770 masraf merkezi zorunlu.
        var projeli = Hesap($"150.{ek[..3]}", "Proje zorunlu hesap", projeZorunlu: true, merkezZorunlu: false);
        var merkezli = Hesap($"770.{ek[..3]}", "Masraf merkezi zorunlu hesap", projeZorunlu: false, merkezZorunlu: true);
        var sade = Hesap($"379.{ek[..3]}", "Boyutsuz hesap", projeZorunlu: false, merkezZorunlu: false);

        db.AccountingAccounts.AddRange(projeli, merkezli, sade);
        await db.SaveChangesAsync();

        // ÖLÇÜM SAĞLIĞI: bayraklar GERÇEKTEN yazıldı mı? Yazılmadıysa
        // aşağıdaki "kırmızı yanmalı" iddiaları boş kümede koşar.
        var yazilan = await db.AccountingAccounts
            .Where(x => x.Id == projeli.Id || x.Id == merkezli.Id)
            .Select(x => new { x.Code, x.RequiresProject, x.RequiresCostCenter })
            .ToListAsync();

        Assert.Contains(yazilan, x => x.RequiresProject);
        Assert.Contains(yazilan, x => x.RequiresCostCenter);

        return new Zemin(proje.CompanyId, projeli.Id, merkezli.Id, sade.Id, proje.Id, proje.Code);
    }

    private async Task<Exception?> FisKesAsync(
        Zemin z, Guid borcHesap, Guid? projectId, string? costCenterCode)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var vouchers = scope.ServiceProvider.GetRequiredService<IAccountingVoucherService>();

        var istek = new CreateAccountingVoucherRequest(
            CompanyId: z.CompanyId,
            VoucherType: (int)AccountingVoucherType.Journal,
            VoucherDate: DateTime.UtcNow.Date,
            CurrencyCode: "TRY",
            ExchangeRate: 1m,
            Description: "Boyut zorunluluğu sondası",
            ReferenceNumber: null,
            SourceModule: "Sonda",
            SourceEntityId: null,
            Lines:
            [
                new AccountingVoucherLineRequest(borcHesap, "borç", 100m, 0m, "TRY", 1m,
                    null, projectId, costCenterCode, null, DateTime.UtcNow.Date, null),
                new AccountingVoucherLineRequest(z.SadeHesap, "alacak", 0m, 100m, "TRY", 1m,
                    null, null, null, null, DateTime.UtcNow.Date, null),
            ]);

        return await Record.ExceptionAsync(() => vouchers.CreateAsync(istek, CancellationToken.None));
    }

    /// <summary>
    /// KIRMIZIYA DÖNERSE: proje zorunlu bir hesaba projesiz fiş kesilir
    /// ve mal kabul/sayım sessizce yanlış fiş üretir.
    /// </summary>
    [Fact]
    public async Task ProjeZorunluHesaba_Projesiz_KIRMIZI()
    {
        var z = await ZeminKurAsync();

        var hata = await FisKesAsync(z, z.ProjeliHesap, projectId: null, costCenterCode: null);

        Assert.NotNull(hata);
        Assert.Contains("proje seçimi zorunludur", hata!.Message, StringComparison.Ordinal);
    }

    /// <summary>POZİTİF KONTROL: proje verilince aynı fiş GEÇMELİ.</summary>
    [Fact]
    public async Task ProjeZorunluHesaba_Projeyle_YESIL()
    {
        var z = await ZeminKurAsync();

        var hata = await FisKesAsync(z, z.ProjeliHesap, projectId: z.ProjectId, costCenterCode: null);

        Assert.True(hata is null, $"Proje verildiği hâlde düştü: {hata?.Message}");
    }

    /// <summary>
    /// KIRMIZIYA DÖNERSE: masraf merkezi zorunlu hesaba merkezsiz fiş
    /// kesilir — 89 gider hesabının hepsinde aynı kural var.
    /// </summary>
    [Fact]
    public async Task MasrafMerkeziZorunluHesaba_Merkezsiz_KIRMIZI()
    {
        var z = await ZeminKurAsync();

        var hata = await FisKesAsync(z, z.MerkezliHesap, projectId: null, costCenterCode: null);

        Assert.NotNull(hata);
        Assert.Contains("masraf merkezi zorunludur", hata!.Message, StringComparison.Ordinal);
    }

    /// <summary>POZİTİF KONTROL: masraf merkezi verilince GEÇMELİ.</summary>
    [Fact]
    public async Task MasrafMerkeziZorunluHesaba_Merkezle_YESIL()
    {
        var z = await ZeminKurAsync();

        var hata = await FisKesAsync(z, z.MerkezliHesap, projectId: null, costCenterCode: "MERKEZ");

        Assert.True(hata is null, $"Masraf merkezi verildiği hâlde düştü: {hata?.Message}");
    }

    /// <summary>
    /// ÖLÇÜM SAĞLIĞI (Kural 48): boyutsuz hesap her iki hâlde de geçmeli.
    /// Geçmiyorsa yukarıdaki kırmızılar boyut kuralını değil BAŞKA bir
    /// şeyi ölçüyor demektir.
    /// </summary>
    [Fact]
    public async Task BoyutsuzHesap_HerHaldeGecer()
    {
        var z = await ZeminKurAsync();

        Assert.Null(await FisKesAsync(z, z.SadeHesap, null, null));
        Assert.Null(await FisKesAsync(z, z.SadeHesap, z.ProjectId, "MERKEZ"));
    }
}
