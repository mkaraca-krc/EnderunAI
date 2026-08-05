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

[Collection("Integration")]
public sealed class SupplierInvoiceTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Otomatik fiş için gereken asgari hesap planını kurar: maliyet
    /// (740), indirilecek KDV (191.01.03) ve satıcılar (320).
    /// </summary>
    private static async Task SeedChartOfAccountsAsync(AppDbContext db, Guid companyId)
    {
        var accounts = new[]
        {
            new AccountingAccount
            {
                CompanyId = companyId, Code = "740", Name = "Hizmet Üretim Maliyeti",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "191.01.03", Name = "% 20 İndirilecek KDV",
                Nature = AccountingAccountNature.Debit, Level = 5, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "320", Name = "Satıcılar",
                Nature = AccountingAccountNature.Credit, Level = 3, IsPostingAllowed = true
            }
        };

        db.AccountingAccounts.AddRange(accounts);
        await db.SaveChangesAsync();
    }

    private async Task<(Guid CompanyId, Guid SupplierId, Guid ProjectId)> CreateInvoiceContextAsync(
        string suffix)
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

        return (project.CompanyId, supplier.Id, project.Id);
    }

    private static object BuildInvoicePayload(
        Guid companyId, Guid supplierId, Guid projectId,
        decimal quantity, decimal unitPrice, decimal vatRate = 20m,
        Guid? purchaseOrderId = null) => new
        {
            companyId,
            supplierCurrentAccountId = supplierId,
            projectId,
            purchaseOrderId,
            goodsReceiptId = (Guid?)null,
            invoiceNumber = $"FTR-{Guid.NewGuid():N}"[..12],
            invoiceDate = DateTime.UtcNow.Date,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            description = "Test faturası",
            items = new[]
            {
                new
                {
                    description = "Test malzeme",
                    quantity,
                    unit = "adet",
                    unitPrice,
                    vatRate,
                    purchaseOrderItemId = (Guid?)null
                }
            }
        };

    [Fact]
    public async Task Create_ComputesLineAndHeaderTotalsWithVat()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, supplierId, projectId) = await CreateInvoiceContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/supplier-invoices",
            BuildInvoicePayload(companyId, supplierId, projectId, quantity: 10m, unitPrice: 100m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1000m, payload.GetProperty("subtotal").GetDecimal());
        Assert.Equal(200m, payload.GetProperty("vatTotal").GetDecimal());
        Assert.Equal(1200m, payload.GetProperty("grandTotal").GetDecimal());
        Assert.Equal(0, payload.GetProperty("status").GetInt32()); // Draft
    }

    [Fact]
    public async Task Approve_CreatesBalancedPostedVoucherWithSourceModule()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, supplierId, projectId) = await CreateInvoiceContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsJsonAsync("/api/supplier-invoices",
            BuildInvoicePayload(companyId, supplierId, projectId, quantity: 5m, unitPrice: 200m));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var invoiceId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var submitResponse = await client.PostAsync($"/api/supplier-invoices/{invoiceId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var approveResponse = await client.PostAsync($"/api/supplier-invoices/{invoiceId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var invoice = await db.SupplierInvoices
            .AsNoTracking()
            .SingleAsync(x => x.Id == invoiceId);

        Assert.Equal(SupplierInvoiceStatus.Approved, invoice.Status);
        Assert.NotNull(invoice.AccountingVoucherId);

        var voucher = await db.AccountingVouchers
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == invoice.AccountingVoucherId!.Value);

        // Fiş doğrudan Posted olarak düşer ve dengelidir.
        Assert.Equal(AccountingVoucherStatus.Posted, voucher.Status);
        Assert.Equal("SupplierInvoice", voucher.SourceModule);
        Assert.Equal(invoiceId, voucher.SourceEntityId);
        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);
        Assert.Equal(1200m, voucher.TotalDebit); // 1000 maliyet + 200 KDV
        Assert.Equal(3, voucher.Lines.Count);

        // 320 Satıcılar alacak satırı cari boyutunu taşımalı.
        var payableLine = voucher.Lines.Single(x => x.CreditAmount > 0);
        Assert.Equal(1200m, payableLine.CreditAmount);
        Assert.Equal(supplierId, payableLine.CurrentAccountId);
    }

    [Fact]
    public async Task Approve_AlsoWritesProjectCostTransaction()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, supplierId, projectId) = await CreateInvoiceContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsJsonAsync("/api/supplier-invoices",
            BuildInvoicePayload(companyId, supplierId, projectId, quantity: 2m, unitPrice: 750m));
        var invoiceId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await client.PostAsync($"/api/supplier-invoices/{invoiceId}/submit", null);
        await client.PostAsync($"/api/supplier-invoices/{invoiceId}/approve", null);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cost = await db.ProjectCostTransactions
            .AsNoTracking()
            .SingleAsync(x => x.ReferenceType == "SupplierInvoice" && x.ReferenceId == invoiceId);

        Assert.Equal(projectId, cost.ProjectId);
        Assert.Equal(1500m, cost.Amount); // KDV hariç ara toplam
    }

    /// <summary>
    /// Faz C: proje maliyet kaydı, muhasebedeki maliyet satırına
    /// bağlanmalı ki iki tarafta iki ayrı "doğru" rakam oluşmasın.
    /// </summary>
    [Fact]
    public async Task Approve_LinksProjectCostToAccountingLine_AndReconciles()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, supplierId, projectId) = await CreateInvoiceContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsJsonAsync("/api/supplier-invoices",
            BuildInvoicePayload(companyId, supplierId, projectId, quantity: 4m, unitPrice: 250m));
        var invoiceId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await client.PostAsync($"/api/supplier-invoices/{invoiceId}/submit", null);
        await client.PostAsync($"/api/supplier-invoices/{invoiceId}/approve", null);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var cost = await db.ProjectCostTransactions
                .AsNoTracking()
                .SingleAsync(x => x.ReferenceType == "SupplierInvoice" && x.ReferenceId == invoiceId);

            Assert.NotNull(cost.AccountingVoucherLineId);

            // Bağlanan satır gerçekten maliyet hesabının borç satırı olmalı
            // ve tutarı proje maliyetiyle birebir aynı olmalı.
            var line = await db.AccountingVoucherLines
                .AsNoTracking()
                .Include(x => x.AccountingAccount)
                .SingleAsync(x => x.Id == cost.AccountingVoucherLineId!.Value);

            Assert.Equal("740", line.AccountingAccount.Code);
            Assert.Equal(cost.Amount, line.DebitAmountLocal);
        }

        var reconciliation = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/cost-reconciliation");

        Assert.True(reconciliation.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(1_000m, reconciliation.GetProperty("projectCostTotal").GetDecimal());
        Assert.Equal(1_000m, reconciliation.GetProperty("accountingTotal").GetDecimal());
        Assert.Equal(0, reconciliation.GetProperty("unlinkedCosts").GetArrayLength());
        Assert.Equal(0, reconciliation.GetProperty("unlinkedAccountingLines").GetArrayLength());
    }

    /// <summary>
    /// Elle girilen (muhasebeleşmemiş) proje maliyeti mutabakatsızlık
    /// olarak raporlanmalı — sessizce eşit sayılmamalı.
    /// </summary>
    [Fact]
    public async Task CostReconciliation_FlagsManualCostNotInAccounting()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (_, _, projectId) = await CreateInvoiceContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ProjectCostTransactions.Add(new ProjectCostTransaction
            {
                ProjectId = projectId,
                CostType = ProjectCostType.Other,
                CostDate = DateTime.UtcNow,
                Amount = 3_000m,
                Description = "Elle girilen maliyet"
            });
            await db.SaveChangesAsync();
        }

        var reconciliation = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/cost-reconciliation");

        Assert.False(reconciliation.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(3_000m, reconciliation.GetProperty("difference").GetDecimal());
        Assert.Equal(3_000m, reconciliation.GetProperty("unlinkedCostTotal").GetDecimal());
        Assert.Equal(1, reconciliation.GetProperty("unlinkedCosts").GetArrayLength());
    }

    [Fact]
    public async Task Submit_WithoutPurchaseOrder_MarksMatchNotApplicable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, supplierId, projectId) = await CreateInvoiceContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsJsonAsync("/api/supplier-invoices",
            BuildInvoicePayload(companyId, supplierId, projectId, quantity: 1m, unitPrice: 500m));
        var invoiceId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await client.PostAsync($"/api/supplier-invoices/{invoiceId}/submit", null);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/supplier-invoices/{invoiceId}");

        Assert.Equal(0, detail.GetProperty("matchStatus").GetInt32()); // NotApplicable
        Assert.False(detail.GetProperty("requiresGmApproval").GetBoolean());
    }

    [Fact]
    public async Task Submit_AmountAboveGmThreshold_RequiresGmApproval()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, supplierId, projectId) = await CreateInvoiceContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CompanyFinanceSettings.Add(new CompanyFinanceSettings
            {
                CompanyId = companyId,
                GmApprovalThresholdTry = 1_000m,
                ExpenseAccountId = await db.AccountingAccounts
                    .Where(x => x.CompanyId == companyId && x.Code == "740")
                    .Select(x => (Guid?)x.Id).SingleAsync(),
                VatInAccountId = await db.AccountingAccounts
                    .Where(x => x.CompanyId == companyId && x.Code == "191.01.03")
                    .Select(x => (Guid?)x.Id).SingleAsync(),
                PayablesAccountId = await db.AccountingAccounts
                    .Where(x => x.CompanyId == companyId && x.Code == "320")
                    .Select(x => (Guid?)x.Id).SingleAsync()
            });
            await db.SaveChangesAsync();
        }

        var createResponse = await client.PostAsJsonAsync("/api/supplier-invoices",
            BuildInvoicePayload(companyId, supplierId, projectId, quantity: 10m, unitPrice: 500m));
        var invoiceId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var submitResponse = await client.PostAsync($"/api/supplier-invoices/{invoiceId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/supplier-invoices/{invoiceId}");
        Assert.True(detail.GetProperty("requiresGmApproval").GetBoolean());

        // Admin rolündeki test kullanıcısı GM eşiğini aşan faturayı onaylayabilir.
        var approveResponse = await client.PostAsync($"/api/supplier-invoices/{invoiceId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
    }

    [Fact]
    public async Task Create_RejectsInvoiceWithoutItems()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, supplierId, projectId) = await CreateInvoiceContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/supplier-invoices", new
        {
            companyId,
            supplierCurrentAccountId = supplierId,
            projectId,
            purchaseOrderId = (Guid?)null,
            goodsReceiptId = (Guid?)null,
            invoiceNumber = "FTR-BOS",
            invoiceDate = DateTime.UtcNow.Date,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            description = (string?)null,
            items = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_AfterApproval_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, supplierId, projectId) = await CreateInvoiceContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsJsonAsync("/api/supplier-invoices",
            BuildInvoicePayload(companyId, supplierId, projectId, quantity: 1m, unitPrice: 100m));
        var invoiceId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await client.PostAsync($"/api/supplier-invoices/{invoiceId}/submit", null);
        await client.PostAsync($"/api/supplier-invoices/{invoiceId}/approve", null);

        var updateResponse = await client.PutAsJsonAsync($"/api/supplier-invoices/{invoiceId}", new
        {
            invoiceNumber = "FTR-DEGISTI",
            invoiceDate = DateTime.UtcNow.Date,
            dueDate = (DateTime?)null,
            currencyCode = "TRY",
            exchangeRate = 1m,
            description = (string?)null,
            items = new[]
            {
                new
                {
                    description = "Değişiklik denemesi",
                    quantity = 1m,
                    unit = "adet",
                    unitPrice = 999m,
                    vatRate = 20m,
                    purchaseOrderItemId = (Guid?)null
                }
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode);
    }

    [Fact]
    public async Task FinanceSettings_PutRejectsGroupAccount()
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var current = await client.GetFromJsonAsync<JsonElement>("/api/company-settings/finance-settings");
        Assert.True(current.TryGetProperty("gmApprovalThresholdTry", out _));

        Guid groupAccountId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var companyId = await db.Companies
                .OrderBy(x => x.CreatedAtUtc)
                .Select(x => x.Id)
                .FirstAsync();

            var groupAccount = new AccountingAccount
            {
                CompanyId = companyId,
                Code = $"GRP-{Guid.NewGuid():N}"[..12],
                Name = "Grup hesabı",
                Nature = AccountingAccountNature.Debit,
                Level = 1,
                IsPostingAllowed = false
            };
            db.AccountingAccounts.Add(groupAccount);
            await db.SaveChangesAsync();
            groupAccountId = groupAccount.Id;
        }

        var response = await client.PutAsJsonAsync("/api/company-settings/finance-settings", new
        {
            gmApprovalThresholdTry = 50_000m,
            threeWayTolerancePercent = 2m,
            defaultVatRate = 20m,
            vatInAccountId = (Guid?)null,
            vatOutAccountId = (Guid?)null,
            salesAccountId = (Guid?)null,
            expenseAccountId = groupAccountId,
            payablesAccountId = (Guid?)null,
            receivablesAccountId = (Guid?)null,
            factoringExpenseAccountId = (Guid?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Stok hesabı ayarlardan seçilebilmeli: seçilemezse alış faturası
    /// her şirkette 740 maliyete düşer ve depodaki mal bilançoya girmez.
    /// </summary>
    [Fact]
    public async Task FinanceSettings_PersistsInventoryAccount()
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var current = await client.GetFromJsonAsync<JsonElement>(
            "/api/company-settings/finance-settings");

        Guid inventoryAccountId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var companyId = await db.Companies
                .OrderBy(x => x.CreatedAtUtc)
                .Select(x => x.Id)
                .FirstAsync();

            var account = new AccountingAccount
            {
                CompanyId = companyId,
                Code = $"153-{Guid.NewGuid():N}"[..12],
                Name = "Ticari Mallar",
                Nature = AccountingAccountNature.Debit,
                Level = 3,
                IsPostingAllowed = true
            };
            db.AccountingAccounts.Add(account);
            await db.SaveChangesAsync();
            inventoryAccountId = account.Id;
        }

        var response = await client.PutAsJsonAsync("/api/company-settings/finance-settings", new
        {
            gmApprovalThresholdTry = current.GetProperty("gmApprovalThresholdTry").GetDecimal(),
            threeWayTolerancePercent = current.GetProperty("threeWayTolerancePercent").GetDecimal(),
            defaultVatRate = current.GetProperty("defaultVatRate").GetDecimal(),
            vatInAccountId = (Guid?)null,
            vatOutAccountId = (Guid?)null,
            salesAccountId = (Guid?)null,
            expenseAccountId = (Guid?)null,
            inventoryAccountId,
            payablesAccountId = (Guid?)null,
            receivablesAccountId = (Guid?)null,
            factoringExpenseAccountId = (Guid?)null,
            deductionAccountId = (Guid?)null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var saved = await client.GetFromJsonAsync<JsonElement>(
            "/api/company-settings/finance-settings");

        Assert.Equal(inventoryAccountId,
            saved.GetProperty("inventoryAccountId").GetGuid());
    }
}
