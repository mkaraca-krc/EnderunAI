using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Tax;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Vergi takvimi ve nakit akış entegrasyonu.
///
/// Buradaki asıl güvence şu: ödenen vergi listede kalmamalı. Kalsaydı
/// nakit akış şirketi olduğundan dar gösterir ve yönetim gereksiz yere
/// ödeme ertelerdi.
/// </summary>
[Collection("Integration")]
public sealed class TaxCashFlowTests(DatabaseFixture fixture)
{
    private sealed record TestContext(Guid CompanyId);

    private static readonly (string Code, string Name, AccountingAccountNature Nature)[]
        Accounts =
        [
            ("191.01.03", "% 20 İNDİRİLECEK KDV", AccountingAccountNature.Debit),
            ("391.09", "% 20 HESAPLANAN KDV", AccountingAccountNature.Credit),
            ("360.99", "ÖDENECEK KDV", AccountingAccountNature.Credit),
            ("190.01", "DEVREDEN KDV", AccountingAccountNature.Debit),
            ("600.03", "YURTİÇİ SATIŞLAR", AccountingAccountNature.Credit),
            ("120", "ALICILAR", AccountingAccountNature.Debit),
            ("102", "BANKALAR", AccountingAccountNature.Debit)
        ];

    private async Task<TestContext> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        foreach (var (code, name, nature) in Accounts)
        {
            db.AccountingAccounts.Add(new AccountingAccount
            {
                CompanyId = project.CompanyId,
                Code = code,
                Name = name,
                Nature = nature,
                Level = 4,
                IsPostingAllowed = true
            });
        }

        await db.SaveChangesAsync();

