using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Faz E4: bordro tahakkuk ve ödeme muhasebesi. Fişlerin dengeli
/// olması ve 335 Personele Borçlar bakiyesinin ödeme sonrası kapanması
/// bu fazın asıl güvencesi.
/// </summary>
[Collection("Integration")]
public sealed class PayrollAccountingTests(DatabaseFixture fixture)
{
    private const int Year = 2026;
    private const int Month = 7;

    private sealed record Context(Guid CompanyId, Guid PersonnelId, Guid BankAccountId);

    private static async Task SeedChartOfAccountsAsync(AppDbContext db, Guid companyId)
    {
        db.AccountingAccounts.AddRange(
            new AccountingAccount
            {
                CompanyId = companyId, Code = "102", Name = "Bankalar",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "770", Name = "Genel Yönetim Giderleri",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "335", Name = "Personele Borçlar",
                Nature = AccountingAccountNature.Credit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "360", Name = "Ödenecek Vergi ve Fonlar",
                Nature = AccountingAccountNature.Credit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "361", Name = "Ödenecek Sosyal Güvenlik Kesintileri",
                Nature = AccountingAccountNature.Credit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "195", Name = "İş Avansları",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            });

        await db.SaveChangesAsync();
    }

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);
        await SeedChartOfAccountsAsync(db, company.Id);

        db.CompanyPayrollSettings.Add(new CompanyPayrollSettings
        {
            CompanyId = company.Id,
            Year = Year,
            MinimumWageGross = 33_030m,
            MinimumWageNet = 28_075.50m,
            SgkBaseFloor = 33_030m,
            SgkBaseCeiling = 247_725m,
            VerifiedAtUtc = DateTime.UtcNow,
            TaxBrackets = new List<PayrollTaxBracket>
            {
                new() { Order = 1, LowerBound = 0m, UpperBound = 200_000m, Rate = 15m },
                new() { Order = 2, LowerBound = 200_000m, UpperBound = null, Rate = 20m }
            }
        });

        var personnel = new Personnel
        {
            CompanyId = company.Id,
            EmployeeNumber = $"PRS-{suffix}",
            FirstName = "Bordro",
            LastName = "Test",
            Status = PersonnelStatus.Active
        };
        db.Personnel.Add(personnel);

        var bankAccountingId = await db.AccountingAccounts
            .Where(x => x.CompanyId == company.Id && x.Code == "102")
            .Select(x => x.Id)
            .SingleAsync();

        var bank = new CashAccount
        {
            CompanyId = company.Id,
            Type = CashAccountType.Bank,
            Code = $"BNK-{suffix}",
            Name = $"Test Banka {suffix}",
            CurrencyCode = "TRY",
            AccountingAccountId = bankAccountingId
        };
        db.CashAccounts.Add(bank);

        await db.SaveChangesAsync();

        hrDb.SalaryDefinitions.Add(new HrSalaryDefinition
        {
            CompanyId = company.Id,
            PersonnelId = personnel.Id,
            EffectiveStartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            GrossSalary = 60_000m,
            NetSalary = 47_356.63m,
            DailyRate = 2_000m,
            HourlyRate = 266.67m,
            CurrencyCode = "TRY"
        });
        await hrDb.SaveChangesAsync();

        return new Context(company.Id, personnel.Id, bank.Id);
    }

    /// <summary>Bordroyu hesaplayıp onaylar; dönem muhasebeleştirmeye hazır olur.</summary>
    private async Task<HttpClient> PrepareApprovedPayrollAsync(Context context)
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var calculate = await client.PostAsJsonAsync(
            "/api/hr/payroll/records/calculate-company",
            new { companyId = context.CompanyId, year = Year, month = Month, recalculateExisting = true });

        Assert.Equal(HttpStatusCode.OK, calculate.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var record = await hrDb.PayrollRecords.SingleAsync(
            x => x.CompanyId == context.CompanyId && x.Month == Month);

        var approve = await client.PostAsync(
            $"/api/hr/payroll/records/{record.Id}/approve", null);

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        return client;
    }

    [Fact]
    public async Task PostPeriod_CreatesBalancedAccrualVoucher()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await PrepareApprovedPayrollAsync(context);

        var response = await client.PostAsJsonAsync(
            "/api/hr/payroll/periods/post",
            new { companyId = context.CompanyId, year = Year, month = Month });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        var voucherId = payload.GetProperty("accountingVoucherId").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var voucher = await db.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.Id == voucherId);

        Assert.Equal(AccountingVoucherStatus.Posted, voucher.Status);
        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);

        // 60.000 brüt + işveren payı (20,75% + 2% = 13.650) = 73.650
        var expense = voucher.Lines.Single(x => x.AccountingAccount.Code == "770");
        Assert.Equal(73_650m, expense.DebitAmount);

        var payable = voucher.Lines.Single(x => x.AccountingAccount.Code == "335");
        Assert.Equal(47_356.63m, payable.CreditAmount);

        // Gelir vergisi 3.438,67 + damga 204,70
        var tax = voucher.Lines.Single(x => x.AccountingAccount.Code == "360");
        Assert.Equal(3_643.37m, tax.CreditAmount);

        // İşçi 8.400 + 600 + işveren 12.450 + 1.200
        var sgk = voucher.Lines.Single(x => x.AccountingAccount.Code == "361");
        Assert.Equal(22_650m, sgk.CreditAmount);

        Assert.Equal(73_650m, voucher.TotalDebit);
    }

    [Fact]
    public async Task PostPeriod_IsRejectedTwiceForSamePeriod()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await PrepareApprovedPayrollAsync(context);

        var body = new { companyId = context.CompanyId, year = Year, month = Month };

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/hr/payroll/periods/post", body)).StatusCode);

        var second = await client.PostAsJsonAsync("/api/hr/payroll/periods/post", body);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains(
            "zaten",
            (await second.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task PayPeriod_ClosesPayableAndProducesCashOutflow()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await PrepareApprovedPayrollAsync(context);

        await client.PostAsJsonAsync("/api/hr/payroll/periods/post",
            new { companyId = context.CompanyId, year = Year, month = Month });

        var response = await client.PostAsJsonAsync("/api/hr/payroll/periods/pay", new
        {
            companyId = context.CompanyId,
            year = Year,
            month = Month,
            cashAccountId = context.BankAccountId,
            paymentDate = new DateTime(Year, Month, 28, 0, 0, 0, DateTimeKind.Utc),
            paymentReference = "Toplu havale"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(47_356.63m, payload.GetProperty("paidAmount").GetDecimal());

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var paymentVoucher = await db.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.Id == payload.GetProperty("accountingVoucherId").GetGuid());

        Assert.Equal(AccountingVoucherStatus.Posted, paymentVoucher.Status);
        Assert.Equal(paymentVoucher.TotalDebit, paymentVoucher.TotalCredit);

        // 335 borçlanır, banka alacaklanır.
        Assert.Contains(paymentVoucher.Lines,
            x => x.AccountingAccount.Code == "335" && x.DebitAmount == 47_356.63m);
        Assert.Contains(paymentVoucher.Lines,
            x => x.AccountingAccount.Code == "102" && x.CreditAmount == 47_356.63m);

        // 335 bakiyesi kapanmalı: tahakkukta alacak, ödemede borç.
        var payableLines = await db.AccountingVoucherLines
            .Where(x => x.AccountingAccount.CompanyId == context.CompanyId &&
                        x.AccountingAccount.Code == "335" &&
                        x.AccountingVoucher.Status == AccountingVoucherStatus.Posted)
            .ToListAsync();

        Assert.Equal(
            payableLines.Sum(x => x.CreditAmount),
            payableLines.Sum(x => x.DebitAmount));

        // Kasa hareketi aynı fişe bağlı, tek fiş üretilmiş.
        var cashTransaction = await db.CashTransactions.SingleAsync(
            x => x.SourceModule == "PayrollPayment" &&
                 x.CashAccountId == context.BankAccountId);

        Assert.Equal(CashTransactionDirection.Out, cashTransaction.Direction);
        Assert.Equal(47_356.63m, cashTransaction.Amount);
        Assert.Equal(paymentVoucher.Id, cashTransaction.AccountingVoucherId);

        // Bordro ödendi işaretlendi.
        var record = await hrDb.PayrollRecords.SingleAsync(
            x => x.CompanyId == context.CompanyId && x.Month == Month);
        Assert.Equal(PayrollStatus.Paid, record.Status);
        Assert.Equal("Toplu havale", record.PaymentReference);
    }

    [Fact]
    public async Task PayPeriod_IsRejectedBeforeAccrual()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await PrepareApprovedPayrollAsync(context);

        var response = await client.PostAsJsonAsync("/api/hr/payroll/periods/pay", new
        {
            companyId = context.CompanyId,
            year = Year,
            month = Month,
            cashAccountId = context.BankAccountId,
            paymentDate = DateTime.UtcNow.Date,
            paymentReference = (string?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "muhasebeleştirilmelidir",
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task PostPeriod_IsRejectedWhenNothingApproved()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // Hesaplanmış ama onaylanmamış bordro.
        await client.PostAsJsonAsync(
            "/api/hr/payroll/records/calculate-company",
            new { companyId = context.CompanyId, year = Year, month = Month, recalculateExisting = true });

        var response = await client.PostAsJsonAsync("/api/hr/payroll/periods/post",
            new { companyId = context.CompanyId, year = Year, month = Month });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "onaylanmış bordro yok",
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("message").GetString()!);
    }
}
