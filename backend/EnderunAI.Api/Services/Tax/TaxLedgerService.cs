using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;
using EnderunAI.Api.Formatting;

namespace EnderunAI.Api.Services.Tax;

/// <summary>Bir ayın KDV netleştirmesi.</summary>
/// <param name="OutputVat">391 Hesaplanan KDV (alacak − borç).</param>
/// <param name="InputVat">191 İndirilecek KDV (borç − alacak).</param>
/// <param name="ReverseChargeVat">
/// 360.002 Sorumlu sıfatıyla ödenecek KDV — tevkifatlı alışta bizim
/// beyan edip ödediğimiz kısım. İndirilecek KDV'nin içinde yer alır
/// ama AYRI ödenir; net hesapta bu yüzden ayrı satır.
/// </param>
/// <param name="CarryForwardIn">Önceki aydan devreden KDV.</param>
/// <param name="PayableVat">Bu ay ödenecek KDV (0'dan küçük olamaz).</param>
/// <param name="CarryForwardOut">Sonraki aya devreden KDV.</param>
public sealed record VatPeriodSummary(
    int Year,
    int Month,
    string Label,
    decimal OutputVat,
    decimal InputVat,
    decimal ReverseChargeVat,
    decimal CarryForwardIn,
    decimal PayableVat,
    decimal CarryForwardOut,
    /// <summary>Dönem sonu KDV tahakkuk fişi kesilmiş mi.</summary>
    bool IsAccrued,
    Guid? AccrualVoucherId,
    string? AccrualVoucherNumber);

/// <summary>Bir ayın bordro kaynaklı vergi ve prim yükü.</summary>
public sealed record PayrollTaxPeriodSummary(
    int Year,
    int Month,
    string Label,
    decimal IncomeTaxWithholding,
    decimal StampTax,
    decimal SgkEmployee,
    decimal SgkEmployer,
    decimal SgkTotal,
    decimal TotalBurden,
    int PersonnelCount,
    /// <summary>
    /// Tahakkuk fişi kesilmiş mi. Kesilmemişse rakamlar onaylı
    /// bordrolardan okunur ve "tahakkuk edilmemiş" sayılır.
    /// </summary>
    bool IsAccrued);

/// <summary>Üç aylık geçici vergi tahmini.</summary>
public sealed record AdvanceTaxPeriodSummary(
    int Year,
    int Quarter,
    string Label,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    decimal Revenue,
    decimal Expense,
    decimal ProfitBeforeTax,
    decimal TaxRate,
    decimal EstimatedTax,
    /// <summary>Beyan/ödeme tarihi — takvim ve nakit akış bunu kullanır.</summary>
    DateTime DueDate);

public sealed record TaxOverview(
    Guid CompanyId,
    string CurrencyCode,
    IReadOnlyList<VatPeriodSummary> Vat,
    IReadOnlyList<PayrollTaxPeriodSummary> Payroll,
    IReadOnlyList<AdvanceTaxPeriodSummary> AdvanceTax,
    decimal CorporateTaxRate,
    decimal EstimatedAnnualCorporateTax,
    IReadOnlyList<string> Assumptions);

public interface ITaxLedgerService
{
    /// <summary>
    /// Verilen yıl için aylık KDV, bordro yükü ve geçici vergi tahmini.
    /// </summary>
    Task<TaxOverview> GetOverviewAsync(
        Guid companyId, int year, CancellationToken cancellationToken);

    /// <summary>Tek bir ayın KDV netleştirmesi (tahakkuk fişi için).</summary>
    Task<VatPeriodSummary> GetVatPeriodAsync(
        Guid companyId, int year, int month, CancellationToken cancellationToken);
}

