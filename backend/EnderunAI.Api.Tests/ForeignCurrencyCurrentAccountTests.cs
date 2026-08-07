using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Market;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Dövizli cari bakiye ve ekstre (D2).
///
/// Sorun somuttu: cari bakiyesi yalnızca TL (defter) tarafından
/// okunuyordu. 10.000 dolarlık bir tedarikçi için ekranda tek bir TL
/// rakamı vardı; "kaç dolar borcumuz var" sorusunun cevabı yoktu ve
/// kur değiştikçe TL rakam da hareket ettiği için borç azalmış/artmış
/// gibi görünüyordu.
///
/// İki rakamı bilinçli olarak ayrı tutuyoruz:
/// - DEFTER değeri: her hareket kendi günündeki kurla TL'ye çevrilir,
///   muhasebe bakiyesi budur ve geriye dönük değişmez.
/// - DEĞERLEME değeri: aynı döviz bakiyesinin bugünkü kurla karşılığı.
/// Aradaki fark gerçekleşmemiş kur farkıdır — burada RAPORLANIR,
/// fiş kesilmez (o D3'ün işi).
///
/// PARA BİRİMİ SEÇİMİ: kur arşivi (ExchangeRates) şirketten bağımsız,
/// TEK ve ORTAK bir tablodur; başka test paketleri USD/EUR kurlarını
/// yıllar geriye dolduruyor ve kur araması geriye doğru yürüdüğü için
/// hangi tarihi verirsek verelim bir kur buluyor. Bu yüzden burada
/// başka hiçbir testin dokunmadığı NOK ve DKK kullanılıyor.
/// </summary>
[Collection("Integration")]
public sealed class ForeignCurrencyCurrentAccountTests(DatabaseFixture fixture)
{
    private const string Foreign = "NOK";

    /// <summary>Hiçbir testin kur yazmadığı para birimi.</summary>
    private const string RatelessForeign = "DKK";

    private async Task<(Guid CompanyId, Guid CurrentAccountId, Guid ProjectId)>
        CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        db.AccountingAccounts.AddRange(
            new AccountingAccount
            {
                CompanyId = project.CompanyId, Code = "320", Name = "Satıcılar",
                Nature = AccountingAccountNature.Credit, Level = 3,
                IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = project.CompanyId, Code = "153", Name = "Ticari Mal",
                Nature = AccountingAccountNature.Debit, Level = 3,
                IsPostingAllowed = true
            });

        await db.SaveChangesAsync();

