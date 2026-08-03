using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Accounting;

/// <summary>
/// Tedarikçi faturası fişleştirme sonucu. ExpenseLineId, proje maliyet
/// kaydını muhasebedeki maliyet satırına bağlamak için kullanılır.
/// </summary>
public sealed record SupplierInvoicePostingResult(Guid VoucherId, Guid ExpenseLineId);

public interface IAccountingIntegrationService
{
    /// <summary>
    /// Şirketin finans ayarlarını getirir; yoksa hesap planından kod
    /// eşleştirmesiyle (191/391/600/740/320/120/780) varsayılanları
    /// oluşturur.
    /// </summary>
    Task<CompanyFinanceSettings> GetOrCreateFinanceSettingsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Onaylanan tedarikçi faturası için dengeli ve doğrudan Posted bir
    /// mahsup fişi üretir: maliyet hesabı + 191 İndirilecek KDV (borç),
    /// 320 Satıcılar (alacak). Fiş Id'si ile birlikte maliyet satırının
    /// Id'sini de döndürür (proje maliyeti ↔ muhasebe köprüsü için).
    /// </summary>
    Task<SupplierInvoicePostingResult> CreateSupplierInvoiceVoucherAsync(
        SupplierInvoice invoice,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kesinleştirilen hakediş için dengeli ve doğrudan Posted bir gelir
    /// fişi üretir: 120 Alıcılar + kesinti hesapları (borç),
    /// 600 Yurtiçi Satışlar + 391 Hesaplanan KDV (alacak).
    /// </summary>
    Task<Guid> CreateProgressPaymentVoucherAsync(
        ProgressPayment progressPayment,
        CancellationToken cancellationToken = default);
}

public sealed class AccountingIntegrationService(
    AppDbContext db,
    IAccountingVoucherService voucherService) : IAccountingIntegrationService
{
    public async Task<CompanyFinanceSettings> GetOrCreateFinanceSettingsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var settings = await db.CompanyFinanceSettings
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        if (settings is not null)
            return settings;

        settings = new CompanyFinanceSettings
        {
            CompanyId = companyId,
            VatInAccountId = await FindAccountIdAsync(companyId, cancellationToken, "191.01.03", "191"),
            VatOutAccountId = await FindAccountIdAsync(companyId, cancellationToken, "391.09", "391"),
            SalesAccountId = await FindAccountIdAsync(companyId, cancellationToken, "600.03", "600"),
            ExpenseAccountId = await FindAccountIdAsync(companyId, cancellationToken, "740"),
            PayablesAccountId = await FindAccountIdAsync(companyId, cancellationToken, "320"),
            ReceivablesAccountId = await FindAccountIdAsync(companyId, cancellationToken, "120"),
            FactoringExpenseAccountId = await FindAccountIdAsync(companyId, cancellationToken, "780.01.01", "780"),
            DeductionAccountId = await FindAccountIdAsync(companyId, cancellationToken, "126")
        };

        db.CompanyFinanceSettings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);

