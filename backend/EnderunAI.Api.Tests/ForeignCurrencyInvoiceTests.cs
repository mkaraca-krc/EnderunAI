using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Market;
using EnderunAI.Api.Services.EInvoice;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Dövizli fatura: kurun nereden geldiği ve deftere ne yazıldığı.
///
/// Buradaki asıl güvence, düzeltilen hatanın geri gelmemesi: içe
/// aktarma eskiden para birimini XML'den alıp kuru sabit 1 yazıyordu.
/// 6.000 USD'lik bir fatura deftere 6.000 TL olarak giriyor, tedarikçi
/// kırk küsur kat eksik alacaklanıyordu. TRY faturaların davranışının
/// hiç değişmediği de ayrıca doğrulanıyor.
/// </summary>
[Collection("Integration")]
public sealed class ForeignCurrencyInvoiceTests(DatabaseFixture fixture)
{
    private const string SupplierTaxNumber = "1234567890";

    /// <summary>2026-08-05 TCMB USD döviz alışı — canlı bültenle aynı.</summary>
    private const decimal UsdRate = 47.4881m;

    private static readonly DateTime InvoiceDate =
        new(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc);

    private async Task<(Guid CompanyId, Guid ProjectId, Guid ExpenseAccountId)>
        CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var company = await db.Companies.SingleAsync(x => x.Id == project.CompanyId);
        company.TaxNumber = EInvoiceFixtures.OurTaxNumber;

        var expenseAccount = new AccountingAccount
        {
            CompanyId = company.Id, Code = "740", Name = "Hizmet Üretim Maliyeti",
            Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
        };

        db.AccountingAccounts.AddRange(
            new AccountingAccount
            {
                CompanyId = company.Id, Code = "320", Name = "Satıcılar",
                Nature = AccountingAccountNature.Credit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = company.Id, Code = "191.01.03", Name = "İndirilecek KDV",
                Nature = AccountingAccountNature.Debit, Level = 5, IsPostingAllowed = true
            },
            expenseAccount);

        await EnsureRateAsync(db, InvoiceDate, "USD", UsdRate);
        await db.SaveChangesAsync();