/// <summary>
/// Vergi yükü YÖNETİM GÖRÜNÜMÜ.
///
/// Bu servis beyanname üretmez: müşavirin beyanıyla mutabakat için
/// defterdeki rakamı okur ve ileriye dönük tahmin sunar. Bu yüzden
/// hiçbir yerde "beyan" demez, tahminleri de kaynağıyla birlikte verir.
///
/// KDV rakamları FİŞLERDEN okunur, faturalardan değil: muhasebeleşmemiş
/// bir fatura beyana da girmez. İki kaynak kullanılsaydı ekrandaki rakam
/// müşavirin defterinden farklı çıkardı.
/// </summary>
public sealed class TaxLedgerService(AppDbContext db) : ITaxLedgerService
{
    /// <summary>
    /// KDV tahakkuk fişinin kaynak modülü — dönemin kapatılıp
    /// kapatılmadığı buradan anlaşılır.
    /// </summary>
    public const string VatAccrualSourceModule = "VatAccrual";

    public static string PeriodReference(int year, int month) => $"{year:0000}-{month:00}";

    public async Task<VatPeriodSummary> GetVatPeriodAsync(
        Guid companyId, int year, int month, CancellationToken cancellationToken)
    {
        var periods = await BuildVatPeriodsAsync(companyId, year, cancellationToken);

        return periods.SingleOrDefault(x => x.Month == month)
            ?? throw new ArgumentException("Geçersiz dönem.");
    }

    public async Task<TaxOverview> GetOverviewAsync(
        Guid companyId, int year, CancellationToken cancellationToken)
    {
        var company = await db.Companies
            .AsNoTracking()
            .Where(x => x.Id == companyId)
            .Select(x => new { x.Id })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Şirket bulunamadı.");

        var settings = await db.CompanyFinanceSettings
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new { x.CorporateTaxRate })
            .SingleOrDefaultAsync(cancellationToken);

        var taxRate = settings?.CorporateTaxRate ?? 25m;

        var vat = await BuildVatPeriodsAsync(companyId, year, cancellationToken);
        var payroll = await BuildPayrollPeriodsAsync(companyId, year, cancellationToken);
        var advance = await BuildAdvanceTaxAsync(companyId, year, taxRate, cancellationToken);

        var assumptions = new List<string>
        {
            "Rakamlar kesinleşmiş muhasebe fişlerinden okunur; " +
            "muhasebeleşmemiş belgeler bu görünüme girmez.",
            $"Geçici ve kurumlar vergisi tahminleri %{TurkishFormat.Whole(taxRate)} oranıyla, " +
            "defterdeki ticari kâr üzerinden hesaplandı: 60x satışlar eksi " +
            "61x iade/indirimler eksi gider hesapları. Yansıtma (741/771) " +
            "kullanan şirkette gider 62x/63x'ten, kullanmayanda 7'li " +
            "hesaplardan okunur — ikisi toplansaydı aynı maliyet iki kez " +
            "sayılırdı. " +
            "Kanunen kabul edilmeyen giderler, istisnalar ve geçmiş yıl " +
            "zararları dikkate alınmadı — kesin hesap müşavirindedir.",
            "Bu ekran beyanname üretmez; müşavirin beyanıyla mutabakat için " +
            "hazırlanmıştır."
        };

        var annualProfit = advance.Sum(x => x.ProfitBeforeTax);

