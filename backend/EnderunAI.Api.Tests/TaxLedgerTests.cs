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
/// Vergi yükü yönetim görünümü: KDV netleştirme, devreden zinciri,
/// bordro yükü ve geçici vergi tahmini.
///
/// Kritik nokta devreden zinciri: bir ayın devredeni sonraki ayın
/// indirilecek tarafına eklenmezse devreden KDV kaybolur ve ödenecek
/// KDV olduğundan yüksek çıkar.
/// </summary>
[Collection("Integration")]
public sealed class TaxLedgerTests(DatabaseFixture fixture)
{
    private sealed record TestContext(Guid CompanyId, Dictionary<string, Guid> Accounts);

    private static readonly (string Code, string Name, AccountingAccountNature Nature)[]
        RequiredAccounts =
        [
            ("191.01.03", "% 20 İNDİRİLECEK KDV", AccountingAccountNature.Debit),
            ("191.05", "SORUMLU SIFATIYLA BEYAN EDİLEN KDV", AccountingAccountNature.Debit),
            ("391.09", "% 20 HESAPLANAN KDV", AccountingAccountNature.Credit),
            ("360.002", "SORUMLU SIFATIYLA ÖDENECEK KDV", AccountingAccountNature.Credit),
            ("360.99", "ÖDENECEK KDV", AccountingAccountNature.Credit),
            ("190.01", "DEVREDEN KDV", AccountingAccountNature.Debit),
            ("600.03", "YURTİÇİ SATIŞLAR", AccountingAccountNature.Credit),
            ("740", "HİZMET ÜRETİM MALİYETİ", AccountingAccountNature.Debit),
            ("741.01", "HİZMET ÜRETİM MALİYETİ YANSITMA", AccountingAccountNature.Credit),
            ("622", "SATILAN HİZMET MALİYETİ", AccountingAccountNature.Debit),
            ("120", "ALICILAR", AccountingAccountNature.Debit),
            ("320", "SATICILAR", AccountingAccountNature.Credit)
        ];

    private async Task<TestContext> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var accounts = new Dictionary<string, Guid>();

        foreach (var (code, name, nature) in RequiredAccounts)
        {
            var account = new AccountingAccount
            {
                CompanyId = project.CompanyId,
                Code = code,
                Name = name,
                Nature = nature,
                Level = 4,
                IsPostingAllowed = true
            };

            db.AccountingAccounts.Add(account);
            accounts[code] = account.Id;
        }

        // Kurumlar vergisi oranı artık yıl bazlı ve varsayılanı yok:
        // tanımlanmadan geçici vergi tahmini üretilmiyor.
        db.CompanyCorporateTaxRates.Add(new CompanyCorporateTaxRate
        {
            CompanyId = project.CompanyId,
            Year = 2026,
            Rate = 25m
        });

        await db.SaveChangesAsync();

