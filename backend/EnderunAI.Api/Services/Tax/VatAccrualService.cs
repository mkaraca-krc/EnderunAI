using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Accounting;
using Microsoft.EntityFrameworkCore;
using EnderunAI.Api.Formatting;

namespace EnderunAI.Api.Services.Tax;

public sealed record VatAccrualResult(
    Guid VoucherId,
    string VoucherNumber,
    int Year,
    int Month,
    decimal OutputVat,
    decimal InputVat,
    decimal CarryForwardIn,
    decimal PayableVat,
    decimal CarryForwardOut,
    string Message);

/// <summary>Müşavir mutabakatı için tek bir dönemin karşılaştırması.</summary>
public sealed record VatReconciliationRow(
    int Year,
    int Month,
    string Label,
    decimal ComputedPayable,
    decimal ComputedCarryForward,
    bool IsAccrued,
    string? VoucherNumber,
    decimal AccruedPayable,
    decimal AccruedCarryForward,
    /// <summary>Hesaplanan ile fişe geçen arasındaki fark; sıfır olmalı.</summary>
    decimal Difference);

public interface IVatAccrualService
{
    /// <summary>
    /// Dönem sonu KDV tahakkuk fişi: 391 borç / 191 alacak kapatma,
    /// fark 360.99 (ödenecek) ya da 190 (devreden).
    /// </summary>
    Task<VatAccrualResult> AccrueAsync(
        Guid companyId, int year, int month, CancellationToken cancellationToken);

    /// <summary>Hesaplanan tutarlar ile kesilen fişlerin karşılaştırması.</summary>
    Task<IReadOnlyList<VatReconciliationRow>> ReconcileAsync(
        Guid companyId, int year, CancellationToken cancellationToken);
}