        return new TaxOverview(
            company.Id,
            "TRY",
            vat,
            payroll,
            advance,
            taxRate,
            annualProfit > 0m ? decimal.Round(annualProfit * taxRate / 100m, 2) : 0m,
            assumptions);
    }

    /// <summary>
    /// Aylık KDV netleştirmesi ve devreden zinciri.
    ///
    /// Devreden zinciri ay ay yürür: bir ayın devredeni sonraki ayın
    /// indirilecek tarafına eklenir. Zincir kurulmasaydı her ay tek
    /// başına hesaplanır ve devreden KDV kaybolurdu.
    /// </summary>
    private async Task<IReadOnlyList<VatPeriodSummary>> BuildVatPeriodsAsync(
        Guid companyId, int year, CancellationToken cancellationToken)
    {
        var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddYears(1);

        var lines = await db.AccountingVoucherLines
            .AsNoTracking()
            .Where(x => !x.IsDeleted &&
                        !x.AccountingVoucher.IsDeleted &&
                        x.AccountingVoucher.CompanyId == companyId &&
                        x.AccountingVoucher.Status == AccountingVoucherStatus.Posted &&
                        x.AccountingVoucher.VoucherDate >= start &&
                        x.AccountingVoucher.VoucherDate < end &&
                        (x.AccountingAccount.Code.StartsWith("191") ||
                         x.AccountingAccount.Code.StartsWith("391") ||
                         x.AccountingAccount.Code.StartsWith("360.002")) &&
                        // Dönem sonu KDV kapatma fişinin kendisi netleştirmeye
                        // girmez; girseydi kapatılan tutar ikinci kez sayılır
                        // ve devreden zinciri bozulurdu.
                        x.AccountingVoucher.SourceModule != VatAccrualSourceModule)
            .Select(x => new
            {
                x.AccountingVoucher.VoucherDate,
                Code = x.AccountingAccount.Code,
                x.DebitAmountLocal,
                x.CreditAmountLocal
            })
            .ToListAsync(cancellationToken);

        var accruals = await db.AccountingVouchers
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.SourceModule == VatAccrualSourceModule &&
                        x.Status != AccountingVoucherStatus.Cancelled)
            .Select(x => new { x.Id, x.VoucherNumber, x.ReferenceNumber })
            .ToListAsync(cancellationToken);

        var result = new List<VatPeriodSummary>(12);

        // Yılın ilk ayına devreden, bir önceki yıl sonundaki 190 bakiyesi
        // olmalı; defterde 190 hareketi varsa oradan alınır.
        var carryForward = await GetOpeningCarryForwardAsync(
            companyId, start, cancellationToken);

        for (var month = 1; month <= 12; month++)
        {
            var monthLines = lines
                .Where(x => x.VoucherDate.Year == year && x.VoucherDate.Month == month)
                .ToList();

            var output = decimal.Round(monthLines
                .Where(x => x.Code.StartsWith("391", StringComparison.Ordinal))
                .Sum(x => x.CreditAmountLocal - x.DebitAmountLocal), 2);

            var input = decimal.Round(monthLines
                .Where(x => x.Code.StartsWith("191", StringComparison.Ordinal))
                .Sum(x => x.DebitAmountLocal - x.CreditAmountLocal), 2);

            var reverseCharge = decimal.Round(monthLines
                .Where(x => x.Code.StartsWith("360.002", StringComparison.Ordinal))
                .Sum(x => x.CreditAmountLocal - x.DebitAmountLocal), 2);

            var net = decimal.Round(output - input - carryForward, 2);

            var payable = net > 0m ? net : 0m;
            var carryOut = net < 0m ? -net : 0m;

            var reference = PeriodReference(year, month);
            var accrual = accruals.SingleOrDefault(x => x.ReferenceNumber == reference);

            result.Add(new VatPeriodSummary(
                year,
                month,
                $"{month:00}.{year}",
                output,
                input,
                reverseCharge,
                carryForward,
                payable,
                carryOut,
                accrual is not null,
                accrual?.Id,
                accrual?.VoucherNumber));

            carryForward = carryOut;
        }

        return result;
    }

    /// <summary>
    /// Yılbaşı devreden KDV: önceki dönemlerin 190 hesabındaki net
    /// bakiyesi. Hiç hareket yoksa sıfır — açılış fişi girilmemiş
    /// şirkette uydurma bir devreden üretilmez.
    /// </summary>
    private async Task<decimal> GetOpeningCarryForwardAsync(
        Guid companyId, DateTime yearStart, CancellationToken cancellationToken)
    {
        var balance = await db.AccountingVoucherLines
            .AsNoTracking()
            .Where(x => !x.IsDeleted &&
                        !x.AccountingVoucher.IsDeleted &&
                        x.AccountingVoucher.CompanyId == companyId &&
                        x.AccountingVoucher.Status == AccountingVoucherStatus.Posted &&
                        x.AccountingVoucher.VoucherDate < yearStart &&
                        x.AccountingAccount.Code.StartsWith("190"))
            .SumAsync(x => (decimal?)(x.DebitAmountLocal - x.CreditAmountLocal),
                cancellationToken) ?? 0m;

        return balance > 0m ? decimal.Round(balance, 2) : 0m;
    }

    /// <summary>
    /// Bordro kaynaklı vergi ve prim yükü.
    ///
    /// Tahakkuk fişi kesilmişse rakamlar FİŞTEN okunur (defterdeki
    /// gerçek), kesilmemişse onaylı bordrolardan hesaplanır ve
    /// <c>IsAccrued=false</c> ile işaretlenir. İkisi karıştırılsaydı
    /// muhasebeleşmemiş bir dönem defterde varmış gibi görünürdü.
    /// </summary>
    private async Task<IReadOnlyList<PayrollTaxPeriodSummary>> BuildPayrollPeriodsAsync(
        Guid companyId, int year, CancellationToken cancellationToken)
    {
        var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddYears(1);

        var accrualVouchers = await db.AccountingVouchers
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.SourceModule == "PayrollAccrual" &&
                        x.Status == AccountingVoucherStatus.Posted &&
                        x.VoucherDate >= start && x.VoucherDate < end)
            .Select(x => new
            {
                x.Id,
                x.VoucherDate,
                Lines = x.Lines
                    .Where(line => !line.IsDeleted)
                    .Select(line => new
                    {
                        Code = line.AccountingAccount.Code,
                        line.CreditAmountLocal,
                        line.DebitAmountLocal
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var result = new List<PayrollTaxPeriodSummary>(12);

        for (var month = 1; month <= 12; month++)
        {
            var voucher = accrualVouchers.SingleOrDefault(x =>
                x.VoucherDate.Year == year && x.VoucherDate.Month == month);

            if (voucher is not null)
            {
                decimal Net(string prefix) => decimal.Round(voucher.Lines
                    .Where(x => x.Code.StartsWith(prefix, StringComparison.Ordinal))
                    .Sum(x => x.CreditAmountLocal - x.DebitAmountLocal), 2);

                // Fişte gelir vergisi ve damga aynı 360 hesabında;
                // ayrıştırmak için bordro kayıtlarına bakılır.
                var payrollSplit = await GetPayrollSplitAsync(
                    companyId, year, month, cancellationToken);

                var sgkTotal = Net("361");

                result.Add(new PayrollTaxPeriodSummary(
                    year, month, $"{month:00}.{year}",
                    payrollSplit.IncomeTax,
                    payrollSplit.StampTax,
                    payrollSplit.SgkEmployee,
                    payrollSplit.SgkEmployer,
                    sgkTotal > 0m ? sgkTotal : payrollSplit.SgkTotal,
                    decimal.Round(
                        payrollSplit.IncomeTax + payrollSplit.StampTax +
                        (sgkTotal > 0m ? sgkTotal : payrollSplit.SgkTotal), 2),
                    payrollSplit.PersonnelCount,
                    true));

                continue;
            }

            var pending = await GetPayrollSplitAsync(companyId, year, month, cancellationToken);

            if (pending.PersonnelCount == 0)
                continue;

            result.Add(new PayrollTaxPeriodSummary(
                year, month, $"{month:00}.{year}",
                pending.IncomeTax,
                pending.StampTax,
                pending.SgkEmployee,
                pending.SgkEmployer,
                pending.SgkTotal,
                decimal.Round(pending.IncomeTax + pending.StampTax + pending.SgkTotal, 2),
                pending.PersonnelCount,
                false));
        }

        return result;
    }

    private sealed record PayrollSplit(
        decimal IncomeTax,
        decimal StampTax,
        decimal SgkEmployee,
        decimal SgkEmployer,
        decimal SgkTotal,
        int PersonnelCount);

    /// <summary>
    /// Onaylı bordrolardan vergi/prim kırılımı. hr_* kolonları aynı
    /// veritabanında olduğu için ayrı bağlam gerekmiyor.
    /// </summary>
    private async Task<PayrollSplit> GetPayrollSplitAsync(
        Guid companyId, int year, int month, CancellationToken cancellationToken)
    {
        var rows = await db.Database
            .SqlQuery<PayrollTaxRow>($"""
                SELECT
                    COALESCE(SUM("IncomeTaxDeduction"), 0) AS "IncomeTax",
                    COALESCE(SUM("StampTaxDeduction"), 0) AS "StampTax",
                    COALESCE(SUM("SgkEmployeeDeduction" + "UnemploymentEmployeeDeduction"), 0)
                        AS "SgkEmployee",
                    COALESCE(SUM("SgkEmployerAmount" + "UnemploymentEmployerAmount"), 0)
                        AS "SgkEmployer",
                    COUNT(*)::int AS "PersonnelCount"
                FROM hr_payroll_records
                WHERE "CompanyId" = {companyId}
                  AND "Year" = {year}
                  AND "Month" = {month}
                  -- Onaylı (2) ve ödenmiş (3) bordrolar: ödeme sonrası
                  -- durum değiştiği için yalnız 2 aransaydı ödenmiş
                  -- dönemin vergi kırılımı sıfır görünürdü.
                  AND "Status" IN (2, 3)
                  AND "IsDeleted" = FALSE
                """)
            .ToListAsync(cancellationToken);

        var row = rows.SingleOrDefault();

        if (row is null || row.PersonnelCount == 0)
            return new PayrollSplit(0m, 0m, 0m, 0m, 0m, 0);

        return new PayrollSplit(
            decimal.Round(row.IncomeTax, 2),
            decimal.Round(row.StampTax, 2),
            decimal.Round(row.SgkEmployee, 2),
            decimal.Round(row.SgkEmployer, 2),
            decimal.Round(row.SgkEmployee + row.SgkEmployer, 2),
            row.PersonnelCount);
    }

    private sealed record PayrollTaxRow(
        decimal IncomeTax,
        decimal StampTax,
        decimal SgkEmployee,
        decimal SgkEmployer,
        int PersonnelCount);

    /// <summary>
    /// Üç aylık geçici vergi tahmini.
    ///
    /// Matrah muhasebe defterinden: 6xx gelir hesapları − 7xx
    /// maliyet/gider hesapları. Proje maliyet analizinden alınsaydı
    /// projesiz merkez giderleri dışarıda kalır ve kâr olduğundan
    /// yüksek çıkardı.
    ///
    /// Yansıtma hesapları (761/771/741) dışarıda tutulur: onlar 7'li
    /// maliyeti gelir tablosuna aktaran teknik hesaplardır, ikinci kez
    /// sayılırsa gider sıfırlanır.
    /// </summary>
    private async Task<IReadOnlyList<AdvanceTaxPeriodSummary>> BuildAdvanceTaxAsync(
        Guid companyId, int year, decimal taxRate, CancellationToken cancellationToken)
    {
        var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddYears(1);

        var lines = await db.AccountingVoucherLines
            .AsNoTracking()
            .Where(x => !x.IsDeleted &&
                        !x.AccountingVoucher.IsDeleted &&
                        x.AccountingVoucher.CompanyId == companyId &&
                        x.AccountingVoucher.Status == AccountingVoucherStatus.Posted &&
                        x.AccountingVoucher.VoucherDate >= start &&
                        x.AccountingVoucher.VoucherDate < end &&
                        (x.AccountingAccount.Code.StartsWith("6") ||
                         x.AccountingAccount.Code.StartsWith("7")))
            .Select(x => new
            {
                x.AccountingVoucher.VoucherDate,
                Code = x.AccountingAccount.Code,
                x.DebitAmountLocal,
                x.CreditAmountLocal
            })
            .ToListAsync(cancellationToken);

        // Yansıtma hesapları: 741, 751, 761, 771, 781.
        bool IsReflection(string code) =>
            code.StartsWith("741", StringComparison.Ordinal) ||
            code.StartsWith("751", StringComparison.Ordinal) ||
            code.StartsWith("761", StringComparison.Ordinal) ||
            code.StartsWith("771", StringComparison.Ordinal) ||
            code.StartsWith("781", StringComparison.Ordinal);

        // 7/A sistemi kullanılıyor mu: yansıtma hesabında hareket varsa
        // maliyet 7'li hesaplardan 62x/63x'e aktarılıyor demektir.
        //
        // Bu ayrım şart: her ikisi de toplanırsa aynı maliyet iki kez
        // sayılır (740 açıkken 622 de dolu olur), yalnız 7'li alınırsa
        // dönem sonunda 740 kapandığı için gider sıfıra düşer ve kâr
        // olduğundan yüksek çıkar.
        var usesReflection = lines.Any(x =>
            IsReflection(x.Code) && (x.DebitAmountLocal + x.CreditAmountLocal) > 0m);

        // Gelir tablosu gider hesapları: satılan mal maliyeti, faaliyet
        // giderleri, diğer giderler ve finansman gideri.
        bool IsIncomeStatementExpense(string code) =>
            code.StartsWith("62", StringComparison.Ordinal) ||
            code.StartsWith("63", StringComparison.Ordinal) ||
            code.StartsWith("65", StringComparison.Ordinal) ||
            code.StartsWith("66", StringComparison.Ordinal);

        var quarters = new List<AdvanceTaxPeriodSummary>(4);

        for (var quarter = 1; quarter <= 4; quarter++)
        {
            var periodStart = new DateTime(year, (quarter - 1) * 3 + 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var periodEnd = periodStart.AddMonths(3);

            var periodLines = lines
                .Where(x => x.VoucherDate >= periodStart && x.VoucherDate < periodEnd)
                .ToList();

            // Gelir: 60x satışlar eksi 61x satış indirimleri/iadeleri.
            var revenue = decimal.Round(periodLines
                .Where(x => x.Code.StartsWith("60", StringComparison.Ordinal))
                .Sum(x => x.CreditAmountLocal - x.DebitAmountLocal), 2);

            var salesDeductions = decimal.Round(periodLines
                .Where(x => x.Code.StartsWith("61", StringComparison.Ordinal))
                .Sum(x => x.DebitAmountLocal - x.CreditAmountLocal), 2);

            revenue = decimal.Round(revenue - salesDeductions, 2);

            var expense = decimal.Round(periodLines
                .Where(x => usesReflection
                    ? IsIncomeStatementExpense(x.Code)
                    : x.Code.StartsWith("7", StringComparison.Ordinal) && !IsReflection(x.Code))
                .Sum(x => x.DebitAmountLocal - x.CreditAmountLocal), 2);

            var profit = decimal.Round(revenue - expense, 2);

            quarters.Add(new AdvanceTaxPeriodSummary(
                year,
                quarter,
                $"{quarter}. Dönem ({periodStart:MM}-{periodEnd.AddMonths(-1):MM}.{year})",
                periodStart,
                periodEnd.AddDays(-1),
                revenue,
                expense,
                profit,
                taxRate,
                profit > 0m ? decimal.Round(profit * taxRate / 100m, 2) : 0m,
                TaxCalendar.AdvanceTaxDueDate(year, quarter)));
        }

        return quarters;
    }
}
