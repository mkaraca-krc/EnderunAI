using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Accounting;

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
    /// 320 Satıcılar (alacak). Fiş Id'sini döndürür.
    /// </summary>
    Task<Guid> CreateSupplierInvoiceVoucherAsync(
        SupplierInvoice invoice,
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
            FactoringExpenseAccountId = await FindAccountIdAsync(companyId, cancellationToken, "780.01.01", "780")
        };

        db.CompanyFinanceSettings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);

        return settings;
    }

    public async Task<Guid> CreateSupplierInvoiceVoucherAsync(
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
