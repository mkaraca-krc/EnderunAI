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
/// Alış tevkifatının muhasebeleşmesi ve dönem sonu KDV tahakkuku.
///
/// Tevkifat işlenmezse iki şey birden bozulur: tedarikçiye borcumuz
/// tevkifat kadar fazla görünür (o tutarı ona ödemeyeceğiz) ve vergi
/// dairesine olan yükümlülük hiç doğmaz.
/// </summary>
[Collection("Integration")]
public sealed class VatAccrualTests(DatabaseFixture fixture)
{
    private sealed record TestContext(Guid CompanyId, Guid SupplierId, Guid ProjectId);

    private static readonly (string Code, string Name, AccountingAccountNature Nature)[]
        Accounts =
        [
            ("191.01.03", "% 20 İNDİRİLECEK KDV", AccountingAccountNature.Debit),
            ("191.05", "SORUMLU SIFATIYLA BEYAN EDİLEN KDV", AccountingAccountNature.Debit),
            ("391.09", "% 20 HESAPLANAN KDV", AccountingAccountNature.Credit),
            ("360.002", "SORUMLU SIFATIYLA ÖDENECEK KDV", AccountingAccountNature.Credit),
            ("360.99", "ÖDENECEK KDV", AccountingAccountNature.Credit),
            ("190.01", "DEVREDEN KDV", AccountingAccountNature.Debit),
            ("600.03", "YURTİÇİ SATIŞLAR", AccountingAccountNature.Credit),
            ("740", "HİZMET ÜRETİM MALİYETİ", AccountingAccountNature.Debit),
            ("120", "ALICILAR", AccountingAccountNature.Debit),
            ("320", "SATICILAR", AccountingAccountNature.Credit)
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

        var supplier = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"TED-{suffix}",
            Title = $"Tevkifatlı Tedarikçi {suffix}",
            Roles = CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };

        db.CurrentAccounts.Add(supplier);
        await db.SaveChangesAsync();