        return (project.CompanyId, project.EmployerCurrentAccountId!.Value, project.Id);
    }

    /// <summary>
    /// Cari boyutu dolu, dengeli ve kesinleşmiş bir fiş yazar.
    /// Cari satırı BORÇ tarafındadır: bakiye pozitif çıkar.
    /// </summary>
    private async Task PostVoucherAsync(
        Guid companyId, Guid currentAccountId, Guid projectId,
        DateTime date, decimal amount, string currencyCode, decimal rate)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var debitAccount = await db.AccountingAccounts
            .Where(x => x.CompanyId == companyId && x.Code == "153")
            .Select(x => x.Id).SingleAsync();
        var creditAccount = await db.AccountingAccounts
            .Where(x => x.CompanyId == companyId && x.Code == "320")
            .Select(x => x.Id).SingleAsync();

        var local = decimal.Round(amount * rate, 2);

        db.AccountingVouchers.Add(new AccountingVoucher
        {
            CompanyId = companyId,
            VoucherNumber = $"D2-{Guid.NewGuid():N}"[..14],
            VoucherType = AccountingVoucherType.Journal,
            Status = AccountingVoucherStatus.Posted,
            VoucherDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
            FiscalYear = date.Year,
            FiscalPeriod = date.Month,
            CurrencyCode = currencyCode,
            ExchangeRate = rate,
            // Fiş TL (yerel) tutarlar üzerinden denkleşir.
            TotalDebit = local,
            TotalCredit = local,
            PostedAtUtc = DateTime.UtcNow,
            Lines =
            [
                new AccountingVoucherLine
                {
                    LineNumber = 1,
                    AccountingAccountId = debitAccount,
                    DebitAmount = amount, DebitAmountLocal = local,
                    CreditAmount = 0m, CreditAmountLocal = 0m,
                    CurrencyCode = currencyCode, ExchangeRate = rate,
                    CurrentAccountId = currentAccountId, ProjectId = projectId
                },
                new AccountingVoucherLine
                {
                    LineNumber = 2,
                    AccountingAccountId = creditAccount,
                    DebitAmount = 0m, DebitAmountLocal = 0m,
                    CreditAmount = amount, CreditAmountLocal = local,
                    CurrencyCode = currencyCode, ExchangeRate = rate,
                    ProjectId = projectId
                }
            ]
        });

        await db.SaveChangesAsync();
    }

    private async Task SeedRateAsync(string currencyCode, DateTime date, decimal buying)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var day = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

        var existing = await db.ExchangeRates.SingleOrDefaultAsync(
            x => x.RateDate == day && x.CurrencyCode == currencyCode);

        if (existing is not null)
        {
            // Arşiv ortak tablo: başka bir test aynı günü yazdıysa
            // atlamak yerine bu testin beklediği kura çekiyoruz, yoksa
            // testler birbirinin kurunu okur.
            existing.ForexBuying = buying;
            existing.ForexSelling = buying + 0.1m;
        }
        else
        {
            db.ExchangeRates.Add(new ExchangeRate
            {
                RateDate = day,
                CurrencyCode = currencyCode,
                Unit = 1,
                ForexBuying = buying,
                ForexSelling = buying + 0.1m,
                Source = "TCMB"
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Bakiye para birimi kırılımıyla dönmeli: TL toplam eskisi gibi
    /// kalırken dövizin kendi tutarı ayrıca okunabilmeli.
    /// </summary>
    [Fact]
    public async Task Balances_BreakDownByCurrency()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 2, 10), 1_000m, "TRY", 1m);
        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 2, 20), 1_000m, Foreign, 3m);

        var balances = await client.GetFromJsonAsync<JsonElement>(
            $"/api/current-accounts/balances?companyId={companyId}");

        var row = balances.EnumerateArray()
            .Single(x => x.GetProperty("currentAccountId").GetGuid() == currentAccountId);

        // TL bakiye = 1.000 TL + (1.000 NOK × 3) = 4.000
        Assert.Equal(4_000m, row.GetProperty("balance").GetDecimal());
        Assert.True(row.GetProperty("hasForeignCurrency").GetBoolean());

        var currencies = row.GetProperty("currencyBalances").EnumerateArray().ToList();
        Assert.Equal(2, currencies.Count);

        // TL her zaman ilk sırada
        Assert.Equal("TRY", currencies[0].GetProperty("currencyCode").GetString());
        Assert.Equal(1_000m, currencies[0].GetProperty("balance").GetDecimal());

        var foreign = currencies
            .Single(x => x.GetProperty("currencyCode").GetString() == Foreign);

        Assert.Equal(1_000m, foreign.GetProperty("balance").GetDecimal());
        Assert.Equal(3_000m, foreign.GetProperty("balanceLocal").GetDecimal());
    }

    /// <summary>
    /// TL bakiyesi değişmeyen carilerde sözleşme aynen korunmalı —
    /// dövizsiz cari için ekranda yeni bir şey çıkmamalı.
    /// </summary>
    [Fact]
    public async Task Balances_LocalOnlyAccount_KeepsExistingContract()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 4, 1), 2_500m, "TRY", 1m);

        var balances = await client.GetFromJsonAsync<JsonElement>(
            $"/api/current-accounts/balances?companyId={companyId}");

        var row = balances.EnumerateArray()
            .Single(x => x.GetProperty("currentAccountId").GetGuid() == currentAccountId);

        Assert.Equal(2_500m, row.GetProperty("totalDebit").GetDecimal());
        Assert.Equal(2_500m, row.GetProperty("balance").GetDecimal());
        Assert.Equal(1, row.GetProperty("movementCount").GetInt32());
        Assert.False(row.GetProperty("hasForeignCurrency").GetBoolean());
    }

    /// <summary>
    /// Ekstrede her para birimi kendi yürüyen bakiyesini taşımalı:
    /// farklı kurlardan geçen iki dövizli hareket, döviz tarafında
    /// toplanırken TL tarafında kendi kurlarıyla birikir.
    /// </summary>
    [Fact]
    public async Task Statement_RunsSeparateBalancePerCurrency()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 3, 1), 100m, Foreign, 3m);
        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 3, 2), 100m, Foreign, 4m);

        var statement = await client.GetFromJsonAsync<JsonElement>(
            $"/api/current-accounts/{currentAccountId}/statement");

        var lines = statement.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);

        // Döviz tarafı: 100 → 200
        Assert.Equal(100m, lines[0].GetProperty("runningBalanceOriginal").GetDecimal());
        Assert.Equal(200m, lines[1].GetProperty("runningBalanceOriginal").GetDecimal());

        // TL tarafı: 300 → 700 (her hareket kendi kuruyla)
        Assert.Equal(300m, lines[0].GetProperty("runningBalance").GetDecimal());
        Assert.Equal(700m, lines[1].GetProperty("runningBalance").GetDecimal());

        Assert.Equal(3m, lines[0].GetProperty("exchangeRate").GetDecimal());
        Assert.Equal(Foreign, lines[1].GetProperty("currencyCode").GetString());

        var summary = statement.GetProperty("currencySummary").EnumerateArray()
            .Single(x => x.GetProperty("currencyCode").GetString() == Foreign);

        Assert.Equal(200m, summary.GetProperty("closingBalance").GetDecimal());
        Assert.Equal(700m, summary.GetProperty("closingBalanceLocal").GetDecimal());
        Assert.True(statement.GetProperty("hasForeignCurrency").GetBoolean());
    }

    /// <summary>
    /// Dönem başı devri döviz tarafında da taşınmalı; yürüyen döviz
    /// bakiyesi sıfırdan başlarsa ekstre yanlış borç gösterir.
    /// </summary>
    [Fact]
    public async Task Statement_OpeningBalance_CarriesForeignCurrency()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 1, 10), 500m, Foreign, 2m);
        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 6, 10), 200m, Foreign, 5m);

        var statement = await client.GetFromJsonAsync<JsonElement>(
            $"/api/current-accounts/{currentAccountId}/statement?startDate=2026-05-01");

        var summary = statement.GetProperty("currencySummary").EnumerateArray()
            .Single(x => x.GetProperty("currencyCode").GetString() == Foreign);

        Assert.Equal(500m, summary.GetProperty("openingBalance").GetDecimal());
        Assert.Equal(1_000m, summary.GetProperty("openingBalanceLocal").GetDecimal());
        Assert.Equal(700m, summary.GetProperty("closingBalance").GetDecimal());

        var lines = statement.GetProperty("lines").EnumerateArray().ToList();
        Assert.Single(lines);
        Assert.Equal(700m, lines[0].GetProperty("runningBalanceOriginal").GetDecimal());
    }

    /// <summary>
    /// Para birimi filtresi verildiğinde ekstre yalnızca o dövizi
    /// göstermeli — TL hareketleri araya karışmamalı.
    /// </summary>
    [Fact]
    public async Task Statement_CurrencyFilter_LimitsToRequestedCurrency()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 7, 1), 900m, "TRY", 1m);
        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 7, 2), 50m, Foreign, 6m);

        var statement = await client.GetFromJsonAsync<JsonElement>(
            $"/api/current-accounts/{currentAccountId}/statement?currency={Foreign}");

        Assert.Equal(1, statement.GetProperty("lineCount").GetInt32());
        Assert.Equal(Foreign, statement.GetProperty("currency").GetString());
        // TL kolonu yine defter değerini gösterir: 50 × 6
        Assert.Equal(300m, statement.GetProperty("closingBalance").GetDecimal());

        var currencies = statement.GetProperty("currencySummary").EnumerateArray().ToList();
        Assert.Single(currencies);
        Assert.Equal(50m, currencies[0].GetProperty("closingBalance").GetDecimal());
    }

    /// <summary>
    /// Değerleme, defter değeri ile bugünkü kurun farkını göstermeli.
    /// Defter DEĞİŞMEZ; fark ayrı bir rakam olarak raporlanır.
    /// </summary>
    [Fact]
    public async Task Valuation_ReportsDifferenceAgainstBookValue()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // 1.000 NOK, 3,00 kurla defterlendi → defter değeri 3.000 TL
        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 2, 20), 1_000m, Foreign, 3m);

        var valuationDay = new DateTime(2026, 8, 20);
        await SeedRateAsync(Foreign, valuationDay, 4m);

        var valuation = await client.GetFromJsonAsync<JsonElement>(
            $"/api/current-accounts/{currentAccountId}/currency-valuation" +
            $"?valuationDate={valuationDay:yyyy-MM-dd}");

        var row = valuation.GetProperty("currencies").EnumerateArray()
            .Single(x => x.GetProperty("currencyCode").GetString() == Foreign);

        Assert.True(row.GetProperty("rateAvailable").GetBoolean());
        Assert.Equal(1_000m, row.GetProperty("balance").GetDecimal());
        Assert.Equal(3_000m, row.GetProperty("bookValueLocal").GetDecimal());
        Assert.Equal(4m, row.GetProperty("valuationRate").GetDecimal());
        Assert.Equal(4_000m, row.GetProperty("valuedLocal").GetDecimal());
        Assert.Equal(1_000m, row.GetProperty("difference").GetDecimal());

        Assert.Equal(1_000m, valuation.GetProperty("totalDifference").GetDecimal());
        Assert.False(valuation.GetProperty("hasMissingRate").GetBoolean());

        // Değerleme defteri BOZMAMALI: bakiye hâlâ defter değeri.
        var balances = await client.GetFromJsonAsync<JsonElement>(
            $"/api/current-accounts/balances?companyId={companyId}");
        var balanceRow = balances.EnumerateArray()
            .Single(x => x.GetProperty("currentAccountId").GetGuid() == currentAccountId);

        Assert.Equal(3_000m, balanceRow.GetProperty("balance").GetDecimal());
    }

    /// <summary>
    /// Kuru olmayan döviz için tutar UYDURULMAMALI: satır "kur yok"
    /// döner, toplam farka girmez ve çağıran toplamın eksik olduğunu
    /// bilir. Yanlış kurla değerlenmiş bir bakiye, hiç değerlenmemiş
    /// olandan çok daha pahalıya patlar.
    /// </summary>
    [Fact]
    public async Task Valuation_WithoutRate_DoesNotInventOne()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 2, 20), 700m, RatelessForeign, 5m);

        var valuation = await client.GetFromJsonAsync<JsonElement>(
            $"/api/current-accounts/{currentAccountId}/currency-valuation" +
            "?valuationDate=2026-09-01");

        var row = valuation.GetProperty("currencies").EnumerateArray()
            .Single(x => x.GetProperty("currencyCode").GetString() == RatelessForeign);

        Assert.False(row.GetProperty("rateAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("valuationRate").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("difference").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(
            row.GetProperty("message").GetString()));

        Assert.True(valuation.GetProperty("hasMissingRate").GetBoolean());
        Assert.Equal(0m, valuation.GetProperty("totalDifference").GetDecimal());
    }

    /// <summary>
    /// Değerleme TL bakiyesini kapsamaz — yerel para biriminin kur
    /// farkı olmaz.
    /// </summary>
    [Fact]
    public async Task Valuation_IgnoresLocalCurrency()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 5, 5), 1_200m, "TRY", 1m);

        var valuation = await client.GetFromJsonAsync<JsonElement>(
            $"/api/current-accounts/{currentAccountId}/currency-valuation");

        Assert.Empty(valuation.GetProperty("currencies").EnumerateArray());
        Assert.Equal(0m, valuation.GetProperty("totalDifference").GetDecimal());
        Assert.False(valuation.GetProperty("hasMissingRate").GetBoolean());
    }
}
