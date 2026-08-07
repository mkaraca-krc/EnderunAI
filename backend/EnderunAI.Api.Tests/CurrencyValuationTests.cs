using System.Net;
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
/// Dönem sonu kur değerlemesi (D3).
///
/// Dövizli cari bakiyesi defterde, hareketlerin kendi günündeki kurla
/// TL'ye çevrilmiş haliyle durur. Dönem sonunda bu değerin o günkü
/// kurla karşılığına çekilmesi ve farkın 646/656'ya yazılması gerekir.
///
/// Bu paketin asıl güvencesi ÇİFT KAYIT OLMAMASI: değerleme satırları
/// TL kesildiği için dövizin kendi bakiyesi değişmez ve bir sonraki
/// değerleme aynı farkı yeniden bulur. Kümülatif mantık çalışmazsa
/// aynı kâr iki kez yazılır ve vergi matrahı şişer.
///
/// PARA BİRİMİ: kur arşivi ortak tablo olduğu ve başka paketler
/// USD/EUR'yu yıllar geriye doldurduğu için burada CHF kullanılıyor.
/// </summary>
[Collection("Integration")]
public sealed class CurrencyValuationTests(DatabaseFixture fixture)
{
    private const string Foreign = "CHF";

    private async Task<(Guid CompanyId, Guid CurrentAccountId, Guid ProjectId)>
        CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        (string Code, string Name, AccountingAccountNature Nature)[] accounts =
        [
            ("120", "Alıcılar", AccountingAccountNature.Debit),
            ("320", "Satıcılar", AccountingAccountNature.Credit),
            ("153", "Ticari Mal", AccountingAccountNature.Debit),
            ("646.01", "Kambiyo Kârı", AccountingAccountNature.Credit),
            ("656.01", "Kambiyo Zararı", AccountingAccountNature.Debit)
        ];

        foreach (var (code, name, nature) in accounts)
        {
            db.AccountingAccounts.Add(new AccountingAccount
            {
                CompanyId = project.CompanyId,
                Code = code,
                Name = name,
                Nature = nature,
                Level = code.Contains('.') ? 4 : 3,
                IsPostingAllowed = true
            });
        }

        await db.SaveChangesAsync();

        // Cari 120'ye bağlanıyor: değerleme satırının hangi hesaba
        // yazılacağı buradan çözülüyor.
        var receivableId = await db.AccountingAccounts
            .Where(x => x.CompanyId == project.CompanyId && x.Code == "120")
            .Select(x => x.Id).SingleAsync();

        var currentAccountId = project.EmployerCurrentAccountId!.Value;

        var currentAccount = await db.CurrentAccounts.SingleAsync(
            x => x.Id == currentAccountId);
        currentAccount.ReceivableAccountingAccountId = receivableId;

        await db.SaveChangesAsync();

