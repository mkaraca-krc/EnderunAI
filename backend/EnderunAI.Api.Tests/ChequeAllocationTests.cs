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
/// Çekte masraf merkezi (Merkez) ve çek dağılımı.
///
/// Buradaki asıl güvence fişin dengesi ve payların doğru masraf
/// merkezine düşmesi: dağılım yanlış bölünürse fiş dengesiz kalır ya da
/// bir şantiyenin maliyeti başka şantiyeye yazılır — ikisi de defteri
/// sessizce bozar.
/// </summary>
[Collection("Integration")]
public sealed class ChequeAllocationTests(DatabaseFixture fixture)
{
    private sealed record TestContext(
        Guid CompanyId,
        Guid ProjectId,
        Guid SecondProjectId,
        string ProjectCode,
        string SecondProjectCode,
        Guid SupplierId);

    private static async Task SeedChartOfAccountsAsync(AppDbContext db, Guid companyId)
    {
        db.AccountingAccounts.AddRange(
            new AccountingAccount
            {
                CompanyId = companyId, Code = "103.01", Name = "Verilen Çekler",
                Nature = AccountingAccountNature.Credit, Level = 4, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "320", Name = "Satıcılar",
                Nature = AccountingAccountNature.Credit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "191.01.03", Name = "İndirilecek KDV",
                Nature = AccountingAccountNature.Debit, Level = 5, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "740", Name = "Hizmet Üretim Maliyeti",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "153", Name = "Ticari Mallar",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            });

        await db.SaveChangesAsync();
    }

    private async Task<TestContext> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        await SeedChartOfAccountsAsync(db, project.CompanyId);

        var second = await TestDataFactory.CreateProjectAsync(db, $"{suffix}b");