/// <summary>
/// Dönem sonu KDV muhasebeleştirmesi.
///
/// Fiş, 391 ve 191 ALT HESAPLARINI tek tek kapatır: müşavir mutabakatı
/// oran bazında yapılır, tek toplam satır kesilseydi hangi oranda ne
/// kaldığı görünmezdi.
///
/// Sorumlu sıfatıyla ödenecek KDV (360.002) bu fişe girmez: o tutar
/// netleştirmeye tabi değil, ayrı ödenen bir yükümlülüktür.
/// </summary>
public sealed class VatAccrualService(
    AppDbContext db,
    ITaxLedgerService taxLedger,
    IAccountingIntegrationService accountingIntegration,
    IAccountingVoucherService voucherService) : IVatAccrualService
{
    public async Task<VatAccrualResult> AccrueAsync(
        Guid companyId, int year, int month, CancellationToken cancellationToken)
    {
        if (month is < 1 or > 12)
            throw new ArgumentException("Ay 1 ile 12 arasında olmalıdır.");

        var period = await taxLedger.GetVatPeriodAsync(
            companyId, year, month, cancellationToken);

        if (period.IsAccrued)
        {
            throw new InvalidOperationException(
                $"{month:00}/{year} dönemi KDV tahakkuku zaten yapılmış " +
                $"({period.AccrualVoucherNumber}).");
        }

        if (period.OutputVat == 0m && period.InputVat == 0m && period.CarryForwardIn == 0m)
        {
            throw new InvalidOperationException(
                $"{month:00}/{year} döneminde KDV hareketi yok; tahakkuk kesilmez.");
        }

        // Ayar satırı yoksa kurulur ve hesaplar hesap planından eşlenir;
        // ilk kez KDV tahakkuku kesen şirket ayar ekranına gitmek
        // zorunda kalmasın.
        var settings = await accountingIntegration.GetOrCreateFinanceSettingsAsync(
            companyId, cancellationToken);

        var payableAccountId = settings.VatPayableAccountId
            ?? throw new InvalidOperationException(
                "Ödenecek KDV hesabı (360.99) yapılandırılmamış. " +
                "Şirket Ayarları → Finans Ayarları'ndan seçin.");

        var carryForwardAccountId = settings.VatCarryForwardAccountId
            ?? throw new InvalidOperationException(
                "Devreden KDV hesabı (190) yapılandırılmamış. " +
                "Şirket Ayarları → Finans Ayarları'ndan seçin.");

        var balances = await GetAccountBalancesAsync(companyId, year, month, cancellationToken);

        var companyCode = await db.Companies
            .Where(x => x.Id == companyId)
            .Select(x => x.Code)
            .SingleAsync(cancellationToken);

        var voucherDate = new DateTime(
            year, month, DateTime.DaysInMonth(year, month), 0, 0, 0, DateTimeKind.Utc);

        var lines = new List<AccountingVoucherLineRequest>();

        void Add(Guid accountId, decimal debit, decimal credit, string description)
        {
            if (debit == 0m && credit == 0m)
                return;

            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: accountId,
                Description: description,
                DebitAmount: debit,
                CreditAmount: credit,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                CurrentAccountId: null,
                ProjectId: null,
                CostCenterCode: companyCode,
                DocumentNumber: null,
                DocumentDate: voucherDate,
                DueDate: null));
        }

        // Hesaplanan KDV alt hesapları borçlandırılarak kapanır.
        foreach (var account in balances.Where(x => x.Code.StartsWith("391", StringComparison.Ordinal)))
        {
            if (account.Net > 0m)
                Add(account.Id, account.Net, 0m, $"{account.Code} hesaplanan KDV kapanışı");
            else if (account.Net < 0m)
                Add(account.Id, 0m, -account.Net, $"{account.Code} hesaplanan KDV düzeltmesi");
        }

        // İndirilecek KDV alt hesapları alacaklandırılarak kapanır.
        foreach (var account in balances.Where(x => x.Code.StartsWith("191", StringComparison.Ordinal)))
        {
            if (account.Net > 0m)
                Add(account.Id, 0m, account.Net, $"{account.Code} indirilecek KDV kapanışı");
            else if (account.Net < 0m)
                Add(account.Id, -account.Net, 0m, $"{account.Code} indirilecek KDV düzeltmesi");
        }

        // Önceki aydan gelen devreden kullanıldığı için 190 alacaklanır.
        Add(carryForwardAccountId, 0m, period.CarryForwardIn,
            "Önceki dönemden devreden KDV mahsubu");

        Add(payableAccountId, 0m, period.PayableVat, $"{month:00}/{year} ödenecek KDV");

        Add(carryForwardAccountId, period.CarryForwardOut, 0m,
            $"{month:00}/{year} sonraki döneme devreden KDV");

        var debitTotal = decimal.Round(lines.Sum(x => x.DebitAmount), 2);
        var creditTotal = decimal.Round(lines.Sum(x => x.CreditAmount), 2);

        if (debitTotal != creditTotal)
        {
            throw new InvalidOperationException(
                $"KDV tahakkuk fişi dengesiz: borç {TurkishFormat.Amount(debitTotal)} ≠ alacak {TurkishFormat.Amount(creditTotal)}. " +
                "Dönemdeki KDV hesap hareketleri kendi içinde tutarsız.");
        }

        var reference = TaxLedgerService.PeriodReference(year, month);

        var created = await voucherService.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: companyId,
                VoucherType: (int)AccountingVoucherType.Journal,
                VoucherDate: voucherDate,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                Description: $"{month:00}/{year} dönemi KDV tahakkuku",
                ReferenceNumber: reference,
                SourceModule: TaxLedgerService.VatAccrualSourceModule,
                SourceEntityId: null,
                Lines: lines),
            cancellationToken);

        await voucherService.PostAsync(created.Id, cancellationToken);

        var message = period.PayableVat > 0m
            ? $"{month:00}/{year} dönemi KDV tahakkuku kesildi; " +
              $"ödenecek KDV {TurkishFormat.Amount(period.PayableVat)} TL."
            : $"{month:00}/{year} dönemi KDV tahakkuku kesildi; " +
              $"sonraki döneme {TurkishFormat.Amount(period.CarryForwardOut)} TL devrediyor.";

        return new VatAccrualResult(
            created.Id,
            created.VoucherNumber,
            year,
            month,
            period.OutputVat,
            period.InputVat,
            period.CarryForwardIn,
            period.PayableVat,
            period.CarryForwardOut,
            message);
    }

    public async Task<IReadOnlyList<VatReconciliationRow>> ReconcileAsync(
        Guid companyId, int year, CancellationToken cancellationToken)
    {
        var overview = await taxLedger.GetOverviewAsync(companyId, year, cancellationToken);

        var settings = await db.CompanyFinanceSettings
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new { x.VatPayableAccountId, x.VatCarryForwardAccountId })
            .SingleOrDefaultAsync(cancellationToken);

        var accrualLines = await db.AccountingVoucherLines
            .AsNoTracking()
            .Where(x => !x.IsDeleted &&
                        x.AccountingVoucher.CompanyId == companyId &&
                        x.AccountingVoucher.SourceModule ==
                            TaxLedgerService.VatAccrualSourceModule &&
                        x.AccountingVoucher.Status == AccountingVoucherStatus.Posted)
            .Select(x => new
            {
                x.AccountingVoucher.ReferenceNumber,
                x.AccountingAccountId,
                x.DebitAmountLocal,
                x.CreditAmountLocal
            })
            .ToListAsync(cancellationToken);

        var rows = new List<VatReconciliationRow>(12);

        foreach (var period in overview.Vat)
        {
            var reference = TaxLedgerService.PeriodReference(period.Year, period.Month);

            var periodLines = accrualLines
                .Where(x => x.ReferenceNumber == reference)
                .ToList();

            var accruedPayable = settings?.VatPayableAccountId is Guid payableId
                ? decimal.Round(periodLines
                    .Where(x => x.AccountingAccountId == payableId)
                    .Sum(x => x.CreditAmountLocal - x.DebitAmountLocal), 2)
                : 0m;

            // Devreden hesabında hem mahsup (alacak) hem yeni devreden
            // (borç) satırı olabilir; net borç bakiyesi dönem sonundaki
            // devredendir.
            var accruedCarry = settings?.VatCarryForwardAccountId is Guid carryId
                ? decimal.Round(periodLines
                    .Where(x => x.AccountingAccountId == carryId)
                    .Sum(x => x.DebitAmountLocal), 2)
                : 0m;

            rows.Add(new VatReconciliationRow(
                period.Year,
                period.Month,
                period.Label,
                period.PayableVat,
                period.CarryForwardOut,
                period.IsAccrued,
                period.AccrualVoucherNumber,
                accruedPayable,
                accruedCarry,
                decimal.Round(
                    (period.PayableVat - accruedPayable) +
                    (period.CarryForwardOut - accruedCarry), 2)));
        }

        return rows;
    }

    private sealed record AccountBalance(Guid Id, string Code, decimal Net);

    /// <summary>
    /// Dönemdeki KDV hesaplarının net bakiyesi. 391 için alacak − borç,
    /// 191 için borç − alacak; işaret hesabın doğal yönüne göre pozitif
    /// olur ve kapanış satırı ters yöne yazılır.
    /// </summary>
    private async Task<IReadOnlyList<AccountBalance>> GetAccountBalancesAsync(
        Guid companyId, int year, int month, CancellationToken cancellationToken)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);

        var rows = await db.AccountingVoucherLines
            .AsNoTracking()
            .Where(x => !x.IsDeleted &&
                        !x.AccountingVoucher.IsDeleted &&
                        x.AccountingVoucher.CompanyId == companyId &&
                        x.AccountingVoucher.Status == AccountingVoucherStatus.Posted &&
                        x.AccountingVoucher.VoucherDate >= start &&
                        x.AccountingVoucher.VoucherDate < end &&
                        x.AccountingVoucher.SourceModule !=
                            TaxLedgerService.VatAccrualSourceModule &&
                        (x.AccountingAccount.Code.StartsWith("191") ||
                         x.AccountingAccount.Code.StartsWith("391")))
            .GroupBy(x => new { x.AccountingAccountId, x.AccountingAccount.Code })
            .Select(g => new
            {
                g.Key.AccountingAccountId,
                g.Key.Code,
                Debit = g.Sum(x => x.DebitAmountLocal),
                Credit = g.Sum(x => x.CreditAmountLocal)
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new AccountBalance(
                x.AccountingAccountId,
                x.Code,
                x.Code.StartsWith("391", StringComparison.Ordinal)
                    ? decimal.Round(x.Credit - x.Debit, 2)
                    : decimal.Round(x.Debit - x.Credit, 2)))
            .Where(x => x.Net != 0m)
            .OrderBy(x => x.Code)
            .ToList();
    }
}