        return new TestContext(project.CompanyId);
    }

    /// <summary>
    /// Geçen ayın KDV'sini üretir: ödeme tarihi bu ayın 26'sı olur ve
    /// 30/60/90 gün penceresine düşer.
    /// </summary>
    private async Task<(int Year, int Month, decimal Vat)> PostLastMonthVatAsync(
        TestContext context, decimal vatAmount)
    {
        var lastMonth = DateTime.UtcNow.Date.AddMonths(-1);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var accountIds = await db.AccountingAccounts
            .Where(x => x.CompanyId == context.CompanyId)
            .ToDictionaryAsync(x => x.Code, x => x.Id);

        var voucher = new AccountingVoucher
        {
            CompanyId = context.CompanyId,
            VoucherNumber = $"TST-{Guid.NewGuid():N}"[..14],
            VoucherType = AccountingVoucherType.Journal,
            Status = AccountingVoucherStatus.Posted,
            VoucherDate = DateTime.SpecifyKind(
                new DateTime(lastMonth.Year, lastMonth.Month, 15), DateTimeKind.Utc),
            FiscalYear = lastMonth.Year,
            FiscalPeriod = lastMonth.Month,
            CurrencyCode = "TRY",
            ExchangeRate = 1m,
            SourceModule = "Test",
            TotalDebit = vatAmount * 6,
            TotalCredit = vatAmount * 6,
            PostedAtUtc = DateTime.UtcNow
        };

        voucher.Lines.Add(new AccountingVoucherLine
        {
            LineNumber = 1,
            AccountingAccountId = accountIds["120"],
            Description = "Satış",
            DebitAmount = vatAmount * 6,
            DebitAmountLocal = vatAmount * 6,
            CurrencyCode = "TRY",
            ExchangeRate = 1m
        });

        voucher.Lines.Add(new AccountingVoucherLine
        {
            LineNumber = 2,
            AccountingAccountId = accountIds["600.03"],
            Description = "Satış geliri",
            CreditAmount = vatAmount * 5,
            CreditAmountLocal = vatAmount * 5,
            CurrencyCode = "TRY",
            ExchangeRate = 1m
        });

        voucher.Lines.Add(new AccountingVoucherLine
        {
            LineNumber = 3,
            AccountingAccountId = accountIds["391.09"],
            Description = "Hesaplanan KDV",
            CreditAmount = vatAmount,
            CreditAmountLocal = vatAmount,
            CurrencyCode = "TRY",
            ExchangeRate = 1m
        });

        db.AccountingVouchers.Add(voucher);
        await db.SaveChangesAsync();

        return (lastMonth.Year, lastMonth.Month, vatAmount);
    }

    /// <summary>
    /// Ödenmemiş KDV nakit akışta çıkış olarak görünür ve tahmini
    /// olduğu adında yazar.
    /// </summary>
    [Fact]
    public async Task CashFlow_IncludesUnpaidVatAsEstimatedOutflow()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var (_, _, vat) = await PostLastMonthVatAsync(context, 40_000m);

        var cashFlow = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cash-flow?companyId={context.CompanyId}");

        var taxItems = cashFlow.GetProperty("outflows").EnumerateArray()
            .Where(x => x.GetProperty("kind").GetString()!.StartsWith("Tax"))
            .ToList();

        Assert.Single(taxItems);
        Assert.Equal(vat, taxItems[0].GetProperty("amount").GetDecimal());
        Assert.Contains("tahmini", taxItems[0].GetProperty("kindName").GetString());
        Assert.Equal("TaxVat", taxItems[0].GetProperty("kind").GetString());
    }

    /// <summary>
    /// Ödendi işaretlenen dönem nakit akıştan düşer; düşmeseydi ödenmiş
    /// vergi listede durmaya devam ederdi.
    /// </summary>
    [Fact]
    public async Task CashFlow_ExcludesPaidTaxPeriods()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var (year, month, _) = await PostLastMonthVatAsync(context, 25_000m);

        var mark = await client.PostAsJsonAsync("/api/tax/payments", new
        {
            companyId = context.CompanyId,
            kind = (int)TaxObligationKind.Vat,
            periodYear = year,
            periodNumber = month
        });

        Assert.Equal(HttpStatusCode.OK, mark.StatusCode);

        var payment = await mark.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payment.GetProperty("isPaid").GetBoolean());
        Assert.Equal(25_000m, payment.GetProperty("paidAmount").GetDecimal());

        var cashFlow = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cash-flow?companyId={context.CompanyId}");

        Assert.Empty(cashFlow.GetProperty("outflows").EnumerateArray()
            .Where(x => x.GetProperty("kind").GetString()!.StartsWith("Tax")));
    }

    /// <summary>
    /// Takvim ödenmiş dönemi de gösterir (işaretli olarak); nakit akış
    /// göstermez. İki görünümün işi farklı.
    /// </summary>
    [Fact]
    public async Task Calendar_ShowsPaidPeriodsAsMarked()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var (year, month, _) = await PostLastMonthVatAsync(context, 12_000m);

        await client.PostAsJsonAsync("/api/tax/payments", new
        {
            companyId = context.CompanyId,
            kind = (int)TaxObligationKind.Vat,
            periodYear = year,
            periodNumber = month,
            amount = 11_500m,
            note = "Erken ödendi"
        });

        var calendar = await client.GetFromJsonAsync<JsonElement>(
            $"/api/tax/calendar?companyId={context.CompanyId}");

        var vatRow = calendar.EnumerateArray()
            .Single(x => x.GetProperty("kind").GetInt32() == (int)TaxObligationKind.Vat &&
                         x.GetProperty("periodNumber").GetInt32() == month);

        Assert.True(vatRow.GetProperty("isPaid").GetBoolean());
        Assert.Equal(12_000m, vatRow.GetProperty("estimatedAmount").GetDecimal());
        Assert.Equal(11_500m, vatRow.GetProperty("paidAmount").GetDecimal());
        Assert.False(vatRow.GetProperty("isOverdue").GetBoolean());
    }

    /// <summary>Aynı dönem iki kez ödendi işaretlenemez.</summary>
    [Fact]
    public async Task MarkPaid_Twice_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var (year, month, _) = await PostLastMonthVatAsync(context, 5_000m);

        var body = new
        {
            companyId = context.CompanyId,
            kind = (int)TaxObligationKind.Vat,
            periodYear = year,
            periodNumber = month
        };

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/tax/payments", body)).StatusCode);

        var second = await client.PostAsJsonAsync("/api/tax/payments", body);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("zaten ödendi", await second.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Yanlış işaretlenen ödeme geri alınabilir ve dönem nakit akışa
    /// geri döner.
    /// </summary>
    [Fact]
    public async Task UndoPayment_RestoresObligationToCashFlow()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var (year, month, vat) = await PostLastMonthVatAsync(context, 18_000m);

        await client.PostAsJsonAsync("/api/tax/payments", new
        {
            companyId = context.CompanyId,
            kind = (int)TaxObligationKind.Vat,
            periodYear = year,
            periodNumber = month
        });

        var undo = await client.DeleteAsync(
            $"/api/tax/payments?companyId={context.CompanyId}" +
            $"&kind={(int)TaxObligationKind.Vat}&periodYear={year}&periodNumber={month}");

        Assert.Equal(HttpStatusCode.NoContent, undo.StatusCode);

        var cashFlow = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cash-flow?companyId={context.CompanyId}");

        var taxItems = cashFlow.GetProperty("outflows").EnumerateArray()
            .Where(x => x.GetProperty("kind").GetString()!.StartsWith("Tax"))
            .ToList();

        Assert.Single(taxItems);
        Assert.Equal(vat, taxItems[0].GetProperty("amount").GetDecimal());
    }

    /// <summary>
    /// Proje filtresi verildiğinde vergi gösterilmez: vergi şirket
    /// düzeyinde bir yükümlülüktür, tek projeye pay edilmesi yanıltıcı
    /// olurdu.
    /// </summary>
    [Fact]
    public async Task CashFlow_WithProjectFilter_ExcludesTaxItems()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostLastMonthVatAsync(context, 30_000m);

        Guid projectId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            projectId = await db.Projects
                .Where(x => x.CompanyId == context.CompanyId)
                .Select(x => x.Id)
                .FirstAsync();
        }

        var cashFlow = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cash-flow?companyId={context.CompanyId}&projectId={projectId}");

        Assert.Empty(cashFlow.GetProperty("outflows").EnumerateArray()
            .Where(x => x.GetProperty("kind").GetString()!.StartsWith("Tax")));
    }

    /// <summary>
    /// Vergi çıkışı 30/60/90 gün kovalarına da girmeli: "önümüzdeki 30
    /// gün" görünümü çek vadeleriyle birlikte TAM resmi vermeli.
    /// </summary>
    [Fact]
    public async Task CashFlow_TaxOutflow_EntersBuckets()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var (year, month, vat) = await PostLastMonthVatAsync(context, 50_000m);

        var dueDate = TaxCalendar.MonthlyDueDate(year, month);
        var daysToDue = (dueDate.Date - DateTime.UtcNow.Date).TotalDays;

        var cashFlow = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cash-flow?companyId={context.CompanyId}");

        var buckets = cashFlow.GetProperty("buckets").EnumerateArray().ToList();

        // Ödeme günü henüz gelmemişse ilgili kovada görünmeli; geçmişse
        // "gecikmiş" toplamında.
        if (daysToDue >= 0)
        {
            var bucket = buckets.First(x =>
                x.GetProperty("days").GetInt32() >= daysToDue);

            Assert.True(bucket.GetProperty("outflowAmount").GetDecimal() >= vat);
        }
        else
        {
            Assert.True(cashFlow.GetProperty("overdueOutflowAmount").GetDecimal() >= vat);
        }
    }
}