        return new TestContext(project.CompanyId, supplier.Id, project.Id);
    }

    /// <summary>
    /// Tevkifatlı alış faturası: KDV'nin tevkifat kısmı 191.05/360.002
    /// çiftine, tedarikçiye kalan borç tevkifat düşülmüş tutara yazılır.
    /// </summary>
    [Fact]
    public async Task WithholdingPurchaseInvoice_PostsReverseChargeAndReducesSupplierBalance()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // 100.000 + %20 KDV 20.000 = 120.000; tevkifat 4/10 = 8.000.
        Guid invoiceId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var invoice = new SupplierInvoice
            {
                CompanyId = context.CompanyId,
                SupplierCurrentAccountId = context.SupplierId,
                ProjectId = context.ProjectId,
                InvoiceType = SupplierInvoiceType.Expense,
                InternalNumber = $"SFT-{suffix}",
                InvoiceNumber = $"TVK-{suffix}",
                InvoiceDate = DateTime.SpecifyKind(new DateTime(2026, 5, 10), DateTimeKind.Utc),
                CurrencyCode = "TRY",
                ExchangeRate = 1m,
                Subtotal = 100_000m,
                VatTotal = 20_000m,
                GrandTotal = 120_000m,
                WithholdingAmount = 8_000m,
                Status = SupplierInvoiceStatus.PendingApproval
            };

            var expenseAccountId = await db.AccountingAccounts
                .Where(x => x.CompanyId == context.CompanyId && x.Code == "740")
                .Select(x => x.Id)
                .SingleAsync();

            invoice.Items.Add(new SupplierInvoiceItem
            {
                LineNumber = 1,
                Description = "Taşeron hizmeti",
                Quantity = 1m,
                Unit = "adet",
                UnitPrice = 100_000m,
                VatRate = 20m,
                LineSubtotal = 100_000m,
                VatAmount = 20_000m,
                LineTotal = 120_000m,
                ExpenseAccountId = expenseAccountId
            });

            db.SupplierInvoices.Add(invoice);
            await db.SaveChangesAsync();
            invoiceId = invoice.Id;
        }

        var approve = await client.PostAsync(
            $"/api/supplier-invoices/{invoiceId}/approve", null);

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var voucher = await verifyDb.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.SourceModule == "SupplierInvoice" &&
                              x.SourceEntityId == invoiceId);

        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);

        decimal Net(string code) => voucher.Lines
            .Where(x => x.AccountingAccount.Code == code)
            .Sum(x => x.DebitAmount - x.CreditAmount);

        // İndirilecek KDV yalnızca tevkifatsız kısım.
        Assert.Equal(12_000m, Net("191.01.03"));
        // Tevkifat kısmı sorumlu sıfatıyla beyan edilir ve ödenir.
        Assert.Equal(8_000m, Net("191.05"));
        Assert.Equal(-8_000m, Net("360.002"));
        // Tedarikçiye borç: 120.000 − 8.000.
        Assert.Equal(-112_000m, Net("320"));
    }

    /// <summary>
    /// Tevkifat KDV'den büyük olamaz; olursa fiş sessizce dengesiz
    /// kalmak yerine anlaşılır bir hatayla durur.
    /// </summary>
    [Fact]
    public async Task WithholdingGreaterThanVat_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Guid invoiceId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var invoice = new SupplierInvoice
            {
                CompanyId = context.CompanyId,
                SupplierCurrentAccountId = context.SupplierId,
                ProjectId = context.ProjectId,
                InvoiceType = SupplierInvoiceType.Expense,
                InternalNumber = $"SFT2-{suffix}",
                InvoiceNumber = $"TVK2-{suffix}",
                InvoiceDate = DateTime.SpecifyKind(new DateTime(2026, 5, 12), DateTimeKind.Utc),
                CurrencyCode = "TRY",
                ExchangeRate = 1m,
                Subtotal = 10_000m,
                VatTotal = 2_000m,
                GrandTotal = 12_000m,
                WithholdingAmount = 5_000m,
                Status = SupplierInvoiceStatus.PendingApproval
            };

            var expenseAccountId = await db.AccountingAccounts
                .Where(x => x.CompanyId == context.CompanyId && x.Code == "740")
                .Select(x => x.Id)
                .SingleAsync();

            invoice.Items.Add(new SupplierInvoiceItem
            {
                LineNumber = 1,
                Description = "Hizmet",
                Quantity = 1m,
                Unit = "adet",
                UnitPrice = 10_000m,
                VatRate = 20m,
                LineSubtotal = 10_000m,
                VatAmount = 2_000m,
                LineTotal = 12_000m,
                ExpenseAccountId = expenseAccountId
            });

            db.SupplierInvoices.Add(invoice);
            await db.SaveChangesAsync();
            invoiceId = invoice.Id;
        }

        var approve = await client.PostAsync(
            $"/api/supplier-invoices/{invoiceId}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.Contains("Tevkifat", await approve.Content.ReadAsStringAsync());
    }

    private async Task PostVoucherAsync(
        TestContext context, DateTime date, (string Code, decimal Debit, decimal Credit)[] lines)
    {
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
            VoucherDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
            FiscalYear = date.Year,
            FiscalPeriod = date.Month,
            CurrencyCode = "TRY",
            ExchangeRate = 1m,
            SourceModule = "Test",
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
                AccountingAccountId = accountIds[code],
                Description = "Test",
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

    /// <summary>
    /// Ödenecek KDV çıkan dönemde tahakkuk fişi: 391 borç, 191 alacak,
    /// fark 360.99 alacak. Fiş dengeli olmalı.
    /// </summary>
    [Fact]
    public async Task VatAccrual_WhenPayable_ClosesVatAccountsToPayable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(context, new DateTime(2026, 3, 10),
            [("120", 600_000m, 0m), ("600.03", 0m, 500_000m), ("391.09", 0m, 100_000m)]);
        await PostVoucherAsync(context, new DateTime(2026, 3, 18),
            [("740", 150_000m, 0m), ("191.01.03", 30_000m, 0m), ("320", 0m, 180_000m)]);

        var response = await client.PostAsJsonAsync("/api/tax/vat-accrual",
            new { companyId = context.CompanyId, year = 2026, month = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(70_000m, result.GetProperty("payableVat").GetDecimal());
        Assert.Equal(0m, result.GetProperty("carryForwardOut").GetDecimal());

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var voucher = await db.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.Id == result.GetProperty("voucherId").GetGuid());

        Assert.Equal(AccountingVoucherStatus.Posted, voucher.Status);
        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);

        decimal Net(string code) => voucher.Lines
            .Where(x => x.AccountingAccount.Code == code)
            .Sum(x => x.DebitAmount - x.CreditAmount);

        Assert.Equal(100_000m, Net("391.09"));
        Assert.Equal(-30_000m, Net("191.01.03"));
        Assert.Equal(-70_000m, Net("360.99"));
    }

    /// <summary>
    /// İndirilecek fazlaysa fark 190 Devreden KDV'ye borç yazılır ve
    /// sonraki dönem onu mahsup eder.
    /// </summary>
    [Fact]
    public async Task VatAccrual_WhenCarryForward_PostsToCarryForwardAccount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(context, new DateTime(2026, 4, 10),
            [("120", 60_000m, 0m), ("600.03", 0m, 50_000m), ("391.09", 0m, 10_000m)]);
        await PostVoucherAsync(context, new DateTime(2026, 4, 18),
            [("740", 200_000m, 0m), ("191.01.03", 40_000m, 0m), ("320", 0m, 240_000m)]);

        var response = await client.PostAsJsonAsync("/api/tax/vat-accrual",
            new { companyId = context.CompanyId, year = 2026, month = 4 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0m, result.GetProperty("payableVat").GetDecimal());
        Assert.Equal(30_000m, result.GetProperty("carryForwardOut").GetDecimal());

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var voucher = await db.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.Id == result.GetProperty("voucherId").GetGuid());

        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);

        var carryLine = voucher.Lines.Single(x => x.AccountingAccount.Code == "190.01");
        Assert.Equal(30_000m, carryLine.DebitAmount);
    }

    /// <summary>
    /// Tahakkuk fişi netleştirmeye dahil edilmemeli: edilseydi kapatılan
    /// tutar ikinci kez sayılır ve sonraki dönemin devredeni bozulurdu.
    /// </summary>
    [Fact]
    public async Task VatAccrual_DoesNotDistortFollowingPeriods()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(context, new DateTime(2026, 6, 10),
            [("120", 120_000m, 0m), ("600.03", 0m, 100_000m), ("391.09", 0m, 20_000m)]);

        await client.PostAsJsonAsync("/api/tax/vat-accrual",
            new { companyId = context.CompanyId, year = 2026, month = 6 });

        var overview = await client.GetFromJsonAsync<JsonElement>(
            $"/api/tax/overview?companyId={context.CompanyId}&year=2026");

        var june = overview.GetProperty("vat").EnumerateArray()
            .Single(x => x.GetProperty("month").GetInt32() == 6);
        var july = overview.GetProperty("vat").EnumerateArray()
            .Single(x => x.GetProperty("month").GetInt32() == 7);

        // Haziran hâlâ 20.000 ödenecek gösterir ve tahakkuk edilmiş olur.
        Assert.Equal(20_000m, june.GetProperty("payableVat").GetDecimal());
        Assert.True(june.GetProperty("isAccrued").GetBoolean());

        // Temmuz tahakkuk fişinden etkilenmemeli.
        Assert.Equal(0m, july.GetProperty("outputVat").GetDecimal());
        Assert.Equal(0m, july.GetProperty("carryForwardIn").GetDecimal());
    }

    /// <summary>Aynı dönem iki kez muhasebeleştirilemez.</summary>
    [Fact]
    public async Task VatAccrual_Twice_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(context, new DateTime(2026, 9, 10),
            [("120", 24_000m, 0m), ("600.03", 0m, 20_000m), ("391.09", 0m, 4_000m)]);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/tax/vat-accrual",
            new { companyId = context.CompanyId, year = 2026, month = 9 })).StatusCode);

        var second = await client.PostAsJsonAsync("/api/tax/vat-accrual",
            new { companyId = context.CompanyId, year = 2026, month = 9 });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("zaten yapılmış", await second.Content.ReadAsStringAsync());
    }

    /// <summary>Hareketsiz dönemde tahakkuk kesilmez.</summary>
    [Fact]
    public async Task VatAccrual_EmptyPeriod_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/tax/vat-accrual",
            new { companyId = context.CompanyId, year = 2026, month = 11 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("KDV hareketi yok", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Mutabakat raporu: hesaplanan ile fişe geçen aynı olmalı, fark
    /// sıfır çıkmalı.
    /// </summary>
    [Fact]
    public async Task Reconciliation_ShowsNoDifferenceAfterAccrual()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await PostVoucherAsync(context, new DateTime(2026, 2, 10),
            [("120", 60_000m, 0m), ("600.03", 0m, 50_000m), ("391.09", 0m, 10_000m)]);
        await PostVoucherAsync(context, new DateTime(2026, 2, 20),
            [("740", 20_000m, 0m), ("191.01.03", 4_000m, 0m), ("320", 0m, 24_000m)]);

        await client.PostAsJsonAsync("/api/tax/vat-accrual",
            new { companyId = context.CompanyId, year = 2026, month = 2 });

        var rows = await client.GetFromJsonAsync<JsonElement>(
            $"/api/tax/vat-reconciliation?companyId={context.CompanyId}&year=2026");

        var february = rows.EnumerateArray()
            .Single(x => x.GetProperty("month").GetInt32() == 2);

        Assert.True(february.GetProperty("isAccrued").GetBoolean());
        Assert.Equal(6_000m, february.GetProperty("computedPayable").GetDecimal());
        Assert.Equal(6_000m, february.GetProperty("accruedPayable").GetDecimal());
        Assert.Equal(0m, february.GetProperty("difference").GetDecimal());
    }
}
