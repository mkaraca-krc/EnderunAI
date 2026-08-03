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

    /// <summary>
    /// Kasa/banka hareketi için dengeli, doğrudan Posted fiş üretir.
    /// Para girişinde kasa/banka hesabı borçlanır, karşı hesap alacaklanır;
    /// çıkışta tersi. Karşı hesap işlem tipine göre belirlenir
    /// (tahsilat→120, ödeme→320, çek tahsili→101, çek ödemesi→103).
    /// </summary>
    Task<Guid> CreateCashTransactionVoucherAsync(
        CashTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Çekin bir durum geçişi için dengeli, doğrudan Posted fiş üretir.
    /// Muhasebe etkisi olmayan geçişlerde (ör. faktoringdeki çekin
    /// tahsil bildirimi) null döner.
    /// </summary>
    Task<Guid?> CreateChequeVoucherAsync(
        Cheque cheque,
        ChequeStatus? fromStatus,
        ChequeStatus toStatus,
        DateTime voucherDate,
        CashAccount? cashAccount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Çek kırdırma fişi: 102 Bankalar (net) + 780 Finansman Giderleri
    /// (komisyon + BSMV + masraf) borç / 101 Alınan Çekler (nominal)
    /// alacak.
    /// </summary>
    Task<Guid> CreateFactoringVoucherAsync(
        FactoringTransaction transaction,
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

    public async Task<Guid> CreateCashTransactionVoucherAsync(
        CashTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var cashAccount = await db.CashAccounts
            .Include(x => x.AccountingAccount)
            .SingleAsync(x => x.Id == transaction.CashAccountId, cancellationToken);

        var settings = await GetOrCreateFinanceSettingsAsync(
            cashAccount.CompanyId, cancellationToken);

        if (transaction.Amount <= 0m)
            throw new InvalidOperationException("Hareket tutarı sıfırdan büyük olmalıdır.");

        CurrentAccount? counterparty = null;
        if (transaction.CurrentAccountId is not null)
        {
            counterparty = await db.CurrentAccounts
                .SingleAsync(x => x.Id == transaction.CurrentAccountId.Value, cancellationToken);
        }

        // Karşı hesap: paranın nereden geldiği / nereye gittiği.
        var (counterAccountId, counterDescription) = transaction.TransactionType switch
        {
            CashTransactionType.Collection =>
                (counterparty?.ReceivableAccountingAccountId ?? settings.ReceivablesAccountId,
                 $"Tahsilat — {counterparty?.Title ?? "cari"}"),

            CashTransactionType.Payment =>
                (counterparty?.PayableAccountingAccountId ?? settings.PayablesAccountId,
                 $"Ödeme — {counterparty?.Title ?? "cari"}"),

            CashTransactionType.ChequeCollection =>
                (await FindAccountIdAsync(cashAccount.CompanyId, cancellationToken, "101.01.01", "101"),
                 "Alınan çek tahsili"),

            CashTransactionType.ChequePayment =>
                (await FindAccountIdAsync(cashAccount.CompanyId, cancellationToken, "103.01", "103"),
                 "Verilen çek ödemesi"),

            _ => (null, transaction.Description)
        };

        if (counterAccountId is null)
        {
            throw new InvalidOperationException(
                "Bu hareket için karşı muhasebe hesabı belirlenemedi. " +
                "Şirket Ayarları → Finans Ayarları'ndan ilgili hesabı seçin.");
        }

        var isInflow = transaction.Direction == CashTransactionDirection.In;
        var amount = decimal.Round(transaction.Amount, 2);

        var lines = new List<AccountingVoucherLineRequest>
        {
            new(
                AccountingAccountId: cashAccount.AccountingAccountId,
                Description: $"{cashAccount.Name} — {(isInflow ? "giriş" : "çıkış")}",
                DebitAmount: isInflow ? amount : 0m,
                CreditAmount: isInflow ? 0m : amount,
                CurrencyCode: transaction.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: transaction.CurrentAccountId,
                ProjectId: transaction.ProjectId,
                CostCenterCode: null,
                DocumentNumber: transaction.DocumentNumber,
                DocumentDate: transaction.TransactionDate,
                DueDate: null),
            new(
                AccountingAccountId: counterAccountId.Value,
                Description: counterDescription,
                DebitAmount: isInflow ? 0m : amount,
                CreditAmount: isInflow ? amount : 0m,
                CurrencyCode: transaction.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: transaction.CurrentAccountId,
                ProjectId: transaction.ProjectId,
                CostCenterCode: null,
                DocumentNumber: transaction.DocumentNumber,
                DocumentDate: transaction.TransactionDate,
                DueDate: null)
        };

        var voucherType = isInflow
            ? AccountingVoucherType.Collection
            : AccountingVoucherType.Payment;

        var created = await voucherService.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: cashAccount.CompanyId,
                VoucherType: (int)voucherType,
                VoucherDate: transaction.TransactionDate,
                CurrencyCode: transaction.CurrencyCode,
                ExchangeRate: 1m,
                Description: transaction.Description,
                ReferenceNumber: transaction.DocumentNumber,
                SourceModule: transaction.SourceModule ?? "CashTransaction",
                SourceEntityId: transaction.Id,
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
    public async Task<Guid?> CreateChequeVoucherAsync(
        Cheque cheque,
        ChequeStatus? fromStatus,
        ChequeStatus toStatus,
        DateTime voucherDate,
        CashAccount? cashAccount,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateFinanceSettingsAsync(
            cheque.CompanyId, cancellationToken);

        CurrentAccount? counterparty = null;
        if (cheque.CurrentAccountId is not null)
        {
            counterparty = await db.CurrentAccounts
                .SingleOrDefaultAsync(
                    x => x.Id == cheque.CurrentAccountId.Value, cancellationToken);
        }

        var amount = decimal.Round(cheque.Amount, 2);
        if (amount <= 0m)
            throw new InvalidOperationException("Çek tutarı sıfırdan büyük olmalıdır.");

        // Çekin durumuna karşılık gelen muhasebe hesabı.
        async Task<Guid> ChequeAccountAsync(ChequeStatus status)
        {
            var codes = status switch
            {
                ChequeStatus.Portfolio => new[] { "101.01", "101" },
                ChequeStatus.AtBank => new[] { "101.02", "101" },
                ChequeStatus.AtFactoring => new[] { "101.03", "101" },
                ChequeStatus.Issued => new[] { "103.01", "103" },
                _ => new[] { "101" }
            };

            var id = await FindAccountIdAsync(cheque.CompanyId, cancellationToken, codes);
            if (id is null)
            {
                throw new InvalidOperationException(
                    $"Çek hesabı bulunamadı ({string.Join(" / ", codes)}). " +
                    "Hesap planında ilgili hesabı tanımlayın.");
            }

            return id.Value;
        }

        Guid CounterpartyAccount(bool receivable)
        {
            var id = receivable
                ? counterparty?.ReceivableAccountingAccountId ?? settings.ReceivablesAccountId
                : counterparty?.PayableAccountingAccountId ?? settings.PayablesAccountId;

            if (id is null)
            {
                throw new InvalidOperationException(
                    receivable
                        ? "Alıcılar (120) hesabı belirlenemedi. Şirket Ayarları → Finans Ayarları'ndan seçin."
                        : "Satıcılar (320) hesabı belirlenemedi. Şirket Ayarları → Finans Ayarları'ndan seçin.");
            }

            return id.Value;
        }

        Guid CashAccountOrThrow()
        {
            if (cashAccount is null)
            {
                throw new InvalidOperationException(
                    "Bu geçiş için kasa/banka hesabı seçilmelidir.");
            }

            return cashAccount.AccountingAccountId;
        }

        // (borç hesabı, alacak hesabı, açıklama) — geçişin muhasebe karşılığı.
        (Guid Debit, Guid Credit, string Description)? entry = (fromStatus, toStatus) switch
        {
            // Alınan çek girişi: portföye alındı, cari alacağı kapanır.
            (null, ChequeStatus.Portfolio) =>
                (await ChequeAccountAsync(ChequeStatus.Portfolio),
                 CounterpartyAccount(receivable: true),
                 $"Alınan çek — {counterparty?.Title ?? "cari"}"),

            // Verilen çek girişi: satıcı borcu çek borcuna dönüşür.
            (null, ChequeStatus.Issued) =>
                (CounterpartyAccount(receivable: false),
                 await ChequeAccountAsync(ChequeStatus.Issued),
                 $"Verilen çek — {counterparty?.Title ?? "cari"}"),

            // Portföyden bankaya tahsile/teminata verildi ve geri alınması.
            (ChequeStatus.Portfolio, ChequeStatus.AtBank) =>
                (await ChequeAccountAsync(ChequeStatus.AtBank),
                 await ChequeAccountAsync(ChequeStatus.Portfolio),
                 "Çek bankaya tahsile verildi"),

            (ChequeStatus.AtBank, ChequeStatus.Portfolio) =>
                (await ChequeAccountAsync(ChequeStatus.Portfolio),
                 await ChequeAccountAsync(ChequeStatus.AtBank),
                 "Çek bankadan geri alındı"),

            // Tahsil: para kasaya/bankaya girer, çek hesabı kapanır.
            (ChequeStatus.Portfolio, ChequeStatus.Collected) =>
                (CashAccountOrThrow(),
                 await ChequeAccountAsync(ChequeStatus.Portfolio),
                 "Çek tahsil edildi"),

            (ChequeStatus.AtBank, ChequeStatus.Collected) =>
                (CashAccountOrThrow(),
                 await ChequeAccountAsync(ChequeStatus.AtBank),
                 "Çek tahsil edildi"),

            // Faktoringdeki çekin tahsil bildirimi: para zaten kırdırma
            // anında alındığı için muhasebe etkisi yok.
            (ChequeStatus.AtFactoring, ChequeStatus.Collected) => null,

            // Karşılıksız: alacak cariye geri döner.
            (ChequeStatus.Portfolio, ChequeStatus.Bounced) =>
                (CounterpartyAccount(receivable: true),
                 await ChequeAccountAsync(ChequeStatus.Portfolio),
                 "Karşılıksız çek — alacak cariye döndü"),

            (ChequeStatus.AtBank, ChequeStatus.Bounced) =>
                (CounterpartyAccount(receivable: true),
                 await ChequeAccountAsync(ChequeStatus.AtBank),
                 "Karşılıksız çek — alacak cariye döndü"),

            // Faktoringdeki çek karşılıksız çıkarsa rücu: parayı faktoring
            // şirketine iade ederiz, alacak cariye geri döner.
            (ChequeStatus.AtFactoring, ChequeStatus.Bounced) =>
                (CounterpartyAccount(receivable: true),
                 CashAccountOrThrow(),
                 "Karşılıksız çek — faktoring rücu iadesi"),

            // Verilen çek vadesinde ödendi.
            (ChequeStatus.Issued, ChequeStatus.Paid) =>
                (await ChequeAccountAsync(ChequeStatus.Issued),
                 CashAccountOrThrow(),
                 "Verilen çek ödendi"),

            // Verilen çek iade alındı: borç yeniden satıcıda.
            (ChequeStatus.Issued, ChequeStatus.Returned) =>
                (await ChequeAccountAsync(ChequeStatus.Issued),
                 CounterpartyAccount(receivable: false),
                 "Verilen çek iade alındı"),

            _ => throw new InvalidOperationException(
                $"'{fromStatus}' → '{toStatus}' geçişi için muhasebe kaydı tanımlı değil.")
        };

        if (entry is null)
            return null;

        // Aynı hesaba borç ve alacak yazan geçiş (hesap planında 101 alt
        // kırılımı yoksa) muhasebede anlamsız — fiş üretilmez.
        if (entry.Value.Debit == entry.Value.Credit)
            return null;

        var voucherType = toStatus switch
        {
            ChequeStatus.Collected => AccountingVoucherType.Collection,
            ChequeStatus.Paid => AccountingVoucherType.Payment,
            _ => AccountingVoucherType.Journal
        };

        var lines = new List<AccountingVoucherLineRequest>
        {
            new(
                AccountingAccountId: entry.Value.Debit,
                Description: entry.Value.Description,
                DebitAmount: amount,
                CreditAmount: 0m,
                CurrencyCode: cheque.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: cheque.CurrentAccountId,
                ProjectId: cheque.ProjectId,
                CostCenterCode: null,
                DocumentNumber: cheque.ChequeNumber,
                DocumentDate: cheque.IssueDate,
                DueDate: cheque.DueDate),
            new(
                AccountingAccountId: entry.Value.Credit,
                Description: entry.Value.Description,
                DebitAmount: 0m,
                CreditAmount: amount,
                CurrencyCode: cheque.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: cheque.CurrentAccountId,
                ProjectId: cheque.ProjectId,
                CostCenterCode: null,
                DocumentNumber: cheque.ChequeNumber,
                DocumentDate: cheque.IssueDate,
                DueDate: cheque.DueDate)
        };

        var created = await voucherService.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: cheque.CompanyId,
                VoucherType: (int)voucherType,
                VoucherDate: voucherDate,
                CurrencyCode: cheque.CurrencyCode,
                ExchangeRate: 1m,
                Description: $"{cheque.InternalNumber} — {entry.Value.Description}",
                ReferenceNumber: cheque.ChequeNumber,
                SourceModule: "Cheque",
                SourceEntityId: cheque.Id,
                Lines: lines),
            cancellationToken);

        await voucherService.PostAsync(created.Id, cancellationToken);

        return created.Id;
    }

    public async Task<Guid> CreateFactoringVoucherAsync(
        FactoringTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateFinanceSettingsAsync(
            transaction.CompanyId, cancellationToken);

        if (settings.FactoringExpenseAccountId is null)
        {
            throw new InvalidOperationException(
                "Finansman gideri hesabı (780) yapılandırılmamış. " +
                "Şirket Ayarları → Finans Ayarları'ndan seçin.");
        }

        var cheque = await db.Cheques
            .SingleAsync(x => x.Id == transaction.ChequeId, cancellationToken);

        var cashAccount = await db.CashAccounts
            .SingleAsync(x => x.Id == transaction.CashAccountId, cancellationToken);

        var chequeAccountId = await FindAccountIdAsync(
            transaction.CompanyId, cancellationToken, "101.01", "101");

        if (chequeAccountId is null)
        {
            throw new InvalidOperationException(
                "Alınan çekler hesabı (101) bulunamadı. Hesap planını kontrol edin.");
        }

        var nominal = decimal.Round(transaction.ChequeAmount, 2);
        var net = decimal.Round(transaction.NetAmount, 2);
        var deduction = decimal.Round(transaction.TotalDeductionAmount, 2);

        if (net + deduction != nominal)
        {
            throw new InvalidOperationException(
                $"Faktoring tutarları tutarsız: net ({net:N2}) + kesinti ({deduction:N2}) " +
                $"≠ çek tutarı ({nominal:N2}).");
        }

        var project = transaction.ProjectId is null
            ? null
            : await db.Projects
                .SingleOrDefaultAsync(x => x.Id == transaction.ProjectId.Value, cancellationToken);

        var lines = new List<AccountingVoucherLineRequest>
        {
            new(
                AccountingAccountId: cashAccount.AccountingAccountId,
                Description: $"Faktoring net tahsilat — {cashAccount.Name}",
                DebitAmount: net,
                CreditAmount: 0m,
                CurrencyCode: transaction.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: transaction.FactoringCurrentAccountId,
                ProjectId: transaction.ProjectId,
                CostCenterCode: project?.Code,
                DocumentNumber: transaction.InternalNumber,
                DocumentDate: transaction.TransactionDate,
                DueDate: null)
        };

        // Kesintiler ayrı satırlarda: komisyon, BSMV ve masraf tek tek
        // izlenebilsin (hepsi 780 Finansman Giderleri altında).
        void AddDeductionLine(decimal value, string description)
        {
            if (value <= 0m)
                return;

            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: settings.FactoringExpenseAccountId!.Value,
                Description: description,
                DebitAmount: decimal.Round(value, 2),
                CreditAmount: 0m,
                CurrencyCode: transaction.CurrencyCode,
                ExchangeRate: 1m,
                CurrentAccountId: transaction.FactoringCurrentAccountId,
                ProjectId: transaction.ProjectId,
                CostCenterCode: project?.Code,
                DocumentNumber: transaction.InternalNumber,
                DocumentDate: transaction.TransactionDate,
                DueDate: null));
        }

        AddDeductionLine(transaction.CommissionAmount, "Faktoring komisyonu");
        AddDeductionLine(transaction.BsmvAmount, "Faktoring BSMV");
        AddDeductionLine(transaction.ExpenseAmount, "Faktoring masrafı");

        lines.Add(new AccountingVoucherLineRequest(
            AccountingAccountId: chequeAccountId.Value,
            Description: $"Kırdırılan çek — {cheque.ChequeNumber}",
            DebitAmount: 0m,
            CreditAmount: nominal,
            CurrencyCode: transaction.CurrencyCode,
            ExchangeRate: 1m,
            CurrentAccountId: cheque.CurrentAccountId,
            ProjectId: transaction.ProjectId,
            CostCenterCode: project?.Code,
            DocumentNumber: cheque.ChequeNumber,
            DocumentDate: cheque.IssueDate,
            DueDate: cheque.DueDate));

        var created = await voucherService.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: transaction.CompanyId,
                VoucherType: (int)AccountingVoucherType.Collection,
                VoucherDate: transaction.TransactionDate,
                CurrencyCode: transaction.CurrencyCode,
                ExchangeRate: 1m,
                Description: $"Çek kırdırma {transaction.InternalNumber} — {cheque.ChequeNumber}",
                ReferenceNumber: cheque.ChequeNumber,
                SourceModule: "Factoring",
                SourceEntityId: transaction.Id,
                Lines: lines),
            cancellationToken);

        await voucherService.PostAsync(created.Id, cancellationToken);

        return created.Id;
    }

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