        return (project.CompanyId, project.Id, expenseAccount.Id);
    }

    /// <summary>
    /// KURU YETKİLİ OLARAK YAZAR — "varsa dokunma" DEĞİL.
    ///
    /// Önce `AnyAsync` ile bakıp varsa atlıyordu ve bu, 2026-08-25'te
    /// deploy'u iki kez durdurdu:
    ///
    ///   `CommodityPriceTests:319` kendi günlerini GÖRELİ seçiyor
    ///   (`UtcNow.Date.AddDays(-20)`) ve o gün 2026-08-05'e denk
    ///   geldi — bu sınıfın SABİT tarihi. O test kuru 44 olarak
    ///   ÜZERİNE YAZIYOR; buradaki "varsa dokunma" ise 47,4881'i hiç
    ///   tohumlayamıyor. Sonuç: beklenen 47,4881, gelen 44.
    ///
    /// Dün aynı hesap 08-04 veriyordu ve çakışma yoktu; gece
    /// yarısından önceki koşum bu yüzden yeşildi. Yani kusur
    /// TARİHE BAĞLI ve ~ayda birkaç gün kendini gösteriyor.
    ///
    /// Sabit tarih değiştirilmedi: `UsdRate` o günün GERÇEK TCMB
    /// bültenine ait. Değiştirmek testin anlamını bozardı. Bunun
    /// yerine tohumlama yetkili hale getirildi — bu sınıf kendi
    /// kurunu her koşuda garanti eder.
    /// </summary>
    private static async Task EnsureRateAsync(
        AppDbContext db, DateTime date, string currency, decimal buying)
    {
        var mevcut = await db.ExchangeRates
            .SingleOrDefaultAsync(x => x.RateDate == date && x.CurrencyCode == currency);

        if (mevcut is not null)
        {
            mevcut.Unit = 1;
            mevcut.ForexBuying = buying;
            mevcut.ForexSelling = buying + 0.0855m;
            mevcut.Source = "TCMB";
        }
        else
        {
            db.ExchangeRates.Add(new ExchangeRate
            {
                RateDate = date,
                CurrencyCode = currency,
                Unit = 1,
                ForexBuying = buying,
                ForexSelling = buying + 0.0855m,
                Source = "TCMB"
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<Guid> CreateSupplierAsync(Guid companyId, string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = new CurrentAccount
        {
            CompanyId = companyId,
            Code = $"TED-{suffix}",
            Title = $"Global Supply {suffix}",
            TaxNumber = SupplierTaxNumber,
            Roles = CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };

        db.CurrentAccounts.Add(account);
        await db.SaveChangesAsync();

        return account.Id;
    }

    private static MultipartFormDataContent BuildUpload(string name, string xml)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(xml)), "files", name);

        return content;
    }

    private async Task<Guid> ImportAsync(
        HttpClient client, Guid companyId, Guid supplierId, Guid projectId, string xml)
    {
        var previewResponse = await client.PostAsync(
            $"/api/e-invoice/import/preview?companyId={companyId}",
            BuildUpload("doviz.xml", xml));

        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);

        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = preview.GetProperty("items").EnumerateArray()
            .Single().GetProperty("token").GetString();

        var commit = await client.PostAsJsonAsync(
            $"/api/e-invoice/import/commit?companyId={companyId}",
            new
            {
                items = new[]
                {
                    new
                    {
                        token,
                        currentAccountId = supplierId,
                        createCurrentAccount = false,
                        projectId
                    }
                }
            });

        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);

        var result = await commit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("createdCount").GetInt32());

        return result.GetProperty("created").EnumerateArray()
            .Single().GetProperty("invoiceId").GetGuid();
    }

    [Fact]
    public void Parser_ReadsDeclaredExchangeRate()
    {
        var parsed = UblTrInvoiceParser.Parse(
            EInvoiceFixtures.ForeignCurrencyPurchaseInvoice(declaredRate: 47.9m));

        Assert.Equal("USD", parsed.CurrencyCode);
        Assert.Equal(47.9m, parsed.ExchangeRate);
    }

    [Fact]
    public void Parser_TryInvoice_HasNoExchangeRate()
    {
        // TL faturada kur kavramı yok; boş yere 1 yazılmamalı.
        var parsed = UblTrInvoiceParser.Parse(EInvoiceFixtures.PurchaseInvoice());

        Assert.Equal("TRY", parsed.CurrencyCode);
        Assert.Null(parsed.ExchangeRate);
    }

    [Fact]
    public async Task Import_ForeignInvoiceWithoutDeclaredRate_UsesArchivedTcmbRate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, expenseAccountId) = await CreateContextAsync(suffix);
        var supplierId = await CreateSupplierAsync(companyId, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var invoiceId = await ImportAsync(
            client, companyId, supplierId, projectId,
            EInvoiceFixtures.ForeignCurrencyPurchaseInvoice(
                invoiceNumber: $"USD{suffix}"));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var invoice = await db.SupplierInvoices.AsNoTracking()
            .SingleAsync(x => x.Id == invoiceId);

        Assert.Equal("USD", invoice.CurrencyCode);

        // Düzeltilen hata: burası eskiden 1 idi.
        Assert.Equal(UsdRate, invoice.ExchangeRate);
        Assert.Equal(6_000m, invoice.GrandTotal);
    }

    [Fact]
    public async Task Import_ForeignInvoiceWithDeclaredRate_PrefersDocumentRate()
    {
        // Faturadaki TL tutarlar satıcının kuruyla hesaplanmış; TCMB
        // kuruyla ezilirse belge kendi içinde tutarsız hale gelir.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, expenseAccountId) = await CreateContextAsync(suffix);
        var supplierId = await CreateSupplierAsync(companyId, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var invoiceId = await ImportAsync(
            client, companyId, supplierId, projectId,
            EInvoiceFixtures.ForeignCurrencyPurchaseInvoice(
                invoiceNumber: $"USDD{suffix}", declaredRate: 48.1234m));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var invoice = await db.SupplierInvoices.AsNoTracking()
            .SingleAsync(x => x.Id == invoiceId);

        Assert.Equal(48.1234m, invoice.ExchangeRate);
    }

    [Fact]
    public async Task Import_TryInvoice_StillUsesRateOne()
    {
        // Geriye uyum: TL akışında hiçbir şey değişmemeli.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, expenseAccountId) = await CreateContextAsync(suffix);
        var supplierId = await CreateSupplierAsync(companyId, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var invoiceId = await ImportAsync(
            client, companyId, supplierId, projectId,
            EInvoiceFixtures.PurchaseInvoice(invoiceNumber: $"TRY{suffix}"));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var invoice = await db.SupplierInvoices.AsNoTracking()
            .SingleAsync(x => x.Id == invoiceId);

        Assert.Equal("TRY", invoice.CurrencyCode);
        Assert.Equal(1m, invoice.ExchangeRate);
        Assert.Equal(3_101.76m, invoice.GrandTotal);
    }

    [Fact]
    public async Task ManualInvoice_ForeignCurrencyWithoutRate_UsesArchiveAndPostsInLira()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, expenseAccountId) = await CreateContextAsync(suffix);
        var supplierId = await CreateSupplierAsync(companyId, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // Kur alanı 1 gönderiliyor: kullanıcı kur girmemiş demektir,
        // arşivden bulunmalı.
        var response = await client.PostAsJsonAsync("/api/supplier-invoices", new
        {
            companyId,
            supplierCurrentAccountId = supplierId,
            projectId,
            invoiceNumber = $"MAN-{suffix}",
            invoiceDate = InvoiceDate,
            currencyCode = "USD",
            exchangeRate = 1m,
            invoiceType = 1,
            items = new[]
            {
                new
                {
                    description = "Imported unit",
                    quantity = 10m,
                    unit = "AD",
                    unitPrice = 500m,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null,
                    expenseAccountId
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var invoiceId = created.GetProperty("id").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var invoice = await db.SupplierInvoices.AsNoTracking()
            .SingleAsync(x => x.Id == invoiceId);

        Assert.Equal(UsdRate, invoice.ExchangeRate);
        Assert.Equal(6_000m, invoice.GrandTotal);

        // TL karşılığı: 6.000 × 47,4881 = 284.928,60
        Assert.Equal(
            284_928.60m, decimal.Round(invoice.GrandTotal * invoice.ExchangeRate, 2));
    }

    [Fact]
    public async Task ManualInvoice_WithoutArchivedRate_IsRejected()
    {
        // Kur yoksa fatura kaydedilmemeli: uydurma kurla defterlenen
        // fatura, hiç defterlenmemişten çok daha pahalıya patlar.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, expenseAccountId) = await CreateContextAsync(suffix);
        var supplierId = await CreateSupplierAsync(companyId, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/supplier-invoices", new
        {
            companyId,
            supplierCurrentAccountId = supplierId,
            projectId,
            invoiceNumber = $"NORATE-{suffix}",
            // Arşivdeki ilk günden önce: kur yok.
            invoiceDate = new DateTime(2015, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            currencyCode = "USD",
            exchangeRate = 1m,
            invoiceType = 1,
            items = new[]
            {
                new
                {
                    description = "Imported unit",
                    quantity = 1m,
                    unit = "AD",
                    unitPrice = 100m,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null,
                    expenseAccountId
                }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("kur", body, StringComparison.OrdinalIgnoreCase);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.SupplierInvoices
            .AnyAsync(x => x.InvoiceNumber == $"NORATE-{suffix}"));
    }
}
