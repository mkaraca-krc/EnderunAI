using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Faz C: cari bakiye ve ekstre — paralel bir hareket defteri yok,
/// tek gerçek kaynak muhasebe defteri (yalnızca kesinleşmiş fişler).
/// </summary>
[Collection("Integration")]
public sealed class CurrentAccountStatementTests(DatabaseFixture fixture)
{
    private async Task<(Guid CompanyId, Guid CurrentAccountId, Guid ProjectId)> CreateContextAsync(
        string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var currentAccountId = project.EmployerCurrentAccountId!.Value;

        db.AccountingAccounts.AddRange(
            new AccountingAccount
            {
                CompanyId = project.CompanyId, Code = "120", Name = "Alıcılar",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = project.CompanyId, Code = "600.03", Name = "Satışlar",
                Nature = AccountingAccountNature.Credit, Level = 4, IsPostingAllowed = true
            });
        await db.SaveChangesAsync();

        return (project.CompanyId, currentAccountId, project.Id);
    }

    /// <summary>
    /// Belirtilen tarihte, cari boyutu dolu tek satırlık dengeli bir fiş
    /// oluşturur ve kesinleştirir (120 borç / 600 alacak).
    /// </summary>
    private async Task PostVoucherAsync(
        Guid companyId, Guid currentAccountId, Guid projectId,
        DateTime date, decimal amount, AccountingVoucherStatus status = AccountingVoucherStatus.Posted)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var receivable = await db.AccountingAccounts
            .Where(x => x.CompanyId == companyId && x.Code == "120").Select(x => x.Id).SingleAsync();
        var sales = await db.AccountingAccounts
            .Where(x => x.CompanyId == companyId && x.Code == "600.03").Select(x => x.Id).SingleAsync();