        return (project.CompanyId, currentAccountId, project.Id);
    }

    /// <summary>
    /// Cariyi BORÇLU yapan dengeli ve kesinleşmiş bir fiş (120 borç /
    /// 153 alacak), verilen kurla.
    /// </summary>
    private async Task PostVoucherAsync(
        Guid companyId, Guid currentAccountId, Guid projectId,
        DateTime date, decimal amount, decimal rate)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var receivable = await db.AccountingAccounts
            .Where(x => x.CompanyId == companyId && x.Code == "120")
            .Select(x => x.Id).SingleAsync();
        var counter = await db.AccountingAccounts
            .Where(x => x.CompanyId == companyId && x.Code == "153")
            .Select(x => x.Id).SingleAsync();

        var local = decimal.Round(amount * rate, 2);

        db.AccountingVouchers.Add(new AccountingVoucher
        {
            CompanyId = companyId,
            VoucherNumber = $"D3-{Guid.NewGuid():N}"[..14],
            VoucherType = AccountingVoucherType.Journal,
            Status = AccountingVoucherStatus.Posted,
            VoucherDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
            FiscalYear = date.Year,
            FiscalPeriod = date.Month,
            CurrencyCode = Foreign,
            ExchangeRate = rate,
            TotalDebit = local,
            TotalCredit = local,
            PostedAtUtc = DateTime.UtcNow,
            Lines =
            [
                new AccountingVoucherLine
                {
                    LineNumber = 1, AccountingAccountId = receivable,
                    DebitAmount = amount, DebitAmountLocal = local,
                    CreditAmount = 0m, CreditAmountLocal = 0m,
                    CurrencyCode = Foreign, ExchangeRate = rate,
                    CurrentAccountId = currentAccountId, ProjectId = projectId
                },
                new AccountingVoucherLine
                {
                    LineNumber = 2, AccountingAccountId = counter,
                    DebitAmount = 0m, DebitAmountLocal = 0m,
                    CreditAmount = amount, CreditAmountLocal = local,
                    CurrencyCode = Foreign, ExchangeRate = rate,
                    ProjectId = projectId
                }
            ]
        });

        await db.SaveChangesAsync();
    }

    private async Task SeedRateAsync(DateTime date, decimal buying)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var day = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

        var existing = await db.ExchangeRates.SingleOrDefaultAsync(
            x => x.RateDate == day && x.CurrencyCode == Foreign);

        if (existing is not null)
        {
            existing.ForexBuying = buying;
            existing.ForexSelling = buying + 0.1m;
        }
        else
        {
            db.ExchangeRates.Add(new ExchangeRate
            {
                RateDate = day,
                CurrencyCode = Foreign,
                Unit = 1,
                ForexBuying = buying,
                ForexSelling = buying + 0.1m,
                Source = "TCMB"
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Önizleme farkı doğru hesaplamalı ve hiçbir kayıt yazmamalı.
    /// </summary>
    [Fact]
    public async Task Preview_ComputesDifference_AndWritesNothing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // 1.000 CHF, 30 kurundan → defter 30.000
        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 3, 1), 1_000m, 30m);

        var valuationDay = new DateTime(2026, 3, 31);
        await SeedRateAsync(valuationDay, 35m);

        var preview = await client.GetFromJsonAsync<JsonElement>(
            $"/api/accounting/currency-valuation/preview?companyId={companyId}" +
            $"&valuationDate={valuationDay:yyyy-MM-dd}");

        var line = preview.GetProperty("lines").EnumerateArray()
            .Single(x => x.GetProperty("currentAccountId").GetGuid() == currentAccountId);

        Assert.True(line.GetProperty("rateAvailable").GetBoolean());
        Assert.Equal(1_000m, line.GetProperty("balance").GetDecimal());
        Assert.Equal(30_000m, line.GetProperty("bookValueLocal").GetDecimal());
        Assert.Equal(35_000m, line.GetProperty("valuedLocal").GetDecimal());
        Assert.Equal(5_000m, line.GetProperty("totalDifference").GetDecimal());
        Assert.Equal(0m, line.GetProperty("previouslyPosted").GetDecimal());
        Assert.Equal(5_000m, line.GetProperty("postableDifference").GetDecimal());

        Assert.Equal(5_000m, preview.GetProperty("totalGain").GetDecimal());
        Assert.Equal(0m, preview.GetProperty("totalLoss").GetDecimal());
        Assert.Equal(
            JsonValueKind.Null,
            preview.GetProperty("alreadyPostedRunId").ValueKind);

        // Önizleme hiçbir tur yazmamalı
        using var verify = fixture.Factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.CurrencyValuationRuns
            .Where(x => x.CompanyId == companyId).ToListAsync());
    }

    /// <summary>
    /// Fiş 120'yi farkla borçlandırmalı ve karşılığını 646'ya yazmalı;
    /// dövizin kendi bakiyesi DEĞİŞMEMELİ.
    /// </summary>
    [Fact]
    public async Task Post_WritesDifferenceTo646_AndKeepsForeignBalance()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 4, 1), 1_000m, 30m);

        var valuationDay = new DateTime(2026, 4, 30);
        await SeedRateAsync(valuationDay, 35m);

        var response = await client.PostAsJsonAsync(
            "/api/accounting/currency-valuation",
            new { companyId, valuationDate = valuationDay });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5_000m, payload.GetProperty("postedDifference").GetDecimal());

        var voucherId = payload.GetProperty("accountingVoucherId").GetGuid();

        using var verify = fixture.Factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        var lines = await db.AccountingVoucherLines
            .AsNoTracking()
            .Include(x => x.AccountingAccount)
            .Where(x => x.AccountingVoucherId == voucherId)
            .ToListAsync();

        var receivableLine = lines.Single(x => x.AccountingAccount.Code == "120");
        var gainLine = lines.Single(x => x.AccountingAccount.Code == "646.01");

        Assert.Equal(5_000m, receivableLine.DebitAmount);
        Assert.Equal(5_000m, gainLine.CreditAmount);

        // Değerleme satırları TL: dövizin kendi bakiyesini bozmamalı.
        Assert.Equal("TRY", receivableLine.CurrencyCode);
        Assert.Equal("TRY", gainLine.CurrencyCode);

        var balances = await client.GetFromJsonAsync<JsonElement>(
            $"/api/current-accounts/balances?companyId={companyId}");

        var row = balances.EnumerateArray()
            .Single(x => x.GetProperty("currentAccountId").GetGuid() == currentAccountId);

        var foreign = row.GetProperty("currencyBalances").EnumerateArray()
            .Single(x => x.GetProperty("currencyCode").GetString() == Foreign);

        // Hâlâ 1.000 CHF alacaklıyız; TL tarafı 5.000 arttı.
        Assert.Equal(1_000m, foreign.GetProperty("balance").GetDecimal());
        Assert.Equal(35_000m, row.GetProperty("balance").GetDecimal());
    }

    /// <summary>
    /// BU PAKETİN ASIL GÜVENCESİ: ikinci değerleme aynı farkı yeniden
    /// yazmamalı, yalnızca aradaki DEĞİŞİMİ defterlemeli. Kümülatif
    /// mantık çalışmazsa aynı kâr iki kez yazılır.
    /// </summary>
    [Fact]
    public async Task SecondValuation_PostsOnlyTheIncrement()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 5, 1), 1_000m, 30m);

        var firstDay = new DateTime(2026, 5, 31);
        await SeedRateAsync(firstDay, 35m);

        var first = await client.PostAsJsonAsync(
            "/api/accounting/currency-valuation",
            new { companyId, valuationDate = firstDay });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // İkinci dönem: kur 35 → 38. Toplam fark 8.000, ama 5.000
        // zaten yazıldı; bu turda yalnızca 3.000 yazılmalı.
        var secondDay = new DateTime(2026, 6, 30);
        await SeedRateAsync(secondDay, 38m);

        var preview = await client.GetFromJsonAsync<JsonElement>(
            $"/api/accounting/currency-valuation/preview?companyId={companyId}" +
            $"&valuationDate={secondDay:yyyy-MM-dd}");

        var line = preview.GetProperty("lines").EnumerateArray()
            .Single(x => x.GetProperty("currentAccountId").GetGuid() == currentAccountId);

        Assert.Equal(8_000m, line.GetProperty("totalDifference").GetDecimal());
        Assert.Equal(5_000m, line.GetProperty("previouslyPosted").GetDecimal());
        Assert.Equal(3_000m, line.GetProperty("postableDifference").GetDecimal());

        var second = await client.PostAsJsonAsync(
            "/api/accounting/currency-valuation",
            new { companyId, valuationDate = secondDay });

        var payload = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3_000m, payload.GetProperty("postedDifference").GetDecimal());
    }

    /// <summary>
    /// Aynı tarihe ikinci fiş kesilememeli — yoksa aynı fark iki kez
    /// deftere girer.
    /// </summary>
    [Fact]
    public async Task Post_SameDateTwice_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 7, 1), 1_000m, 30m);

        var day = new DateTime(2026, 7, 31);
        await SeedRateAsync(day, 35m);

        var first = await client.PostAsJsonAsync(
            "/api/accounting/currency-valuation",
            new { companyId, valuationDate = day });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            "/api/accounting/currency-valuation",
            new { companyId, valuationDate = day });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    /// <summary>
    /// İptal edilen tur kümülatif toplama girmemeli: iptalden sonra
    /// aynı fark yeniden yazılabilmeli.
    /// </summary>
    [Fact]
    public async Task ReversedRun_IsExcludedFromCumulativeTotal()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 8, 1), 1_000m, 30m);

        var day = new DateTime(2026, 8, 31);
        await SeedRateAsync(day, 35m);

        var posted = await client.PostAsJsonAsync(
            "/api/accounting/currency-valuation",
            new { companyId, valuationDate = day });

        var runId = (await posted.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var reversed = await client.PostAsJsonAsync(
            $"/api/accounting/currency-valuation/{runId}/reverse",
            new { reason = "Test iptali" });

        Assert.Equal(HttpStatusCode.OK, reversed.StatusCode);

        // İptalden sonra fark yeniden yazılabilir olmalı
        var preview = await client.GetFromJsonAsync<JsonElement>(
            $"/api/accounting/currency-valuation/preview?companyId={companyId}" +
            $"&valuationDate={day:yyyy-MM-dd}");

        var line = preview.GetProperty("lines").EnumerateArray()
            .Single(x => x.GetProperty("currentAccountId").GetGuid() == currentAccountId);

        Assert.Equal(0m, line.GetProperty("previouslyPosted").GetDecimal());
        Assert.Equal(5_000m, line.GetProperty("postableDifference").GetDecimal());
        Assert.Equal(
            JsonValueKind.Null,
            preview.GetProperty("alreadyPostedRunId").ValueKind);
    }

    /// <summary>
    /// Yazılacak fark yoksa fiş kesilmemeli — boş fiş defteri kirletir.
    /// </summary>
    [Fact]
    public async Task Post_WithoutDifference_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 9, 1), 1_000m, 30m);

        // Değerleme kuru defter kuruyla aynı → fark yok
        var day = new DateTime(2026, 9, 30);
        await SeedRateAsync(day, 30m);

        var response = await client.PostAsJsonAsync(
            "/api/accounting/currency-valuation",
            new { companyId, valuationDate = day });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
