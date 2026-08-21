using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Accounting;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Faz D: çek defteri durum geçişleri, faktoring kesinti matematiği ve
/// üretilen tüm fişlerin borç=alacak dengesi.
/// </summary>
[Collection("Integration")]
public sealed class ChequeAndFactoringTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Çek/faktoring fişleri için gereken asgari hesap planı:
    /// 102 Bankalar, 101 alt kırılımları, 103, 120, 320, 780.
    /// </summary>
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
                CompanyId = companyId, Code = "101.01", Name = "Portföydeki Çekler",
                Nature = AccountingAccountNature.Debit, Level = 4, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "101.02", Name = "Tahsildeki Çekler",
                Nature = AccountingAccountNature.Debit, Level = 4, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "103.01", Name = "Verilen Çekler",
                Nature = AccountingAccountNature.Credit, Level = 4, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "120", Name = "Alıcılar",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "320", Name = "Satıcılar",
                Nature = AccountingAccountNature.Credit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "780.01.01", Name = "Finansman Giderleri",
                Nature = AccountingAccountNature.Debit, Level = 5, IsPostingAllowed = true
            });

        await db.SaveChangesAsync();
    }

    private sealed record TestContext(
        Guid CompanyId,
        Guid ProjectId,
        Guid EmployerId,
        Guid SupplierId,
        Guid BankAccountId);

    private async Task<TestContext> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        await SeedChartOfAccountsAsync(db, project.CompanyId);

        var supplier = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"TED-{suffix}",
            Title = $"Test Tedarikçi {suffix}",
            Roles = CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };
        db.CurrentAccounts.Add(supplier);

        var bankAccountingId = await db.AccountingAccounts
            .Where(x => x.CompanyId == project.CompanyId && x.Code == "102")
            .Select(x => x.Id)
            .SingleAsync();

        var bank = new CashAccount
        {
            CompanyId = project.CompanyId,
            Type = CashAccountType.Bank,
            Code = $"BNK-{suffix}",
            Name = $"Test Banka {suffix}",
            BankName = "Test Bankası",
            CurrencyCode = "TRY",
            OpeningBalance = 0m,
            AccountingAccountId = bankAccountingId
        };
        db.CashAccounts.Add(bank);

        await db.SaveChangesAsync();

        return new TestContext(
            project.CompanyId,
            project.Id,
            project.EmployerCurrentAccountId!.Value,
            supplier.Id,
            bank.Id);
    }

    private static object BuildChequePayload(
        TestContext context,
        ChequeDirection direction,
        decimal amount = 100_000m,
        int dueInDays = 45) => new
        {
            companyId = context.CompanyId,
            direction = (int)direction,
            chequeNumber = $"CK{Guid.NewGuid():N}"[..10],
            bankName = "Test Bankası",
            bankBranch = "Merkez",
            drawer = "Test Keşideci",
            currentAccountId = direction == ChequeDirection.Received
                ? context.EmployerId
                : context.SupplierId,
            projectId = context.ProjectId,
            amount,
            currencyCode = "TRY",
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddDays(dueInDays),
            progressPaymentId = (Guid?)null,
            supplierInvoiceId = (Guid?)null,
            description = "Test çeki"
        };

    /// <summary>
    /// Bir kaynağa ait tüm fişlerin kesinleşmiş ve borç=alacak dengeli
    /// olduğunu, beklenen sayıda üretildiğini doğrular.
    /// </summary>
    private async Task AssertVouchersBalancedAsync(
        string sourceModule, Guid sourceEntityId, int expectedVoucherCount)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var vouchers = await db.AccountingVouchers
            .Include(x => x.Lines)
            .Where(x => x.SourceModule == sourceModule && x.SourceEntityId == sourceEntityId)
            .ToListAsync();

        Assert.Equal(expectedVoucherCount, vouchers.Count);

        foreach (var voucher in vouchers)
        {
            Assert.Equal(AccountingVoucherStatus.Posted, voucher.Status);
            Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);
            Assert.Equal(
                voucher.Lines.Sum(x => x.DebitAmount),
                voucher.Lines.Sum(x => x.CreditAmount));
            Assert.True(voucher.TotalDebit > 0m);
        }
    }

    [Fact]
    public async Task ReceivedCheque_Create_PostsBalancedVoucherAndStartsInPortfolio()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, ChequeDirection.Received, amount: 50_000m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        var chequeId = payload.GetProperty("id").GetGuid();
        Assert.Equal((int)ChequeStatus.Portfolio, payload.GetProperty("status").GetInt32());
        Assert.Single(payload.GetProperty("movements").EnumerateArray());

        await AssertVouchersBalancedAsync("Cheque", chequeId, expectedVoucherCount: 1);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lines = await db.AccountingVoucherLines
            .Include(x => x.AccountingAccount)
            .Where(x => x.AccountingVoucher.SourceModule == "Cheque"
                && x.AccountingVoucher.SourceEntityId == chequeId)
            .ToListAsync();

        // 101.01 Portföydeki Çekler borç / 120 Alıcılar alacak
        Assert.Contains(lines, x => x.AccountingAccount.Code == "101.01" && x.DebitAmount == 50_000m);
        Assert.Contains(lines, x => x.AccountingAccount.Code == "120" && x.CreditAmount == 50_000m);
    }

    [Fact]
    public async Task ReceivedCheque_PortfolioToBankToCollected_ProducesCashTransactionAndBalancedVouchers()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, ChequeDirection.Received, amount: 80_000m));
        var chequeId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var toBank = await client.PostChequeAsync($"/api/cheques/{chequeId}/status", chequeId, new
        {
            toStatus = (int)ChequeStatus.AtBank,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = context.BankAccountId,
            description = "Tahsile verildi"
        });
        Assert.Equal(HttpStatusCode.OK, toBank.StatusCode);

        var collect = await client.PostChequeAsync($"/api/cheques/{chequeId}/status", chequeId, new
        {
            toStatus = (int)ChequeStatus.Collected,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = context.BankAccountId,
            description = "Tahsil edildi"
        });
        Assert.Equal(HttpStatusCode.OK, collect.StatusCode);

        var detail = await collect.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)ChequeStatus.Collected, detail.GetProperty("status").GetInt32());
        Assert.Equal(3, detail.GetProperty("movements").GetArrayLength());

        // Giriş + bankaya verme + tahsil = 3 dengeli fiş.
        await AssertVouchersBalancedAsync("Cheque", chequeId, expectedVoucherCount: 3);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Yalnızca tahsilde para hareketi doğar; bankaya tahsile verme
        // çekin yerini değiştirir, kasa bakiyesini etkilemez.
        var cashTransactions = await db.CashTransactions
            .Where(x => x.SourceModule == "Cheque" && x.SourceEntityId == chequeId)
            .ToListAsync();

        var cashTransaction = Assert.Single(cashTransactions);
        Assert.Equal(CashTransactionDirection.In, cashTransaction.Direction);
        Assert.Equal(CashTransactionType.ChequeCollection, cashTransaction.TransactionType);
        Assert.Equal(80_000m, cashTransaction.Amount);
        Assert.NotNull(cashTransaction.AccountingVoucherId);
    }

    [Fact]
    public async Task ReceivedCheque_Bounced_ReturnsReceivableToCurrentAccount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, ChequeDirection.Received, amount: 25_000m));
        var chequeId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var bounce = await client.PostChequeAsync($"/api/cheques/{chequeId}/status", chequeId, new
        {
            toStatus = (int)ChequeStatus.Bounced,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = (Guid?)null,
            description = "Karşılıksız"
        });

        Assert.Equal(HttpStatusCode.OK, bounce.StatusCode);
        await AssertVouchersBalancedAsync("Cheque", chequeId, expectedVoucherCount: 2);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Karşılıksız fişinde 120 yeniden borçlanır (alacak cariye döner).
        var bounceLines = await db.AccountingVoucherLines
            .Include(x => x.AccountingAccount)
            .Where(x => x.AccountingVoucher.SourceModule == "Cheque"
                && x.AccountingVoucher.SourceEntityId == chequeId
                && x.AccountingAccount.Code == "120"
                && x.DebitAmount > 0m)
            .ToListAsync();

        Assert.Single(bounceLines);
        Assert.Equal(25_000m, bounceLines[0].DebitAmount);
    }

    [Fact]
    public async Task IssuedCheque_PaidFromBank_ProducesOutflowAndBalancedVouchers()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, ChequeDirection.Issued, amount: 30_000m));

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var chequeId = created.GetProperty("id").GetGuid();

        Assert.Equal((int)ChequeStatus.Issued, created.GetProperty("status").GetInt32());

        var pay = await client.PostChequeAsync($"/api/cheques/{chequeId}/status", chequeId, new
        {
            toStatus = (int)ChequeStatus.Paid,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = context.BankAccountId,
            description = "Vadesinde ödendi"
        });

        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        await AssertVouchersBalancedAsync("Cheque", chequeId, expectedVoucherCount: 2);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cashTransaction = await db.CashTransactions
            .SingleAsync(x => x.SourceModule == "Cheque" && x.SourceEntityId == chequeId);

        Assert.Equal(CashTransactionDirection.Out, cashTransaction.Direction);
        Assert.Equal(CashTransactionType.ChequePayment, cashTransaction.TransactionType);
        Assert.Equal(30_000m, cashTransaction.Amount);
    }

    [Fact]
    public async Task ChangeStatus_RejectsTransitionOutsideMatrix()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, ChequeDirection.Received));
        var chequeId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        // Alınan çek "Ödendi" (verilen çek durumu) olamaz.
        var invalid = await client.PostChequeAsync($"/api/cheques/{chequeId}/status", chequeId, new
        {
            toStatus = (int)ChequeStatus.Paid,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = context.BankAccountId,
            description = "Geçersiz"
        });
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);

        // Portföyden doğrudan faktoringe geçilemez — kırdırma faktoring
        // modülünden, kesinti matematiğiyle birlikte yapılır.
        var toFactoring = await client.PostChequeAsync($"/api/cheques/{chequeId}/status", chequeId, new
        {
            toStatus = (int)ChequeStatus.AtFactoring,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = context.BankAccountId,
            description = "Geçersiz"
        });
        Assert.Equal(HttpStatusCode.Conflict, toFactoring.StatusCode);
    }

    [Fact]
    public async Task ChangeStatus_CollectionRequiresCashAccount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, ChequeDirection.Received));
        var chequeId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var response = await client.PostChequeAsync($"/api/cheques/{chequeId}/status", chequeId, new
        {
            toStatus = (int)ChequeStatus.Collected,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = (Guid?)null,
            description = "Kasa seçilmedi"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangeStatus_TerminalStatusCannotMoveAgain()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, ChequeDirection.Received));
        var chequeId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await client.PostChequeAsync($"/api/cheques/{chequeId}/status", chequeId, new
        {
            toStatus = (int)ChequeStatus.Collected,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = context.BankAccountId,
            description = "Tahsil"
        });

        var again = await client.PostChequeAsync($"/api/cheques/{chequeId}/status", chequeId, new
        {
            toStatus = (int)ChequeStatus.Bounced,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = (Guid?)null,
            description = "Tahsilden sonra karşılıksız olamaz"
        });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Theory]
    // Nominal 100.000, komisyon %2 → 2.000; BSMV %5 → 100; masraf 250.
    [InlineData(100_000, 2, 0, 250, 2_000, 100, 2_350, 97_650)]
    // Komisyon tutarı doğrudan verilirse oran ondan türetilir.
    [InlineData(50_000, 0, 1_500, 0, 1_500, 75, 1_575, 48_425)]
    // Kuruşlu yuvarlama: 33.333,33 × %1,75 = 583,33; BSMV 29,17.
    [InlineData(33_333.33, 1.75, 0, 0, 583.33, 29.17, 612.50, 32_720.83)]
    public void FactoringCalculation_SplitsCommissionBsmvAndExpense(
        decimal chequeAmount,
        decimal commissionRate,
        decimal commissionAmount,
        decimal expenseAmount,
        decimal expectedCommission,
        decimal expectedBsmv,
        decimal expectedTotalDeduction,
        decimal expectedNet)
    {
        var result = FactoringService.Calculate(
            chequeAmount,
            commissionRate == 0 ? null : commissionRate,
            commissionAmount == 0 ? null : commissionAmount,
            bsmvRate: null,
            expenseAmount);

        Assert.Equal(expectedCommission, result.CommissionAmount);
        Assert.Equal(expectedBsmv, result.BsmvAmount);
        Assert.Equal(expectedTotalDeduction, result.TotalDeductionAmount);
        Assert.Equal(expectedNet, result.NetAmount);

        // Net + kesinti her zaman nominale eşit olmalı — fiş dengesi buna dayanır.
        Assert.Equal(
            decimal.Round(chequeAmount, 2),
            result.NetAmount + result.TotalDeductionAmount);
    }

    [Fact]
    public void FactoringCalculation_RejectsDeductionsExceedingChequeAmount()
    {
        Assert.Throws<ArgumentException>(() => FactoringService.Calculate(
            chequeAmount: 1_000m,
            commissionRate: null,
            commissionAmount: 900m,
            bsmvRate: null,
            expenseAmount: 200m));
    }

    [Fact]
    public async Task Factoring_DiscountsChequeAndPostsBalancedVoucherWithSeparateDeductionLines()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, ChequeDirection.Received, amount: 100_000m));
        var chequeId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var response = await client.PostChequeAsync("/api/factoring", chequeId, new
        {
            chequeId,
            cashAccountId = context.BankAccountId,
            factoringCurrentAccountId = (Guid?)null,
            projectId = context.ProjectId,
            transactionDate = DateTime.UtcNow.Date,
            commissionRate = 2m,
            commissionAmount = (decimal?)null,
            bsmvRate = (decimal?)null,
            expenseAmount = 250m,
            description = "Test kırdırma"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2_000m, payload.GetProperty("commissionAmount").GetDecimal());
        Assert.Equal(100m, payload.GetProperty("bsmvAmount").GetDecimal());
        Assert.Equal(250m, payload.GetProperty("expenseAmount").GetDecimal());
        Assert.Equal(2_350m, payload.GetProperty("totalDeductionAmount").GetDecimal());
        Assert.Equal(97_650m, payload.GetProperty("netAmount").GetDecimal());

        var factoringId = payload.GetProperty("id").GetGuid();
        await AssertVouchersBalancedAsync("Factoring", factoringId, expectedVoucherCount: 1);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lines = await db.AccountingVoucherLines
            .Include(x => x.AccountingAccount)
            .Where(x => x.AccountingVoucher.SourceModule == "Factoring"
                && x.AccountingVoucher.SourceEntityId == factoringId)
            .ToListAsync();

        // Net banka girişi + üç ayrı kesinti satırı + çek çıkışı = 5 satır.
        Assert.Equal(5, lines.Count);
        Assert.Contains(lines, x => x.AccountingAccount.Code == "102" && x.DebitAmount == 97_650m);
        Assert.Equal(3, lines.Count(x => x.AccountingAccount.Code == "780.01.01"));
        Assert.Contains(lines, x => x.AccountingAccount.Code == "101.01" && x.CreditAmount == 100_000m);

        // Kesintiler projeye yazılır (finansman gideri proje maliyetine bağlanır).
        Assert.All(
            lines.Where(x => x.AccountingAccount.Code == "780.01.01"),
            line => Assert.Equal(context.ProjectId, line.ProjectId));

        var cheque = await db.Cheques.SingleAsync(x => x.Id == chequeId);
        Assert.Equal(ChequeStatus.AtFactoring, cheque.Status);

        var cashTransaction = await db.CashTransactions
            .SingleAsync(x => x.SourceModule == "Factoring" && x.SourceEntityId == factoringId);
        Assert.Equal(97_650m, cashTransaction.Amount);
        Assert.Equal(CashTransactionDirection.In, cashTransaction.Direction);
    }

    [Fact]
    public async Task Factoring_RejectsChequeThatIsNotInPortfolio()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, ChequeDirection.Received, amount: 10_000m));
        var chequeId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await client.PostChequeAsync($"/api/cheques/{chequeId}/status", chequeId, new
        {
            toStatus = (int)ChequeStatus.Collected,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = context.BankAccountId,
            description = "Tahsil"
        });

        var response = await client.PostChequeAsync("/api/factoring", chequeId, new
        {
            chequeId,
            cashAccountId = context.BankAccountId,
            factoringCurrentAccountId = (Guid?)null,
            projectId = (Guid?)null,
            transactionDate = DateTime.UtcNow.Date,
            commissionRate = 2m,
            commissionAmount = (decimal?)null,
            bsmvRate = (decimal?)null,
            expenseAmount = 0m,
            description = (string?)null
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CashAccountTransaction_CreatesBalancedVoucherAndUpdatesBalance()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync(
            $"/api/cash-accounts/{context.BankAccountId}/transactions", new
            {
                transactionDate = DateTime.UtcNow.Date,
                transactionType = (int)CashTransactionType.Collection,
                direction = (int)CashTransactionDirection.In,
                amount = 12_500m,
                currencyCode = "TRY",
                description = "Test tahsilat",
                documentNumber = "DEK-1",
                currentAccountId = context.EmployerId,
                projectId = context.ProjectId
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var voucherId = payload.GetProperty("accountingVoucherId").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var voucher = await db.AccountingVouchers
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == voucherId);

        Assert.Equal(AccountingVoucherStatus.Posted, voucher.Status);
        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);
        Assert.Equal(12_500m, voucher.TotalDebit);

        var statement = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cash-accounts/{context.BankAccountId}/transactions");

        Assert.Equal(12_500m, statement.GetProperty("closingBalance").GetDecimal());
        Assert.Equal(12_500m, statement.GetProperty("totalIn").GetDecimal());
    }

    [Fact]
    public async Task CashAccountTransaction_RejectsChequeTypesFromManualEntry()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync(
            $"/api/cash-accounts/{context.BankAccountId}/transactions", new
            {
                transactionDate = DateTime.UtcNow.Date,
                transactionType = (int)CashTransactionType.ChequeCollection,
                direction = (int)CashTransactionDirection.In,
                amount = 1_000m,
                currencyCode = "TRY",
                description = "Elle çek tahsili",
                documentNumber = (string?)null,
                currentAccountId = context.EmployerId,
                projectId = (Guid?)null
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CashFlow_SplitsExpectedInflowsAndOutflowsIntoBuckets()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // 20 gün vadeli alınan çek → 30/60/90 kovalarının hepsinde.
        await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, ChequeDirection.Received, amount: 40_000m, dueInDays: 20));

        // 75 gün vadeli verilen çek → yalnızca 90 gün kovasında.
        await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, ChequeDirection.Issued, amount: 15_000m, dueInDays: 75));

        var response = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cash-flow?companyId={context.CompanyId}");

        var buckets = response.GetProperty("buckets").EnumerateArray().ToList();
        Assert.Equal(3, buckets.Count);

        var thirty = buckets.Single(x => x.GetProperty("days").GetInt32() == 30);
        Assert.Equal(40_000m, thirty.GetProperty("inflowAmount").GetDecimal());
        Assert.Equal(0m, thirty.GetProperty("outflowAmount").GetDecimal());
        Assert.Equal(40_000m, thirty.GetProperty("netAmount").GetDecimal());

        var ninety = buckets.Single(x => x.GetProperty("days").GetInt32() == 90);
        Assert.Equal(40_000m, ninety.GetProperty("inflowAmount").GetDecimal());
        Assert.Equal(15_000m, ninety.GetProperty("outflowAmount").GetDecimal());
        Assert.Equal(25_000m, ninety.GetProperty("netAmount").GetDecimal());

        Assert.Contains(
            response.GetProperty("inflows").EnumerateArray(),
            x => x.GetProperty("kind").GetString() == "ReceivedCheque");
        Assert.Contains(
            response.GetProperty("outflows").EnumerateArray(),
            x => x.GetProperty("kind").GetString() == "IssuedCheque");
    }

    // ---------------- Proje bağı ----------------

    /// <summary>
    /// Her çek bir yere yazılmalı: proje ya da masraf merkezi. İkisi de
    /// boş kalabildiği sürece çek hiçbir kırılıma düşmüyordu ve proje
    /// bazlı nakit akışında hiç görünmüyordu.
    /// </summary>
    [Fact]
    public async Task Cheque_WithoutProjectOrCostCenter_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var payload = BuildChequePayload(context, ChequeDirection.Issued);

        var body = payload.GetType().GetProperties()
            .ToDictionary(x => x.Name, x => x.GetValue(payload));

        body["projectId"] = null;
        body["costCenterCode"] = null;

        var response = await client.PostAsJsonAsync("/api/cheques", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("masraf merkezine",
            await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// MERKEZ YOLU KORUNUYOR: ofis kirası gibi projesi olmayan çek
    /// masraf merkeziyle açılabiliyor. Proje tek başına zorunlu
    /// tutulsaydı kullanıcı rastgele bir proje seçer ve tam da kurmak
    /// istediğimiz kırılım bozulurdu.
    /// </summary>
    [Fact]
    public async Task Cheque_WithOnlyCostCenter_IsAccepted()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var payload = BuildChequePayload(context, ChequeDirection.Issued);

        var body = payload.GetType().GetProperties()
            .ToDictionary(x => x.Name, x => x.GetValue(payload));

        body["projectId"] = null;
        body["costCenterCode"] = "MERKEZ";

        var response = await client.PostAsJsonAsync("/api/cheques", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Proje filtresi yalnız o projenin çeklerini döndürüyor; ay
    /// gruplaması bunun üzerine kuruluyor ("bu projeye bu ay ne kadar
    /// çek verilmiş").
    /// </summary>
    [Fact]
    public async Task ChequeList_FiltersByProject()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Guid otherProjectId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var other = new Project
            {
                CompanyId = context.CompanyId,
                BranchId = await db.Projects
                    .Where(x => x.Id == context.ProjectId)
                    .Select(x => x.BranchId)
                    .SingleAsync(),
                Code = $"PRJ2-{suffix}",
                Name = $"İkinci Proje {suffix}",
                CurrencyCode = "TRY",
                Status = ProjectStatus.Active
            };

            db.Projects.Add(other);
            await db.SaveChangesAsync();

            otherProjectId = other.Id;
        }

        await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, ChequeDirection.Issued));

        var second = BuildChequePayload(context, ChequeDirection.Issued);

        var secondBody = second.GetType().GetProperties()
            .ToDictionary(x => x.Name, x => x.GetValue(second));

        secondBody["projectId"] = otherProjectId;

        await client.PostAsJsonAsync("/api/cheques", secondBody);

        var filtered = await (await client.GetAsync(
            $"/api/cheques?companyId={context.CompanyId}" +
            $"&direction={(int)ChequeDirection.Issued}" +
            $"&projectId={context.ProjectId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var rows = filtered.EnumerateArray().ToList();

        Assert.NotEmpty(rows);
        Assert.All(rows, row =>
            Assert.Equal(context.ProjectId,
                row.GetProperty("projectId").GetGuid()));

        // Filtresiz listede ikisi de var: filtre kayıt gizlemiyor,
        // yalnızca daraltıyor.
        var all = await (await client.GetAsync(
            $"/api/cheques?companyId={context.CompanyId}" +
            $"&direction={(int)ChequeDirection.Issued}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, all.EnumerateArray().Count());
    }

    /// <summary>
    /// Liste VADEYE göre sıralı geliyor: ay gruplaması ekranda buna
    /// dayanıyor, ayrı bir sıralama yapılmıyor.
    /// </summary>
    [Fact]
    public async Task ChequeList_IsOrderedByDueDate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, ChequeDirection.Issued, dueInDays: 90));

        await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, ChequeDirection.Issued, dueInDays: 15));

        var list = await (await client.GetAsync(
            $"/api/cheques?companyId={context.CompanyId}" +
            $"&direction={(int)ChequeDirection.Issued}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var dueDates = list.EnumerateArray()
            .Select(x => x.GetProperty("dueDate").GetDateTime())
            .ToList();

        Assert.Equal(dueDates.OrderBy(x => x), dueDates);
    }
}
