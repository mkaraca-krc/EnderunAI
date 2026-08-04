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
/// Hakediş dışı satış faturası: kalem/KDV hesabı, tevkifat ve
/// kesinleştirmede üretilen gelir fişi (120/600/391).
/// </summary>
[Collection("Integration")]
public sealed class SalesInvoiceTests(DatabaseFixture fixture)
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
            });

        await db.SaveChangesAsync();
    }

    private async Task<(Guid CompanyId, Guid CustomerId, Guid ProjectId)> CreateContextAsync(
        string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        await SeedRevenueAccountsAsync(db, project.CompanyId);

        var customer = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"MUS-{suffix}",
            Title = $"Test Müşteri {suffix}",
            Roles = CurrentAccountRoles.Customer,
            Status = CurrentAccountStatus.Approved
        };
        db.CurrentAccounts.Add(customer);
        await db.SaveChangesAsync();

        return (project.CompanyId, customer.Id, project.Id);
    }

    private static object BuildPayload(
        Guid companyId, Guid customerId, Guid? projectId,
        decimal quantity, decimal unitPrice, decimal vatRate = 20m,
        decimal withholdingAmount = 0m,
        string? officialInvoiceNumber = null) => new
        {
            companyId,
            customerCurrentAccountId = customerId,
            projectId,
            officialInvoiceNumber = officialInvoiceNumber
                ?? $"ENE{Guid.NewGuid():N}"[..16],
            invoiceDate = DateTime.UtcNow.Date,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            withholdingAmount,
            description = "Hakediş dışı malzeme satışı",
            notes = (string?)null,
            items = new[]
            {
                new
                {
                    description = "40 CT-KM Ek Elemanı",
                    quantity,
                    unit = "adet",
                    unitPrice,
                    vatRate
                }
            }
        };

    private async Task<AccountingVoucher> LoadVoucherAsync(Guid invoiceId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var invoice = await db.SalesInvoices.AsNoTracking()
            .SingleAsync(x => x.Id == invoiceId);

        Assert.NotNull(invoice.AccountingVoucherId);

        return await db.AccountingVouchers.AsNoTracking()
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.Id == invoice.AccountingVoucherId!.Value);
    }

    [Fact]
    public async Task Create_ComputesTotalsFromLines()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, customerId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // Gerçek fatura: 1200 adet × 47,53 = 57.036,00 + %20 = 68.443,20
        var response = await client.PostAsJsonAsync("/api/sales-invoices",
            BuildPayload(companyId, customerId, projectId,
                quantity: 1200m, unitPrice: 47.53m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(57_036.00m, payload.GetProperty("subtotal").GetDecimal());
        Assert.Equal(11_407.20m, payload.GetProperty("vatTotal").GetDecimal());
        Assert.Equal(68_443.20m, payload.GetProperty("grandTotal").GetDecimal());
        Assert.Equal(68_443.20m, payload.GetProperty("netReceivableAmount").GetDecimal());
        Assert.StartsWith("SAT-", payload.GetProperty("internalNumber").GetString());
    }

    [Fact]
    public async Task Post_CreatesBalancedRevenueVoucher()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, customerId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var create = await client.PostAsJsonAsync("/api/sales-invoices",
            BuildPayload(companyId, customerId, projectId,
                quantity: 1200m, unitPrice: 47.53m));
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var post = await client.PostAsync($"/api/sales-invoices/{id}/post", null);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        var voucher = await LoadVoucherAsync(id);

        Assert.Equal(AccountingVoucherStatus.Posted, voucher.Status);
        // Hakediş fişiyle karışmamalı — kaynak modülü ayrı.
        Assert.Equal("SalesInvoice", voucher.SourceModule);
        Assert.Equal(id, voucher.SourceEntityId);

        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);
        Assert.Equal(68_443.20m, voucher.TotalDebit);

        var receivable = voucher.Lines.Single(x => x.AccountingAccount.Code == "120");
        Assert.Equal(68_443.20m, receivable.DebitAmount);
        Assert.Equal(customerId, receivable.CurrentAccountId);
        Assert.Equal(projectId, receivable.ProjectId);

        Assert.Equal(57_036.00m,
            voucher.Lines.Single(x => x.AccountingAccount.Code == "600.03").CreditAmount);
        Assert.Equal(11_407.20m,
            voucher.Lines.Single(x => x.AccountingAccount.Code == "391.09").CreditAmount);
    }

    [Fact]
    public async Task Post_WithWithholding_OnlyDeclaresNonWithheldVat()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, customerId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // 100.000 + 20.000 KDV, 4/10 tevkifat = 8.000 alıcıda kalır
        // → beyan edilen KDV 12.000, tahsil edilecek 112.000
        var create = await client.PostAsJsonAsync("/api/sales-invoices",
            BuildPayload(companyId, customerId, projectId,
                quantity: 100m, unitPrice: 1_000m, withholdingAmount: 8_000m));
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/sales-invoices/{id}/post", null)).StatusCode);

        var voucher = await LoadVoucherAsync(id);

        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);
        Assert.Equal(112_000m, voucher.TotalDebit);
        Assert.Equal(112_000m,
            voucher.Lines.Single(x => x.AccountingAccount.Code == "120").DebitAmount);
        Assert.Equal(100_000m,
            voucher.Lines.Single(x => x.AccountingAccount.Code == "600.03").CreditAmount);
        Assert.Equal(12_000m,
            voucher.Lines.Single(x => x.AccountingAccount.Code == "391.09").CreditAmount);
    }

    [Fact]
    public async Task Create_WithholdingAboveVat_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, customerId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // Tevkifat KDV'nin bir kısmıdır; KDV'yi aşan tevkifat veri hatasıdır.
        var response = await client.PostAsJsonAsync("/api/sales-invoices",
            BuildPayload(companyId, customerId, projectId,
                quantity: 10m, unitPrice: 100m, withholdingAmount: 500m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateOfficialNumberForSameCustomer_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, customerId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var number = $"ENE2026{suffix}";

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/sales-invoices",
                BuildPayload(companyId, customerId, projectId, 1m, 100m,
                    officialInvoiceNumber: number))).StatusCode);

        var second = await client.PostAsJsonAsync("/api/sales-invoices",
            BuildPayload(companyId, customerId, projectId, 1m, 100m,
                officialInvoiceNumber: number));

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Post_WithoutOfficialNumber_IsBlocked()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, customerId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var create = await client.PostAsJsonAsync("/api/sales-invoices", new
        {
            companyId,
            customerCurrentAccountId = customerId,
            projectId,
            officialInvoiceNumber = (string?)null,
            invoiceDate = DateTime.UtcNow.Date,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            withholdingAmount = 0m,
            description = "Numarasız taslak",
            notes = (string?)null,
            items = new[]
            {
                new { description = "Kalem", quantity = 1m, unit = "adet", unitPrice = 100m, vatRate = 20m }
            }
        });

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        // Resmi numara olmadan muhasebeye geçmemeli.
        var post = await client.PostAsync($"/api/sales-invoices/{id}/post", null);
        Assert.Equal(HttpStatusCode.Conflict, post.StatusCode);
    }

    [Fact]
    public async Task Post_PostedInvoice_CannotBeCancelled()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, customerId, projectId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var create = await client.PostAsJsonAsync("/api/sales-invoices",
            BuildPayload(companyId, customerId, projectId, 10m, 100m));
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/sales-invoices/{id}/post", null)).StatusCode);

        var cancel = await client.PostAsJsonAsync($"/api/sales-invoices/{id}/cancel",
            new { reason = "Yanlış müşteri" });

        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
    }
}
