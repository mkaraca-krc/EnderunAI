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
/// Çek erteleme/değişim zinciri.
///
/// Ertelemenin muhasebedeki anlamı şudur: eski çek ters kayıtla kapanır,
/// yeni çek kendi kaydını üretir; net etki yalnızca vadenin değişmesidir.
/// Eski çek açık kalsaydı hem nakit akışında hem cari bakiyesinde aynı
/// borç iki kez görünürdü.
/// </summary>
[Collection("Integration")]
public sealed class ChequeReplacementTests(DatabaseFixture fixture)
{
    private sealed record TestContext(
        Guid CompanyId, Guid ProjectId, Guid SupplierId, Guid EmployerId);

    private static async Task SeedChartOfAccountsAsync(AppDbContext db, Guid companyId)
    {
        db.AccountingAccounts.AddRange(
            new AccountingAccount
            {
                CompanyId = companyId, Code = "101.01", Name = "Portföydeki Çekler",
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
            });

        await db.SaveChangesAsync();
    }

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
        await db.SaveChangesAsync();

        return new TestContext(
            project.CompanyId, project.Id, supplier.Id,
            project.EmployerCurrentAccountId!.Value);
    }

    private static object BuildChequePayload(
        TestContext context, ChequeDirection direction, int dueInDays = 30,
        object[]? allocations = null) => new
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
            amount = 100_000m,
            currencyCode = "TRY",
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddDays(dueInDays),
            progressPaymentId = (Guid?)null,
            supplierInvoiceId = (Guid?)null,
            description = "Test çeki",
            allocations
        };

    private async Task<Guid> CreateChequeAsync(
        HttpClient client, TestContext context, ChequeDirection direction,
        object[]? allocations = null)
    {
        var response = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, direction, allocations: allocations));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Verilen çek ertelendiğinde: eski çek "Ertelendi" olur, ters kaydı
    /// kesilir; yeni çek yeni vadeyle açılır ve zincire bağlanır.
    /// İkisinin fişlerinin net etkisi vadenin değişmesinden ibarettir.
    /// </summary>
    [Fact]
    public async Task ReplaceIssuedCheque_ClosesOldAndOpensNewWithChain()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var oldChequeId = await CreateChequeAsync(client, context, ChequeDirection.Issued);
        var newNumber = $"YENI{Guid.NewGuid():N}"[..10];
        var newDueDate = DateTime.UtcNow.Date.AddDays(90);

        var response = await client.PostAsJsonAsync(
            $"/api/cheques/{oldChequeId}/replace",
            new
            {
                chequeNumber = newNumber,
                dueDate = newDueDate,
                movementDate = DateTime.UtcNow.Date,
                description = "Tedarikçi vade uzatması istedi"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var replacement = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newChequeId = replacement.GetProperty("id").GetGuid();

        Assert.Equal((int)ChequeStatus.Issued, replacement.GetProperty("status").GetInt32());
        Assert.Equal(newNumber, replacement.GetProperty("chequeNumber").GetString());
        Assert.Equal(100_000m, replacement.GetProperty("amount").GetDecimal());
        Assert.Equal(oldChequeId, replacement.GetProperty("replacesChequeId").GetGuid());
        Assert.Equal(1, replacement.GetProperty("renewalCount").GetInt32());

        var old = await client.GetFromJsonAsync<JsonElement>($"/api/cheques/{oldChequeId}");

        Assert.Equal((int)ChequeStatus.Replaced, old.GetProperty("status").GetInt32());
        Assert.Equal("Ertelendi (değiştirildi)", old.GetProperty("statusName").GetString());
        Assert.Equal(newChequeId, old.GetProperty("replacedByChequeId").GetGuid());

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var vouchers = await db.AccountingVouchers
            .Include(x => x.Lines)
            .Where(x => x.SourceModule == "Cheque" &&
                        (x.SourceEntityId == oldChequeId || x.SourceEntityId == newChequeId))
            .ToListAsync();

        // Eski çekin girişi + ters kaydı + yeni çekin girişi.
        Assert.Equal(3, vouchers.Count);
        Assert.All(vouchers, voucher =>
        {
            Assert.Equal(AccountingVoucherStatus.Posted, voucher.Status);
            Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);
        });

        // Eski çeke ait iki fişin net etkisi sıfır olmalı.
        var oldNet = vouchers
            .Where(x => x.SourceEntityId == oldChequeId)
            .SelectMany(x => x.Lines)
            .GroupBy(x => x.AccountingAccountId)
            .Select(g => g.Sum(x => x.DebitAmount - x.CreditAmount));

        Assert.All(oldNet, net => Assert.Equal(0m, net));

        var newCheque = await db.Cheques.SingleAsync(x => x.Id == newChequeId);
        Assert.Equal(newDueDate, newCheque.DueDate);
    }

    /// <summary>
    /// Alınan çek de ertelenebilir: müşteri eski çeki geri alıp yeni
    /// vadeli çek verir.
    /// </summary>
    [Fact]
    public async Task ReplaceReceivedCheque_IsSupported()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var oldChequeId = await CreateChequeAsync(client, context, ChequeDirection.Received);

        var response = await client.PostAsJsonAsync(
            $"/api/cheques/{oldChequeId}/replace",
            new
            {
                chequeNumber = $"YENI{Guid.NewGuid():N}"[..10],
                dueDate = DateTime.UtcNow.Date.AddDays(120),
                movementDate = DateTime.UtcNow.Date
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var replacement = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal((int)ChequeStatus.Portfolio, replacement.GetProperty("status").GetInt32());
        Assert.Equal(oldChequeId, replacement.GetProperty("replacesChequeId").GetGuid());
    }

    /// <summary>
    /// Üst üste erteleme zinciri ve sayacı — sürekli ertelenen çek risk
    /// sinyalidir, sayı kaybolmamalı.
    /// </summary>
    [Fact]
    public async Task RepeatedReplacement_IncrementsRenewalCount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var currentId = await CreateChequeAsync(client, context, ChequeDirection.Issued);

        for (var round = 1; round <= 3; round++)
        {
            var response = await client.PostAsJsonAsync(
                $"/api/cheques/{currentId}/replace",
                new
                {
                    chequeNumber = $"Y{round}{Guid.NewGuid():N}"[..10],
                    dueDate = DateTime.UtcNow.Date.AddDays(30 * (round + 1)),
                    movementDate = DateTime.UtcNow.Date
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(round, payload.GetProperty("renewalCount").GetInt32());
            currentId = payload.GetProperty("id").GetGuid();
        }
    }

    /// <summary>
    /// Ertelenen çek nakit akışında görünmemeli; yerini yeni vadeli çek
    /// almalı. Eski çek de listede kalsaydı aynı ödeme iki kez planlanmış
    /// görünürdü.
    /// </summary>
    [Fact]
    public async Task ReplacedCheque_LeavesCashFlowAndNewDueDateApplies()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var oldChequeId = await CreateChequeAsync(client, context, ChequeDirection.Issued);

        var newDueDate = DateTime.UtcNow.Date.AddDays(200);

        var response = await client.PostAsJsonAsync(
            $"/api/cheques/{oldChequeId}/replace",
            new
            {
                chequeNumber = $"YENI{Guid.NewGuid():N}"[..10],
                dueDate = newDueDate,
                movementDate = DateTime.UtcNow.Date
            });

        var newChequeId = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var cashFlow = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cash-flow?companyId={context.CompanyId}");

        var chequeItems = cashFlow.GetProperty("outflows").EnumerateArray()
            .Where(x => x.GetProperty("kind").GetString() == "IssuedCheque")
            .ToList();

        Assert.Single(chequeItems);
        Assert.Equal(newChequeId, chequeItems[0].GetProperty("sourceId").GetGuid());
        Assert.Equal(newDueDate.Date,
            chequeItems[0].GetProperty("expectedDate").GetDateTime().Date);
    }

    /// <summary>
    /// Dağılım yeni çeke taşınır: yeni çek aynı projeleri karşılıyor,
    /// taşınmasaydı masraf merkezi kırılımı kaybolurdu.
    /// </summary>
    [Fact]
    public async Task Replacement_CarriesAllocationsToNewCheque()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var oldChequeId = await CreateChequeAsync(client, context, ChequeDirection.Issued,
            allocations:
            [
                new { amount = 70_000m, projectId = context.ProjectId },
                new { amount = 30_000m, costCenterCode = "MERKEZ" }
            ]);

        var response = await client.PostAsJsonAsync(
            $"/api/cheques/{oldChequeId}/replace",
            new
            {
                chequeNumber = $"YENI{Guid.NewGuid():N}"[..10],
                dueDate = DateTime.UtcNow.Date.AddDays(75),
                movementDate = DateTime.UtcNow.Date
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var replacement = await response.Content.ReadFromJsonAsync<JsonElement>();
        var allocations = replacement.GetProperty("allocations").EnumerateArray().ToList();

        Assert.Equal(2, allocations.Count);
        Assert.Contains(allocations, x => x.GetProperty("amount").GetDecimal() == 30_000m &&
                                          x.GetProperty("costCenterCode").GetString() == "MERKEZ");

        // Yeni çekin fişinin cari tarafı da dağılıma göre bölünmeli.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var newChequeId = replacement.GetProperty("id").GetGuid();

        var voucher = await db.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.SourceModule == "Cheque" && x.SourceEntityId == newChequeId);

        var payableLines = voucher.Lines
            .Where(x => x.AccountingAccount.Code == "320")
            .ToList();

        Assert.Equal(2, payableLines.Count);
        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);
    }

    /// <summary>
    /// Aynı çek iki kez ertelenemez; ikinci istek zinciri çatallandırır.
    /// </summary>
    [Fact]
    public async Task Replace_AlreadyReplacedCheque_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var oldChequeId = await CreateChequeAsync(client, context, ChequeDirection.Issued);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/cheques/{oldChequeId}/replace",
            new
            {
                chequeNumber = $"YENI{Guid.NewGuid():N}"[..10],
                dueDate = DateTime.UtcNow.Date.AddDays(60),
                movementDate = DateTime.UtcNow.Date
            })).StatusCode);

        var second = await client.PostAsJsonAsync(
            $"/api/cheques/{oldChequeId}/replace",
            new
            {
                chequeNumber = $"BASKA{Guid.NewGuid():N}"[..10],
                dueDate = DateTime.UtcNow.Date.AddDays(90),
                movementDate = DateTime.UtcNow.Date
            });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    /// <summary>
    /// Tahsil edilmiş çek ertelenemez: kapanmış bir işlem geri açılamaz.
    /// </summary>
    [Fact]
    public async Task Replace_CollectedCheque_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var chequeId = await CreateChequeAsync(client, context, ChequeDirection.Received);

        Guid cashAccountId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var bankAccountingId = await db.AccountingAccounts
                .Where(x => x.CompanyId == context.CompanyId && x.Code == "101.01")
                .Select(x => x.Id)
                .SingleAsync();

            var bank = new CashAccount
            {
                CompanyId = context.CompanyId,
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
            cashAccountId = bank.Id;
        }

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/cheques/{chequeId}/status",
            new
            {
                toStatus = (int)ChequeStatus.Collected,
                movementDate = DateTime.UtcNow.Date,
                cashAccountId,
                description = "Tahsil edildi"
            })).StatusCode);

        var replace = await client.PostAsJsonAsync(
            $"/api/cheques/{chequeId}/replace",
            new
            {
                chequeNumber = $"YENI{Guid.NewGuid():N}"[..10],
                dueDate = DateTime.UtcNow.Date.AddDays(60),
                movementDate = DateTime.UtcNow.Date
            });

        Assert.Equal(HttpStatusCode.Conflict, replace.StatusCode);
        Assert.Contains("ertelenemez", await replace.Content.ReadAsStringAsync());
    }
}