        // İki proje aynı şirkette olmalı: dağılım şirket içi kontrol eder.
        second.CompanyId = project.CompanyId;

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
            project.CompanyId, project.Id, second.Id,
            project.Code, second.Code, supplier.Id);
    }

    private static object BuildChequePayload(
        TestContext context,
        decimal amount,
        Guid? projectId,
        string? costCenterCode = null,
        object[]? allocations = null) => new
        {
            companyId = context.CompanyId,
            direction = (int)ChequeDirection.Issued,
            chequeNumber = $"CK{Guid.NewGuid():N}"[..10],
            bankName = "Test Bankası",
            bankBranch = "Merkez",
            drawer = "Enderun",
            currentAccountId = context.SupplierId,
            projectId,
            amount,
            currencyCode = "TRY",
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddDays(60),
            progressPaymentId = (Guid?)null,
            supplierInvoiceId = (Guid?)null,
            description = "Test çeki",
            costCenterCode,
            allocations
        };

    private async Task<Guid> CreateSupplierInvoiceAsync(
        TestContext context, Guid? projectId, decimal grandTotal, string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var subtotal = decimal.Round(grandTotal / 1.20m, 2);

        var invoice = new SupplierInvoice
        {
            CompanyId = context.CompanyId,
            SupplierCurrentAccountId = context.SupplierId,
            ProjectId = projectId,
            InternalNumber = $"SFT-{suffix}",
            InvoiceNumber = $"FTR-{suffix}",
            InvoiceDate = DateTime.UtcNow.Date,
            CurrencyCode = "TRY",
            ExchangeRate = 1m,
            Subtotal = subtotal,
            VatTotal = grandTotal - subtotal,
            GrandTotal = grandTotal,
            Status = SupplierInvoiceStatus.Approved
        };

        db.SupplierInvoices.Add(invoice);
        await db.SaveChangesAsync();

        return invoice.Id;
    }

    private async Task<(decimal Debit, decimal Credit, List<(string Account, decimal Debit,
        string? CostCenter, Guid? ProjectId)> Lines)> LoadEntryVoucherAsync(Guid chequeId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var voucher = await db.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .Where(x => x.SourceModule == "Cheque" &&
                        x.SourceEntityId == chequeId &&
                        x.Status == AccountingVoucherStatus.Posted)
            .SingleAsync();

        return (
            voucher.TotalDebit,
            voucher.TotalCredit,
            voucher.Lines
                .Select(x => (x.AccountingAccount.Code, x.DebitAmount, x.CostCenterCode, x.ProjectId))
                .ToList());
    }

    /// <summary>
    /// Ofis kirası çekinin projesi yoktur ama Merkez'e yazılabilmelidir;
    /// masraf merkezi boş kalsaydı gider muhasebede hangi birime ait
    /// olduğu belirsiz dururdu.
    /// </summary>
    [Fact]
    public async Task Cheque_WithHeadOfficeCostCenter_PostsVoucherToThatCostCenter()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, 30_000m, projectId: null, costCenterCode: "MERKEZ"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var chequeId = payload.GetProperty("id").GetGuid();

        Assert.Equal("MERKEZ", payload.GetProperty("costCenterCode").GetString());

        var voucher = await LoadEntryVoucherAsync(chequeId);

        Assert.Equal(voucher.Debit, voucher.Credit);
        Assert.All(voucher.Lines, line => Assert.Equal("MERKEZ", line.CostCenter));
    }

    /// <summary>
    /// Masraf merkezi seçilmemişse proje kodu kullanılır — eski çekler
    /// bu yüzden bozulmaz.
    /// </summary>
    [Fact]
    public async Task Cheque_WithoutCostCenter_FallsBackToProjectCode()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, 30_000m, projectId: context.ProjectId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var chequeId = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var voucher = await LoadEntryVoucherAsync(chequeId);

        Assert.All(voucher.Lines,
            line => Assert.Equal(context.ProjectCode, line.CostCenter));
    }

    /// <summary>
    /// Elle dağılım: 100.000 TL çek 60.000 + 40.000 olarak iki projeye
    /// bölünür. Fiş dengesi korunur, cari tarafı ikiye ayrılır, çek
    /// hesabı (103) tek satır kalır — çek bir enstrümandır, projesi yok.
    /// </summary>
    [Fact]
    public async Task Cheque_WithManualAllocation_SplitsCounterpartySideOnly()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, 100_000m, projectId: null, allocations:
            [
                new { amount = 60_000m, projectId = context.ProjectId },
                new { amount = 40_000m, projectId = context.SecondProjectId }
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var chequeId = payload.GetProperty("id").GetGuid();

        Assert.Equal(2, payload.GetProperty("allocations").GetArrayLength());

        var voucher = await LoadEntryVoucherAsync(chequeId);

        Assert.Equal(voucher.Debit, voucher.Credit);
        Assert.Equal(100_000m, voucher.Debit);

        var payableLines = voucher.Lines.Where(x => x.Account == "320").ToList();
        var chequeLines = voucher.Lines.Where(x => x.Account == "103.01").ToList();

        Assert.Equal(2, payableLines.Count);
        Assert.Single(chequeLines);
        Assert.Equal(100_000m, chequeLines[0].Debit == 0m ? 100_000m : chequeLines[0].Debit);

        Assert.Contains(payableLines, x =>
            x.Debit == 60_000m && x.CostCenter == context.ProjectCode);
        Assert.Contains(payableLines, x =>
            x.Debit == 40_000m && x.CostCenter == context.SecondProjectCode);
    }

    /// <summary>
    /// Dağılım toplamı çek tutarını tutmuyorsa fiş dengesiz çıkardı;
    /// kayıt en baştan reddedilmeli.
    /// </summary>
    [Fact]
    public async Task Cheque_WithAllocationSumMismatch_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, 100_000m, projectId: null, allocations:
            [
                new { amount = 60_000m, projectId = context.ProjectId },
                new { amount = 30_000m, projectId = context.SecondProjectId }
            ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Dağılım toplamı",
            await response.Content.ReadAsStringAsync());

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Reddedilen çek hiç kaydedilmemeli; yarım kayıt kalmamalı.
        Assert.Equal(0, await db.Cheques.CountAsync(x => x.CompanyId == context.CompanyId));
    }

    /// <summary>
    /// Fatura bağlantılı dağılımda proje ve masraf merkezi FATURADAN
    /// gelir. İstemci başka bir proje gönderse bile yok sayılır: aynı
    /// ödeme iki farklı projeye yazılamaz.
    /// </summary>
    [Fact]
    public async Task Cheque_AllocatedToInvoices_DerivesCostCentersFromInvoices()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var firstInvoiceId = await CreateSupplierInvoiceAsync(
            context, context.ProjectId, 60_000m, $"{suffix}-1");
        var secondInvoiceId = await CreateSupplierInvoiceAsync(
            context, context.SecondProjectId, 40_000m, $"{suffix}-2");

        var response = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, 100_000m, projectId: null, allocations:
            [
                new
                {
                    amount = 60_000m,
                    supplierInvoiceId = firstInvoiceId,
                    // Kasıtlı olarak YANLIŞ proje gönderiliyor; faturadaki
                    // proje kazanmalı.
                    projectId = context.SecondProjectId
                },
                new { amount = 40_000m, supplierInvoiceId = secondInvoiceId }
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var chequeId = payload.GetProperty("id").GetGuid();

        var voucher = await LoadEntryVoucherAsync(chequeId);
        var payableLines = voucher.Lines.Where(x => x.Account == "320").ToList();

        Assert.Equal(voucher.Debit, voucher.Credit);
        Assert.Contains(payableLines, x =>
            x.Debit == 60_000m && x.ProjectId == context.ProjectId);
        Assert.Contains(payableLines, x =>
            x.Debit == 40_000m && x.ProjectId == context.SecondProjectId);

        // Fatura ekranı "bu faturayı hangi çek ödedi" sorusunu aynı
        // kayıttan cevaplamalı.
        var invoice = await client.GetFromJsonAsync<JsonElement>(
            $"/api/supplier-invoices/{firstInvoiceId}");

        Assert.Equal(60_000m, invoice.GetProperty("chequeAllocatedAmount").GetDecimal());
        Assert.Equal(0m, invoice.GetProperty("chequeRemainingAmount").GetDecimal());
        Assert.Single(invoice.GetProperty("chequePayments").EnumerateArray());
    }

    /// <summary>
    /// Bir faturaya bağlanan toplam ödeme fatura tutarını aşamaz; aşarsa
    /// fatura fazla ödenmiş görünür ve cari kapatma yanlış olur.
    /// </summary>
    [Fact]
    public async Task Cheque_AllocatedAboveInvoiceTotal_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var invoiceId = await CreateSupplierInvoiceAsync(
            context, context.ProjectId, 50_000m, suffix);

        var response = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, 60_000m, projectId: null, allocations:
            [
                new { amount = 60_000m, supplierInvoiceId = invoiceId }
            ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("fatura tutarını", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Verilen çeke satış faturası bağlanamaz: yön karışırsa alacak ve
    /// borç birbirine geçer.
    /// </summary>
    [Fact]
    public async Task Cheque_AllocatedToWrongInvoiceDirection_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Guid salesInvoiceId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var salesInvoice = new SalesInvoice
            {
                CompanyId = context.CompanyId,
                CustomerCurrentAccountId = context.SupplierId,
                InternalNumber = $"SAT-{suffix}",
                InvoiceDate = DateTime.UtcNow.Date,
                CurrencyCode = "TRY",
                ExchangeRate = 1m,
                Subtotal = 10_000m,
                VatTotal = 2_000m,
                GrandTotal = 12_000m,
                NetReceivableAmount = 12_000m
            };

            db.SalesInvoices.Add(salesInvoice);
            await db.SaveChangesAsync();
            salesInvoiceId = salesInvoice.Id;
        }

        var response = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, 12_000m, projectId: null, allocations:
            [
                new { amount = 12_000m, salesInvoiceId }
            ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("yalnızca alınan çeke", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Dağılım değiştirilince giriş fişi yeniden kesilir: eskisi
    /// SİLİNMEZ, iptal edilir — muhasebede yazılan fişin izi kalmalı.
    /// </summary>
    [Fact]
    public async Task ReplaceAllocations_CancelsOldVoucherAndPostsNewSplit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var created = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, 100_000m, projectId: context.ProjectId));

        var chequeId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/cheques/{chequeId}/allocations",
            new
            {
                allocations = new object[]
                {
                    new { amount = 70_000m, projectId = context.ProjectId },
                    new { amount = 30_000m, costCenterCode = "MERKEZ" }
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, payload.GetProperty("allocations").GetArrayLength());

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var vouchers = await db.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .Where(x => x.SourceModule == "Cheque" && x.SourceEntityId == chequeId)
            .ToListAsync();

        Assert.Equal(2, vouchers.Count);
        Assert.Single(vouchers, x => x.Status == AccountingVoucherStatus.Cancelled);

        var posted = vouchers.Single(x => x.Status == AccountingVoucherStatus.Posted);

        Assert.Equal(posted.TotalDebit, posted.TotalCredit);

        var payableLines = posted.Lines
            .Where(x => x.AccountingAccount.Code == "320")
            .ToList();

        Assert.Equal(2, payableLines.Count);
        Assert.Contains(payableLines, x => x.DebitAmount == 30_000m && x.CostCenterCode == "MERKEZ");

        // Hareket satırı yeni fişi göstermeli; eski fişe bakan bir kayıt
        // kalırsa iptal edilmiş fişe bağlı çek görünürdü.
        var movement = await db.ChequeMovements
            .SingleAsync(x => x.ChequeId == chequeId && x.FromStatus == null);

        Assert.Equal(posted.Id, movement.AccountingVoucherId);
    }

    /// <summary>
    /// Çek işlem gördükten sonra dağılım değiştirilemez: sonraki fişler
    /// ilk dağılıma göre kesilmiştir.
    /// </summary>
    [Fact]
    public async Task ReplaceAllocations_AfterStatusChange_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var created = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, 100_000m, projectId: context.ProjectId));

        var chequeId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var statusResponse = await client.PostAsJsonAsync(
            $"/api/cheques/{chequeId}/status",
            new
            {
                toStatus = (int)ChequeStatus.Returned,
                movementDate = DateTime.UtcNow.Date,
                cashAccountId = (Guid?)null,
                description = "Tedarikçiden geri alındı"
            });

        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

        var response = await client.PutAsJsonAsync(
            $"/api/cheques/{chequeId}/allocations",
            new
            {
                allocations = new object[]
                {
                    new { amount = 100_000m, costCenterCode = "MERKEZ" }
                }
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("işlem gördüğü için", await response.Content.ReadAsStringAsync());
    }
}
