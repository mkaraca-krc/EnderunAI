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
/// Faz B: hakediş kesinleştirmede otomatik gelir fişi
/// (120 Alıcılar + kesintiler borç / 600 Yurtiçi Satışlar +
/// 391 Hesaplanan KDV alacak), KDV tevkifatı dahil.
/// </summary>
[Collection("Integration")]
public sealed class ProgressPaymentAccountingTests(DatabaseFixture fixture)
{
    private static async Task SeedRevenueAccountsAsync(AppDbContext db, Guid companyId)
    {
        db.AccountingAccounts.AddRange(
            new AccountingAccount
            {
                CompanyId = companyId, Code = "120", Name = "Alıcılar",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "600.03", Name = "% 20 KDV Lİ SATIŞLAR",
                Nature = AccountingAccountNature.Credit, Level = 4, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "391.09", Name = "% 20 HESAPLANAN KDV",
                Nature = AccountingAccountNature.Credit, Level = 4, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "126", Name = "Verilen Depozito ve Teminatlar",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Hakediş için işvereni tanımlı bir proje kurar (TestDataFactory
    /// zaten işveren carisi oluşturuyor, projeye bağlıyor).
    /// </summary>
    private async Task<(Guid CompanyId, Guid ProjectId)> CreateRevenueContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        await SeedRevenueAccountsAsync(db, project.CompanyId);

        return (project.CompanyId, project.Id);
    }

    private static object BuildPayload(
        Guid companyId, Guid projectId,
        decimal quantity, decimal unitPrice,
        decimal vatRate = 20m,
        int withholdingNumerator = 0, int withholdingDenominator = 0,
        object[]? deductions = null) => new
        {
            companyId,
            projectId,
            projectMeasurementId = (Guid?)null,
            progressPaymentNumber = $"HK-{Guid.NewGuid():N}"[..12],
            periodNumber = 1,
            periodStartDate = (DateOnly?)null,
            periodEndDate = (DateOnly?)null,
            progressPaymentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            priceDifferenceAmount = 0m,
            vatRate,
            withholdingNumerator,
            withholdingDenominator,
            description = "Faz B testi",
            notes = (string?)null,
            items = new[]
            {
                new
                {
                    engineeringPositionId = (Guid?)null,
                    positionCode = "POZ-1",
                    description = "Test imalat",
                    unit = "m2",
                    contractQuantity = quantity,
                    currentQuantity = quantity,
                    unitPrice,
                    measurementReference = (string?)null,
                    notes = (string?)null
                }
            },
            deductions = deductions ?? Array.Empty<object>()
        };

    private async Task<Guid> CreateApprovedAsync(HttpClient client, object payload)
    {
        var createResponse = await client.PostAsJsonAsync("/api/progress-payments", payload);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var id = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/progress-payments/{id}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/progress-payments/{id}/approve", null)).StatusCode);

        return id;
    }

    private async Task<AccountingVoucher> LoadVoucherAsync(Guid progressPaymentId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var payment = await db.ProgressPayments.AsNoTracking()
            .SingleAsync(x => x.Id == progressPaymentId);

        Assert.NotNull(payment.AccountingVoucherId);

        return await db.AccountingVouchers.AsNoTracking()
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.Id == payment.AccountingVoucherId!.Value);
    }

    [Fact]
    public async Task Post_CreatesBalancedPostedRevenueVoucher()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId) = await CreateRevenueContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // 100 m2 × 1.000 = 100.000 hakediş, %20 KDV = 20.000
        var id = await CreateApprovedAsync(client,
            BuildPayload(companyId, projectId, quantity: 100m, unitPrice: 1_000m));

        var postResponse = await client.PostAsync($"/api/progress-payments/{id}/post", null);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        var voucher = await LoadVoucherAsync(id);

        Assert.Equal(AccountingVoucherStatus.Posted, voucher.Status);
        Assert.Equal("ProgressPayment", voucher.SourceModule);
        Assert.Equal(id, voucher.SourceEntityId);
        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);
        Assert.Equal(120_000m, voucher.TotalDebit);

        var receivable = voucher.Lines.Single(x => x.AccountingAccount.Code == "120");
        Assert.Equal(120_000m, receivable.DebitAmount);
        Assert.NotNull(receivable.CurrentAccountId);