        db.AccountingVouchers.Add(new AccountingVoucher
        {
            CompanyId = companyId,
            VoucherNumber = $"T-{Guid.NewGuid():N}"[..14],
            VoucherType = AccountingVoucherType.Journal,
            Status = status,
            VoucherDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
            FiscalYear = date.Year,
            FiscalPeriod = date.Month,
            CurrencyCode = "TRY",
            ExchangeRate = 1m,
            TotalDebit = amount,
            TotalCredit = amount,
            PostedAtUtc = status == AccountingVoucherStatus.Posted ? DateTime.UtcNow : null,
            Lines =
            [
                new AccountingVoucherLine
                {
                    LineNumber = 1, AccountingAccountId = receivable,
                    DebitAmount = amount, DebitAmountLocal = amount,
                    CreditAmount = 0m, CreditAmountLocal = 0m,
                    CurrencyCode = "TRY", ExchangeRate = 1m,
                    CurrentAccountId = currentAccountId, ProjectId = projectId
                },
                new AccountingVoucherLine
                {
                    LineNumber = 2, AccountingAccountId = sales,
                    DebitAmount = 0m, DebitAmountLocal = 0m,
                    CreditAmount = amount, CreditAmountLocal = amount,
                    CurrencyCode = "TRY", ExchangeRate = 1m,
                    ProjectId = projectId
                }
            ]
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Statement_ComputesRunningBalanceFromLedger()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 3, 10), 1_000m);
        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 3, 20), 500m);

        var statement = await client.GetFromJsonAsync<JsonElement>(
            $"/api/current-accounts/{currentAccountId}/statement");

        Assert.Equal(2, statement.GetProperty("lineCount").GetInt32());
        Assert.Equal(1_500m, statement.GetProperty("periodDebit").GetDecimal());
        Assert.Equal(0m, statement.GetProperty("periodCredit").GetDecimal());
        Assert.Equal(1_500m, statement.GetProperty("closingBalance").GetDecimal());

        var lines = statement.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(1_000m, lines[0].GetProperty("runningBalance").GetDecimal());
        Assert.Equal(1_500m, lines[1].GetProperty("runningBalance").GetDecimal());
    }

    [Fact]
    public async Task Statement_WithStartDate_CarriesOpeningBalance()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 1, 15), 2_000m);
        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 5, 5), 300m);

        var statement = await client.GetFromJsonAsync<JsonElement>(
            $"/api/current-accounts/{currentAccountId}/statement?startDate=2026-04-01");

        // Ocak hareketi dönem dışı → açılış bakiyesine gider
        Assert.Equal(2_000m, statement.GetProperty("openingBalance").GetDecimal());
        Assert.Equal(1, statement.GetProperty("lineCount").GetInt32());
        Assert.Equal(2_300m, statement.GetProperty("closingBalance").GetDecimal());
    }

    [Fact]
    public async Task Statement_ExcludesDraftVouchers()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 6, 1), 700m);
        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 6, 2), 9_999m, AccountingVoucherStatus.Draft);

        var statement = await client.GetFromJsonAsync<JsonElement>(
            $"/api/current-accounts/{currentAccountId}/statement");

        // Taslak fiş bakiyeye girmemeli
        Assert.Equal(1, statement.GetProperty("lineCount").GetInt32());
        Assert.Equal(700m, statement.GetProperty("closingBalance").GetDecimal());
    }

    [Fact]
    public async Task Balances_ReturnsAggregatePerCurrentAccount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, currentAccountId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(companyId, currentAccountId, projectId,
            new DateTime(2026, 7, 1), 4_000m);

        var balances = await client.GetFromJsonAsync<JsonElement>(
            $"/api/current-accounts/balances?companyId={companyId}");

        var row = balances.EnumerateArray()
            .Single(x => x.GetProperty("currentAccountId").GetGuid() == currentAccountId);

        Assert.Equal(4_000m, row.GetProperty("totalDebit").GetDecimal());
        Assert.Equal(0m, row.GetProperty("totalCredit").GetDecimal());
        Assert.Equal(4_000m, row.GetProperty("balance").GetDecimal());
        Assert.Equal(1, row.GetProperty("movementCount").GetInt32());
    }

    /// <summary>
    /// Eşleştirme YALNIZCA birebir unvan eşleşmesiyle yapılmalı: canlıda
    /// cari kodu ile hesap kodu çakışıyor ama farklı firmaları gösteriyor,
    /// kod bazlı eşleme yanlış firmanın hesabına yazardı.
    /// </summary>
    [Fact]
    public async Task SynchronizeAccounting_MatchesByTitleOnly_AndSkipsAmbiguous()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Guid companyId, exactId, ambiguousId, codeOnlyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);
            companyId = company.Id;

            db.AccountingAccounts.AddRange(
                new AccountingAccount
                {
                    CompanyId = companyId, Code = "320.900", Name = $"BIREBIR AS {suffix}",
                    Nature = AccountingAccountNature.Credit, Level = 4, IsPostingAllowed = true
                },
                // Aynı unvandan iki hesap → belirsiz, eşleşmemeli
                new AccountingAccount
                {
                    CompanyId = companyId, Code = "320.901", Name = $"MUKERRER AS {suffix}",
                    Nature = AccountingAccountNature.Credit, Level = 4, IsPostingAllowed = true
                },
                new AccountingAccount
                {
                    CompanyId = companyId, Code = "320.902", Name = $"MUKERRER AS {suffix}",
                    Nature = AccountingAccountNature.Credit, Level = 4, IsPostingAllowed = true
                },
                // Kodu cariyle aynı ama unvanı BAŞKA firma → eşleşmemeli
                new AccountingAccount
                {
                    CompanyId = companyId, Code = "320.903", Name = $"BASKA FIRMA {suffix}",
                    Nature = AccountingAccountNature.Credit, Level = 4, IsPostingAllowed = true
                });

            var exact = new CurrentAccount
            {
                CompanyId = companyId, Code = $"C1-{suffix}", Title = $"BIREBIR AS {suffix}",
                Roles = CurrentAccountRoles.Supplier, Status = CurrentAccountStatus.Approved
            };
            var ambiguous = new CurrentAccount
            {
                CompanyId = companyId, Code = $"C2-{suffix}", Title = $"MUKERRER AS {suffix}",
                Roles = CurrentAccountRoles.Supplier, Status = CurrentAccountStatus.Approved
            };
            var codeOnly = new CurrentAccount
            {
                CompanyId = companyId, Code = "320.903", Title = $"KOD AYNI UNVAN FARKLI {suffix}",
                Roles = CurrentAccountRoles.Supplier, Status = CurrentAccountStatus.Approved
            };

            db.CurrentAccounts.AddRange(exact, ambiguous, codeOnly);
            await db.SaveChangesAsync();

            exactId = exact.Id;
            ambiguousId = ambiguous.Id;
            codeOnlyId = codeOnly.Id;
        }

        var response = await client.PostAsync(
            $"/api/current-accounts/synchronize-accounting?companyId={companyId}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, payload.GetProperty("matchedPayable").GetInt32());

        using var verify = fixture.Factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        var accounts = await verifyDb.CurrentAccounts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToDictionaryAsync(x => x.Id);

        Assert.NotNull(accounts[exactId].PayableAccountingAccountId);
        Assert.Null(accounts[ambiguousId].PayableAccountingAccountId);
        Assert.Null(accounts[codeOnlyId].PayableAccountingAccountId);
    }

    [Fact]
    public async Task SynchronizeAccounting_DoesNotOverwriteExistingMapping()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Guid companyId, accountId, manualAccountId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);
            companyId = company.Id;

            var manual = new AccountingAccount
            {
                CompanyId = companyId, Code = "320.800", Name = $"ELLE SECILEN {suffix}",
                Nature = AccountingAccountNature.Credit, Level = 4, IsPostingAllowed = true
            };
            var byName = new AccountingAccount
            {
                CompanyId = companyId, Code = "320.801", Name = $"OTOMATIK AS {suffix}",
                Nature = AccountingAccountNature.Credit, Level = 4, IsPostingAllowed = true
            };
            db.AccountingAccounts.AddRange(manual, byName);
            await db.SaveChangesAsync();
            manualAccountId = manual.Id;

            var account = new CurrentAccount
            {
                CompanyId = companyId, Code = $"C-{suffix}", Title = $"OTOMATIK AS {suffix}",
                Roles = CurrentAccountRoles.Supplier, Status = CurrentAccountStatus.Approved,
                // Admin bilinçli olarak başka bir hesap seçmiş
                PayableAccountingAccountId = manualAccountId
            };
            db.CurrentAccounts.Add(account);
            await db.SaveChangesAsync();
            accountId = account.Id;
        }

        await client.PostAsync(
            $"/api/current-accounts/synchronize-accounting?companyId={companyId}", null);

        using var verify = fixture.Factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await verifyDb.CurrentAccounts.AsNoTracking()
            .SingleAsync(x => x.Id == accountId);

        Assert.Equal(manualAccountId, reloaded.PayableAccountingAccountId);
    }

    [Fact]
    public async Task Statement_UnknownAccount_Returns404()
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.GetAsync(
            $"/api/current-accounts/{Guid.NewGuid()}/statement");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