        return settings;
    }

    public async Task<SupplierInvoicePostingResult> CreateSupplierInvoiceVoucherAsync(
        SupplierInvoice invoice,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateFinanceSettingsAsync(
            invoice.CompanyId, cancellationToken);

        if (settings.ExpenseAccountId is null)
            throw new InvalidOperationException(
                "Maliyet hesabı yapılandırılmamış. Şirket Ayarları → Finans Ayarları'ndan seçin.");

        if (invoice.VatTotal > 0 && settings.VatInAccountId is null)
            throw new InvalidOperationException(
                "İndirilecek KDV hesabı yapılandırılmamış. Şirket Ayarları → Finans Ayarları'ndan seçin.");

        var supplier = await db.CurrentAccounts
            .SingleAsync(x => x.Id == invoice.SupplierCurrentAccountId, cancellationToken);

        var project = await db.Projects
            .SingleAsync(x => x.Id == invoice.ProjectId, cancellationToken);

        var payableAccountId = await ResolvePayableAccountAsync(
            supplier, settings, cancellationToken);

        var lines = new List<AccountingVoucherLineRequest>
        {
            new(
                AccountingAccountId: settings.ExpenseAccountId.Value,
                Description: $"Tedarikçi faturası maliyeti — {supplier.Title}",
                DebitAmount: invoice.Subtotal,
                CreditAmount: 0m,
                CurrencyCode: invoice.CurrencyCode,
                ExchangeRate: invoice.ExchangeRate,
                CurrentAccountId: null,
                ProjectId: invoice.ProjectId,
                CostCenterCode: project.Code,
                DocumentNumber: invoice.InvoiceNumber,
                DocumentDate: invoice.InvoiceDate,
                DueDate: null)
        };

        if (invoice.VatTotal > 0)
        {
            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: settings.VatInAccountId!.Value,
                Description: "İndirilecek KDV",
                DebitAmount: invoice.VatTotal,
                CreditAmount: 0m,
                CurrencyCode: invoice.CurrencyCode,
                ExchangeRate: invoice.ExchangeRate,
                CurrentAccountId: null,
                ProjectId: invoice.ProjectId,
                CostCenterCode: project.Code,
                DocumentNumber: invoice.InvoiceNumber,
                DocumentDate: invoice.InvoiceDate,
                DueDate: null));
        }

        lines.Add(new AccountingVoucherLineRequest(
            AccountingAccountId: payableAccountId,
            Description: $"Satıcı borcu — {supplier.Title}",
            DebitAmount: 0m,
            CreditAmount: invoice.GrandTotal,
            CurrencyCode: invoice.CurrencyCode,
            ExchangeRate: invoice.ExchangeRate,
            CurrentAccountId: supplier.Id,
            ProjectId: invoice.ProjectId,
            CostCenterCode: project.Code,
            DocumentNumber: invoice.InvoiceNumber,
            DocumentDate: invoice.InvoiceDate,
            DueDate: invoice.DueDate));

        // Denge ön kontrolü: fatura toplamları tutarlı olmalı; asıl
        // borç=alacak doğrulaması PostAsync içinde bir kez daha yapılır.
        var debitTotal = decimal.Round(invoice.Subtotal + invoice.VatTotal, 2);
        if (debitTotal != decimal.Round(invoice.GrandTotal, 2))
        {
            throw new InvalidOperationException(
                $"Fatura toplamları tutarsız: ara toplam + KDV ({debitTotal:N2}) ≠ genel toplam ({invoice.GrandTotal:N2}).");
        }

        var created = await voucherService.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: invoice.CompanyId,
                VoucherType: (int)AccountingVoucherType.Journal,
                VoucherDate: invoice.InvoiceDate,
                CurrencyCode: invoice.CurrencyCode,
                ExchangeRate: invoice.ExchangeRate,
                Description: $"Tedarikçi faturası {invoice.InternalNumber} — {supplier.Title}",
                ReferenceNumber: invoice.InvoiceNumber,
                SourceModule: "SupplierInvoice",
                SourceEntityId: invoice.Id,
                Lines: lines),
            cancellationToken);

        await voucherService.PostAsync(created.Id, cancellationToken);

        // Maliyet satırı ilk sırada üretiliyor; proje maliyet kaydı buna
        // bağlanacağı için Id'si geri veriliyor.
        var expenseLineId = await db.AccountingVoucherLines
            .Where(x => x.AccountingVoucherId == created.Id &&
                        x.AccountingAccountId == settings.ExpenseAccountId!.Value &&
                        x.DebitAmount > 0m)
            .OrderBy(x => x.LineNumber)
            .Select(x => x.Id)
            .FirstAsync(cancellationToken);

        return new SupplierInvoicePostingResult(created.Id, expenseLineId);
    }

    public async Task<Guid> CreateProgressPaymentVoucherAsync(
        ProgressPayment progressPayment,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateFinanceSettingsAsync(
            progressPayment.CompanyId, cancellationToken);

        if (settings.SalesAccountId is null)
            throw new InvalidOperationException(
                "Yurtiçi satışlar hesabı yapılandırılmamış. Şirket Ayarları → Finans Ayarları'ndan seçin.");

        var project = await db.Projects
            .SingleAsync(x => x.Id == progressPayment.ProjectId, cancellationToken);

        if (project.EmployerCurrentAccountId is null)
            throw new InvalidOperationException(
                "Projede işveren cari kartı tanımlı değil; hakediş muhasebeleştirilemez. " +
                "Proje kartından işvereni seçin.");

        var employer = await db.CurrentAccounts
            .SingleAsync(x => x.Id == project.EmployerCurrentAccountId.Value, cancellationToken);

        var deductions = await db.ProgressPaymentDeductions
            .Where(x => x.ProgressPaymentId == progressPayment.Id && x.Amount != 0m)
            .OrderBy(x => x.LineNumber)
            .ToListAsync(cancellationToken);

        // Tevkifatlı KDV'de satıcının beyan ettiği kısım yalnızca
        // tevkifat dışında kalan tutardır; kesilen kısmı alıcı beyan eder.
        var taxableAmount = decimal.Round(
            progressPayment.CurrentAmount + progressPayment.PriceDifferenceAmount, 2);
        var declaredVat = decimal.Round(
            progressPayment.VatAmount - progressPayment.WithholdingAmount, 2);
        var totalDeduction = decimal.Round(
            deductions.Sum(x => x.Amount), 2);
        var receivable = decimal.Round(
            progressPayment.NetPayableAmount, 2);

        if (taxableAmount <= 0m)
            throw new InvalidOperationException(
                "Hakediş tutarı sıfır; muhasebe fişi oluşturulamaz.");

        if (decimal.Round(receivable + totalDeduction, 2) !=
            decimal.Round(taxableAmount + declaredVat, 2))
        {
            throw new InvalidOperationException(
                $"Hakediş tutarları tutarsız: net ödenecek ({receivable:N2}) + kesintiler ({totalDeduction:N2}) " +
                $"≠ hakediş ({taxableAmount:N2}) + beyan edilen KDV ({declaredVat:N2}).");
        }

        var receivableAccountId = employer.ReceivableAccountingAccountId
            ?? settings.ReceivablesAccountId
            ?? throw new InvalidOperationException(
                $"'{employer.Title}' carisi için 120 Alıcılar hesabı bulunamadı. " +
                "Cari kartında hesap eşleyin veya Şirket Ayarları → Finans Ayarları'ndan varsayılan hesabı seçin.");

        var lines = new List<AccountingVoucherLineRequest>
        {
            new(
                AccountingAccountId: receivableAccountId,
                Description: $"Hakediş alacağı — {employer.Title}",
                DebitAmount: receivable,
                CreditAmount: 0m,
                CurrencyCode: progressPayment.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: employer.Id,
                ProjectId: progressPayment.ProjectId,
                CostCenterCode: project.Code,
                DocumentNumber: progressPayment.ProgressPaymentNumber,
                DocumentDate: progressPayment.ProgressPaymentDate,
                DueDate: null)
        };

        foreach (var deduction in deductions)
        {
            var deductionAccountId = deduction.AccountingAccountId
                ?? settings.DeductionAccountId
                ?? throw new InvalidOperationException(
                    $"'{deduction.Description}' kesintisi için muhasebe hesabı belirlenmemiş. " +
                    "Kesinti satırında hesap seçin veya Şirket Ayarları → Finans Ayarları'ndan varsayılan kesinti hesabını tanımlayın.");

            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: deductionAccountId,
                Description: $"Hakediş kesintisi — {deduction.Description}",
                DebitAmount: decimal.Round(deduction.Amount, 2),
                CreditAmount: 0m,
                CurrencyCode: progressPayment.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: employer.Id,
                ProjectId: progressPayment.ProjectId,
                CostCenterCode: project.Code,
                DocumentNumber: progressPayment.ProgressPaymentNumber,
                DocumentDate: progressPayment.ProgressPaymentDate,
                DueDate: null));
        }

        lines.Add(new AccountingVoucherLineRequest(
            AccountingAccountId: settings.SalesAccountId.Value,
            Description: $"Hakediş geliri — {project.Code}",
            DebitAmount: 0m,
            CreditAmount: taxableAmount,
            CurrencyCode: progressPayment.CurrencyCode,
            ExchangeRate: 1m,
            CurrentAccountId: employer.Id,
            ProjectId: progressPayment.ProjectId,
            CostCenterCode: project.Code,
            DocumentNumber: progressPayment.ProgressPaymentNumber,
            DocumentDate: progressPayment.ProgressPaymentDate,
            DueDate: null));

        if (declaredVat > 0m)
        {
            if (settings.VatOutAccountId is null)
                throw new InvalidOperationException(
                    "Hesaplanan KDV hesabı yapılandırılmamış. Şirket Ayarları → Finans Ayarları'ndan seçin.");

            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: settings.VatOutAccountId.Value,
                Description: progressPayment.WithholdingAmount > 0m
                    ? $"Hesaplanan KDV (tevkifat sonrası {progressPayment.WithholdingNumerator}/{progressPayment.WithholdingDenominator})"
                    : "Hesaplanan KDV",
                DebitAmount: 0m,
                CreditAmount: declaredVat,
                CurrencyCode: progressPayment.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: employer.Id,
                ProjectId: progressPayment.ProjectId,
                CostCenterCode: project.Code,
                DocumentNumber: progressPayment.ProgressPaymentNumber,
                DocumentDate: progressPayment.ProgressPaymentDate,
                DueDate: null));
        }

        var created = await voucherService.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: progressPayment.CompanyId,
                VoucherType: (int)AccountingVoucherType.Journal,
                VoucherDate: progressPayment.ProgressPaymentDate,
                CurrencyCode: progressPayment.CurrencyCode,
                ExchangeRate: 1m,
                Description:
                    $"Hakediş {progressPayment.ProgressPaymentNumber} " +
                    $"({progressPayment.PeriodNumber}. dönem) — {project.Code} {project.Name}",
                ReferenceNumber: progressPayment.ProgressPaymentNumber,
                SourceModule: "ProgressPayment",
                SourceEntityId: progressPayment.Id,
                Lines: lines),
            cancellationToken);

        await voucherService.PostAsync(created.Id, cancellationToken);

        return created.Id;
    }

    /// <summary>
    /// Tedarikçinin 320 alt hesabını çözer: cari kartındaki eşleme →
    /// 320 altında isim eşleşmesi (bulunursa kalıcı eşlenir) → şirket
    /// varsayılanı (320 grup hesabı, CurrentAccountId boyutuyla).
    /// </summary>
    private async Task<Guid> ResolvePayableAccountAsync(
        CurrentAccount supplier,
        CompanyFinanceSettings settings,
        CancellationToken cancellationToken)
    {
        if (supplier.PayableAccountingAccountId is not null)
            return supplier.PayableAccountingAccountId.Value;

        if (settings.PayablesAccountId is not null)
        {
            var normalizedTitle = supplier.Title.Trim().ToLowerInvariant();
            var matched = await db.AccountingAccounts
                .Where(x =>
                    x.CompanyId == supplier.CompanyId &&
                    x.ParentAccountId == settings.PayablesAccountId &&
                    x.IsActive &&
                    x.IsPostingAllowed &&
                    x.Name.ToLower() == normalizedTitle)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (matched is not null)
            {
                supplier.PayableAccountingAccountId = matched;
                await db.SaveChangesAsync(cancellationToken);
                return matched.Value;
            }

            return settings.PayablesAccountId.Value;
        }

        throw new InvalidOperationException(
            $"'{supplier.Title}' carisi için 320 Satıcılar hesabı bulunamadı. " +
            "Cari kartında hesap eşleyin veya Şirket Ayarları → Finans Ayarları'ndan varsayılan hesabı seçin.");
    }

    /// <summary>
    /// Kod adaylarını sırayla dener; hesap aktif ve kayıt yapılabilir
    /// (IsPostingAllowed) olmalıdır. Bulunamazsa null (admin UI'dan seçer).
    /// </summary>
    private async Task<Guid?> FindAccountIdAsync(
        Guid companyId,
        CancellationToken cancellationToken,
        params string[] codeCandidates)
    {
        foreach (var code in codeCandidates)
        {
            var id = await db.AccountingAccounts
                .Where(x =>
                    x.CompanyId == companyId &&
                    x.Code == code &&
                    x.IsActive &&
                    x.IsPostingAllowed)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (id is not null)
                return id;
        }

        return null;
    }
}