        Assert.Equal(100_000m,
            voucher.Lines.Single(x => x.AccountingAccount.Code == "600.03").CreditAmount);
        Assert.Equal(20_000m,
            voucher.Lines.Single(x => x.AccountingAccount.Code == "391.09").CreditAmount);
    }

    [Fact]
    public async Task Post_WithVatWithholding_OnlyDeclaresNonWithheldVat()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId) = await CreateRevenueContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // 100.000 hakediş, %20 KDV = 20.000, 4/10 tevkifat = 8.000 kesilir
        // → beyan edilen KDV 12.000, tahsil edilecek 112.000
        var id = await CreateApprovedAsync(client,
            BuildPayload(companyId, projectId, quantity: 100m, unitPrice: 1_000m,
                withholdingNumerator: 4, withholdingDenominator: 10));

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/progress-payments/{id}/post", null)).StatusCode);

        var voucher = await LoadVoucherAsync(id);

        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);
        Assert.Equal(112_000m, voucher.TotalDebit);
        Assert.Equal(112_000m,
            voucher.Lines.Single(x => x.AccountingAccount.Code == "120").DebitAmount);
        Assert.Equal(100_000m,
            voucher.Lines.Single(x => x.AccountingAccount.Code == "600.03").CreditAmount);
        // Tevkif edilen 8.000 alıcı tarafından beyan edilir, bizde görünmez.
        Assert.Equal(12_000m,
            voucher.Lines.Single(x => x.AccountingAccount.Code == "391.09").CreditAmount);
    }

    [Fact]
    public async Task Post_WithDeductions_DebitsDeductionAccountAndReducesReceivable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId) = await CreateRevenueContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // 100.000 + 20.000 KDV, 5.000 teminat kesintisi → 115.000 tahsil
        var id = await CreateApprovedAsync(client,
            BuildPayload(companyId, projectId, quantity: 100m, unitPrice: 1_000m,
                deductions: [
                    new
                    {
                        deductionType = 0,
                        description = "Nakit teminat",
                        rate = 0m,
                        baseAmount = 0m,
                        manualAmount = (decimal?)5_000m,
                        notes = (string?)null,
                        accountingAccountId = (Guid?)null
                    }
                ]));

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/progress-payments/{id}/post", null)).StatusCode);

        var voucher = await LoadVoucherAsync(id);

        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);
        Assert.Equal(120_000m, voucher.TotalDebit);

        // Alacak, kesinti kadar azalır; kesinti kendi hesabına (126) borç yazılır.
        Assert.Equal(115_000m,
            voucher.Lines.Single(x => x.AccountingAccount.Code == "120").DebitAmount);
        Assert.Equal(5_000m,
            voucher.Lines.Single(x => x.AccountingAccount.Code == "126").DebitAmount);
    }

    [Fact]
    public async Task Post_WithoutEmployerOnProject_IsRejectedWithClearMessage()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId) = await CreateRevenueContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var id = await CreateApprovedAsync(client,
            BuildPayload(companyId, projectId, quantity: 10m, unitPrice: 100m));

        // İşveren carisi sonradan kaldırılmış bir proje (Keşif akışında mümkün)
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await db.Projects.SingleAsync(x => x.Id == projectId);
            project.EmployerCurrentAccountId = null;
            await db.SaveChangesAsync();
        }

        var postResponse = await client.PostAsync($"/api/progress-payments/{id}/post", null);
        Assert.Equal(HttpStatusCode.Conflict, postResponse.StatusCode);

        var payload = await postResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("işveren", payload.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);

        // Fiş üretilmediği gibi hakediş de Approved'da kalmalı (transaction geri alındı).
        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payment = await verifyDb.ProgressPayments.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(ProgressPaymentStatus.Approved, payment.Status);
        Assert.Null(payment.AccountingVoucherId);
    }

    [Fact]
    public async Task Cancel_AfterPosting_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId) = await CreateRevenueContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var id = await CreateApprovedAsync(client,
            BuildPayload(companyId, projectId, quantity: 5m, unitPrice: 200m));

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/progress-payments/{id}/post", null)).StatusCode);

        var cancelResponse = await client.PostAsJsonAsync(
            $"/api/progress-payments/{id}/cancel", new { reason = "Test iptali" });

        Assert.Equal(HttpStatusCode.Conflict, cancelResponse.StatusCode);
    }

    [Fact]
    public async Task Post_TwiceIsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId) = await CreateRevenueContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var id = await CreateApprovedAsync(client,
            BuildPayload(companyId, projectId, quantity: 3m, unitPrice: 500m));

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/progress-payments/{id}/post", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsync($"/api/progress-payments/{id}/post", null)).StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var voucherCount = await db.AccountingVouchers
            .CountAsync(x => x.SourceModule == "ProgressPayment" && x.SourceEntityId == id);
        Assert.Equal(1, voucherCount);
    }
}