        return new TestContext(project.CompanyId, accounts);
    }

    /// <summary>
    /// Kesinleşmiş fiş üretir. Vergi görünümü yalnızca Posted fişleri
    /// okur — taslak fiş beyana da girmez.
    /// </summary>
    private async Task PostVoucherAsync(
        TestContext context,
        DateTime date,
        (string Code, decimal Debit, decimal Credit)[] lines,
        string sourceModule = "Test",
        string? reference = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var voucher = new AccountingVoucher
        {
            CompanyId = context.CompanyId,
            VoucherNumber = $"TST-{Guid.NewGuid():N}"[..14],
            VoucherType = AccountingVoucherType.Journal,
            Status = AccountingVoucherStatus.Posted,
            VoucherDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
            FiscalYear = date.Year,
            FiscalPeriod = date.Month,
            CurrencyCode = "TRY",
            ExchangeRate = 1m,
            SourceModule = sourceModule,
            ReferenceNumber = reference,
            TotalDebit = lines.Sum(x => x.Debit),
            TotalCredit = lines.Sum(x => x.Credit),
            PostedAtUtc = DateTime.UtcNow
        };

        var lineNumber = 1;

        foreach (var (code, debit, credit) in lines)
        {
            voucher.Lines.Add(new AccountingVoucherLine
            {
                LineNumber = lineNumber++,
                AccountingAccountId = context.Accounts[code],
                Description = "Test satırı",
                DebitAmount = debit,
                CreditAmount = credit,
                DebitAmountLocal = debit,
                CreditAmountLocal = credit,
                CurrencyCode = "TRY",
                ExchangeRate = 1m
            });
        }

        db.AccountingVouchers.Add(voucher);
        await db.SaveChangesAsync();
    }

    private async Task<TaxOverview> GetOverviewAsync(TestContext context, int year)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITaxLedgerService>();

        return await service.GetOverviewAsync(context.CompanyId, year, CancellationToken.None);
    }

    /// <summary>
    /// Hesaplanan KDV indirilecekten büyükse fark ödenecek KDV'dir.
    /// </summary>
    [Fact]
    public async Task Vat_WhenOutputExceedsInput_ProducesPayable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        // Mart: 100.000 satış KDV'si, 30.000 alış KDV'si.
        await PostVoucherAsync(context, new DateTime(2026, 3, 10),
            [("120", 600_000m, 0m), ("600.03", 0m, 500_000m), ("391.09", 0m, 100_000m)]);

        await PostVoucherAsync(context, new DateTime(2026, 3, 15),
            [("740", 150_000m, 0m), ("191.01.03", 30_000m, 0m), ("320", 0m, 180_000m)]);

        var overview = await GetOverviewAsync(context, 2026);
        var march = overview.Vat.Single(x => x.Month == 3);

        Assert.Equal(100_000m, march.OutputVat);
        Assert.Equal(30_000m, march.InputVat);
        Assert.Equal(0m, march.CarryForwardIn);
        Assert.Equal(70_000m, march.PayableVat);
        Assert.Equal(0m, march.CarryForwardOut);
        Assert.False(march.IsAccrued);
    }

    /// <summary>
    /// İndirilecek KDV fazlaysa fark sonraki aya DEVREDER ve o ayın
    /// ödenecek tutarını azaltır. Zincir kurulmasaydı devreden kaybolur,
    /// nisanda 40.000 ödenecek görünürdü.
    /// </summary>
    [Fact]
    public async Task Vat_CarryForwardChain_ReducesNextMonthPayable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        // Mart: 10.000 hesaplanan, 60.000 indirilecek → 50.000 devreden.
        await PostVoucherAsync(context, new DateTime(2026, 3, 10),
            [("120", 60_000m, 0m), ("600.03", 0m, 50_000m), ("391.09", 0m, 10_000m)]);
        await PostVoucherAsync(context, new DateTime(2026, 3, 20),
            [("740", 300_000m, 0m), ("191.01.03", 60_000m, 0m), ("320", 0m, 360_000m)]);

        // Nisan: 40.000 hesaplanan, indirilecek yok.
        await PostVoucherAsync(context, new DateTime(2026, 4, 12),
            [("120", 240_000m, 0m), ("600.03", 0m, 200_000m), ("391.09", 0m, 40_000m)]);

        var overview = await GetOverviewAsync(context, 2026);

        var march = overview.Vat.Single(x => x.Month == 3);
        var april = overview.Vat.Single(x => x.Month == 4);
        var may = overview.Vat.Single(x => x.Month == 5);

        Assert.Equal(0m, march.PayableVat);
        Assert.Equal(50_000m, march.CarryForwardOut);

        // Nisan: 40.000 − 50.000 devreden = ödenecek yok, 10.000 devreder.
        Assert.Equal(50_000m, april.CarryForwardIn);
        Assert.Equal(0m, april.PayableVat);
        Assert.Equal(10_000m, april.CarryForwardOut);

        // Mayıs hiç hareket yok ama devreden taşınmaya devam eder.
        Assert.Equal(10_000m, may.CarryForwardIn);
        Assert.Equal(10_000m, may.CarryForwardOut);
    }

    /// <summary>
    /// İade faturası 391'i borçlandırır ve hesaplanan KDV'yi azaltır —
    /// yalnız alacak toplansaydı iade edilen satışın KDV'si beyanda
    /// durmaya devam ederdi.
    /// </summary>
    [Fact]
    public async Task Vat_SalesReturn_ReducesOutputVat()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await PostVoucherAsync(context, new DateTime(2026, 6, 5),
            [("120", 120_000m, 0m), ("600.03", 0m, 100_000m), ("391.09", 0m, 20_000m)]);

        // İade: satırlar ters yönde.
        await PostVoucherAsync(context, new DateTime(2026, 6, 20),
            [("600.03", 25_000m, 0m), ("391.09", 5_000m, 0m), ("120", 0m, 30_000m)]);

        var overview = await GetOverviewAsync(context, 2026);
        var june = overview.Vat.Single(x => x.Month == 6);

        Assert.Equal(15_000m, june.OutputVat);
        Assert.Equal(15_000m, june.PayableVat);
    }

    /// <summary>
    /// Sorumlu sıfatıyla KDV ayrı satırda görünür: indirilecek KDV'nin
    /// içinde yer alsa da vergi dairesine AYRI ödenir.
    /// </summary>
    [Fact]
    public async Task Vat_ReverseCharge_IsReportedSeparately()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await PostVoucherAsync(context, new DateTime(2026, 7, 8),
            [
                ("740", 100_000m, 0m),
                ("191.01.03", 12_000m, 0m),
                ("191.05", 8_000m, 0m),
                ("360.002", 0m, 8_000m),
                ("320", 0m, 112_000m)
            ]);

        var overview = await GetOverviewAsync(context, 2026);
        var july = overview.Vat.Single(x => x.Month == 7);

        Assert.Equal(20_000m, july.InputVat);
        Assert.Equal(8_000m, july.ReverseChargeVat);
    }

    /// <summary>
    /// Taslak fiş vergi görünümüne girmez: muhasebeleşmemiş belge
    /// beyana da girmez.
    /// </summary>
    [Fact]
    public async Task Vat_IgnoresDraftVouchers()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var voucher = new AccountingVoucher
            {
                CompanyId = context.CompanyId,
                VoucherNumber = $"TSL-{Guid.NewGuid():N}"[..14],
                VoucherType = AccountingVoucherType.Journal,
                Status = AccountingVoucherStatus.Draft,
                VoucherDate = DateTime.SpecifyKind(new DateTime(2026, 8, 3), DateTimeKind.Utc),
                FiscalYear = 2026,
                FiscalPeriod = 8,
                CurrencyCode = "TRY",
                ExchangeRate = 1m,
                TotalDebit = 50_000m,
                TotalCredit = 50_000m
            };

            voucher.Lines.Add(new AccountingVoucherLine
            {
                LineNumber = 1,
                AccountingAccountId = context.Accounts["391.09"],
                Description = "Taslak",
                DebitAmount = 0m,
                CreditAmount = 50_000m,
                CreditAmountLocal = 50_000m,
                CurrencyCode = "TRY",
                ExchangeRate = 1m
            });

            db.AccountingVouchers.Add(voucher);
            await db.SaveChangesAsync();
        }

        var overview = await GetOverviewAsync(context, 2026);

        Assert.Equal(0m, overview.Vat.Single(x => x.Month == 8).OutputVat);
    }

    /// <summary>
    /// 7/A sistemi kullanılan şirkette gider 62x'ten okunur.
    ///
    /// Bu testin sınadığı tuzak şu: maliyet önce 740'a girer, sonra
    /// yansıtmayla 622'ye aktarılır ve iki hesap bir süre AYNI ANDA dolu
    /// durur. İkisi toplansaydı aynı maliyet iki kez sayılır ve kâr
    /// olduğundan düşük görünürdü.
    /// </summary>
    [Fact]
    public async Task AdvanceTax_WithReflectionSystem_CountsCostOnce()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        // 1. dönem: 500.000 gelir.
        await PostVoucherAsync(context, new DateTime(2026, 2, 10),
            [("120", 600_000m, 0m), ("600.03", 0m, 500_000m), ("391.09", 0m, 100_000m)]);

        // Maliyet 740'a girer.
        await PostVoucherAsync(context, new DateTime(2026, 2, 15),
            [("740", 300_000m, 0m), ("320", 0m, 300_000m)]);

        // Yansıtma: 622 borç / 741 alacak. Artık 740 ve 622 aynı anda dolu.
        await PostVoucherAsync(context, new DateTime(2026, 3, 31),
            [("622", 300_000m, 0m), ("741.01", 0m, 300_000m)]);

        var overview = await GetOverviewAsync(context, 2026);
        var first = overview.AdvanceTax.Single(x => x.Quarter == 1);

        Assert.Equal(500_000m, first.Revenue);
        Assert.Equal(300_000m, first.Expense);
        Assert.Equal(200_000m, first.ProfitBeforeTax);
        Assert.Equal(25m, first.TaxRate);
        Assert.Equal(50_000m, first.EstimatedTax);

        // 1. dönem ödemesi 17 Mayıs.
        Assert.Equal(new DateTime(2026, 5, 17), first.DueDate.Date);
    }

    /// <summary>
    /// Yansıtma kullanmayan şirkette gider doğrudan 7'li hesaplardan
    /// okunur; yoksa gider hiç görünmez ve kâr gelire eşit çıkardı.
    /// </summary>
    [Fact]
    public async Task AdvanceTax_WithoutReflectionSystem_UsesSevenSeriesAccounts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await PostVoucherAsync(context, new DateTime(2026, 2, 10),
            [("120", 240_000m, 0m), ("600.03", 0m, 200_000m), ("391.09", 0m, 40_000m)]);
        await PostVoucherAsync(context, new DateTime(2026, 2, 20),
            [("740", 120_000m, 0m), ("320", 0m, 120_000m)]);

        var overview = await GetOverviewAsync(context, 2026);
        var first = overview.AdvanceTax.Single(x => x.Quarter == 1);

        Assert.Equal(200_000m, first.Revenue);
        Assert.Equal(120_000m, first.Expense);
        Assert.Equal(80_000m, first.ProfitBeforeTax);
        Assert.Equal(20_000m, first.EstimatedTax);
    }

    /// <summary>Zararda geçici vergi sıfırdır, eksi vergi üretilmez.</summary>
    [Fact]
    public async Task AdvanceTax_WhenLoss_IsZero()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await PostVoucherAsync(context, new DateTime(2026, 2, 10),
            [("740", 400_000m, 0m), ("320", 0m, 400_000m)]);

        var overview = await GetOverviewAsync(context, 2026);
        var first = overview.AdvanceTax.Single(x => x.Quarter == 1);

        Assert.Equal(-400_000m, first.ProfitBeforeTax);
        Assert.Equal(0m, first.EstimatedTax);
    }

    /// <summary>Vergi takvimi tarihleri tek yerden gelir.</summary>
    [Theory]
    [InlineData(2026, 3, 2026, 4, 26)]
    [InlineData(2026, 12, 2027, 1, 26)]
    public void Calendar_MonthlyDueDate_IsFollowingMonth(
        int year, int month, int expectedYear, int expectedMonth, int expectedDay)
    {
        var due = TaxCalendar.MonthlyDueDate(year, month);

        Assert.Equal(expectedYear, due.Year);
        Assert.Equal(expectedMonth, due.Month);
        Assert.Equal(expectedDay, due.Day);
    }

    [Theory]
    [InlineData(1, 2026, 5)]
    [InlineData(2, 2026, 8)]
    [InlineData(3, 2026, 11)]
    [InlineData(4, 2027, 2)]
    public void Calendar_AdvanceTaxDueDate_IsSecondMonthAfterPeriod(
        int quarter, int expectedYear, int expectedMonth)
    {
        var due = TaxCalendar.AdvanceTaxDueDate(2026, quarter);

        Assert.Equal(expectedYear, due.Year);
        Assert.Equal(expectedMonth, due.Month);
        Assert.Equal(17, due.Day);
    }

    /// <summary>
    /// Uç, tahminlerin varsayımlarını da döndürmeli: ekran onları
    /// gizlemeden gösterebilsin diye.
    /// </summary>
    [Fact]
    public async Task Endpoint_ReturnsAssumptions()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var overview = await client.GetFromJsonAsync<JsonElement>(
            $"/api/tax/overview?companyId={context.CompanyId}&year=2026");

        var assumptions = overview.GetProperty("assumptions").EnumerateArray()
            .Select(x => x.GetString())
            .ToList();

        Assert.Contains(assumptions, x => x is not null && x.Contains("beyanname üretmez"));
        Assert.Contains(assumptions, x => x is not null && x.Contains("müşavir"));
        Assert.Equal(12, overview.GetProperty("vat").GetArrayLength());
        Assert.Equal(4, overview.GetProperty("advanceTax").GetArrayLength());
    }
}
