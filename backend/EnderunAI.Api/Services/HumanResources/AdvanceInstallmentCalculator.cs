namespace EnderunAI.Api.Services.HumanResources;

public sealed record AdvanceInstallment(
    int Number, int Year, int Month, decimal Amount);

public sealed record AdvancePlan(
    decimal ApprovedAmount,
    int InstallmentCount,
    IReadOnlyList<AdvanceInstallment> Installments);

/// <param name="AlreadyDeducted">Bu avanstan bugüne kadar kesilmiş
/// toplam. Hesaplanan dönemin kendi kesintisi HARİÇ olmalı; aksi halde
/// bordro yeniden hesaplanınca kesinti iki kez sayılırdı.</param>
public sealed record AdvanceDeductionInput(
    Guid AdvanceId,
    decimal ApprovedAmount,
    int InstallmentCount,
    DateOnly FirstDeductionDate,
    decimal AlreadyDeducted);

/// <param name="ScheduledAmount">Plana göre bu döneme kadar kesilmiş
/// olması gereken ama kesilmemiş tutar (gecikmiş taksitler dahil).</param>
public sealed record AdvanceDeductionLine(
    Guid AdvanceId,
    decimal ScheduledAmount,
    decimal Amount);

/// <param name="Uncovered">Neti yetmediği için bu ay kesilemeyen
/// tutar. Kayıp değildir: kesilmediği için kalan bakiye düşmez ve
/// gelecek ay yeniden gündeme gelir.</param>
public sealed record AdvanceDeductionResult(
    IReadOnlyList<AdvanceDeductionLine> Lines,
    decimal Total,
    decimal Uncovered);

/// <summary>
/// Avans taksitleri ve bordrodan kesintisi.
///
/// Saf ve veritabanısız.
///
/// DENETİMDE BULUNAN EKSİK: taksit sayısı ve ilk kesinti tarihi
/// alanları vardı, bordro kaydında AdvanceDeduction alanı vardı ve
/// hesaba giriyordu — ama o alana KOD HİÇBİR YERDE DEĞER YAZMIYORDU.
/// Kullanıcı taksit giriyor, bordro çalışıyor, kesinti sıfır kalıyordu.
///
/// İki karar:
///   - Yalnızca ÖDENMİŞ avans kesilir. Verilmemiş parayı geri almak
///     olmaz; onaylı ama ödenmemiş avans bekler.
///   - Kesinti o ayın NETİNİ aşamaz. Aşan kısım kaybolmaz; kesilmediği
///     için bakiye düşmez ve gelecek ay "gecikmiş taksit" olarak
///     yeniden gündeme gelir.
/// </summary>
public static class AdvanceInstallmentCalculator
{
    /// <summary>
    /// Taksit planı. Kuruş artığı SON taksite bindirilir; taksitlerin
    /// toplamı her zaman onaylanan tutara birebir eşittir.
    /// </summary>
    public static AdvancePlan BuildPlan(
        decimal approvedAmount, int installmentCount, DateOnly firstDeductionDate)
    {
        if (approvedAmount <= 0m)
            return new AdvancePlan(0m, 0, []);

        var count = Math.Max(1, installmentCount);
        var perInstallment = decimal.Round(approvedAmount / count, 2);

        var installments = new List<AdvanceInstallment>(count);
        var allocated = 0m;

        for (var index = 0; index < count; index++)
        {
            var period = firstDeductionDate.AddMonths(index);

            var amount = index == count - 1
                ? decimal.Round(approvedAmount - allocated, 2)
                : perInstallment;

            allocated += amount;

            installments.Add(new AdvanceInstallment(
                index + 1, period.Year, period.Month, amount));
        }

        return new AdvancePlan(approvedAmount, count, installments);
    }

    /// <summary>
    /// Bir avanstan, verilen döneme KADAR kesilmiş olması gereken
    /// toplam. Geçmiş aylarda kesilememiş taksitler de bu toplamda
    /// olduğu için gecikmeler kendiliğinden telafi edilir.
    /// </summary>
    public static decimal ScheduledThrough(
        AdvanceDeductionInput advance, int year, int month)
    {
        var plan = BuildPlan(
            advance.ApprovedAmount, advance.InstallmentCount,
            advance.FirstDeductionDate);

        var scheduled = plan.Installments
            .Where(x => x.Year < year || (x.Year == year && x.Month <= month))
            .Sum(x => x.Amount);

        return decimal.Round(scheduled, 2);
    }

    /// <summary>
    /// Dönemin avans kesintisi.
    ///
    /// En eski avanstan başlanır: birden fazla açık avans varsa önce
    /// verileni kapatmak, hem borcun yaşlanmasını engeller hem de
    /// kullanıcının beklediği sıradır.
    /// </summary>
    public static AdvanceDeductionResult Resolve(
        IReadOnlyCollection<AdvanceDeductionInput> advances,
        int year,
        int month,
        decimal availableNet)
    {
        var lines = new List<AdvanceDeductionLine>();
        var remainingNet = Math.Max(0m, decimal.Round(availableNet, 2));
        var total = 0m;
        var uncovered = 0m;

        var ordered = advances
            .OrderBy(x => x.FirstDeductionDate)
            .ThenBy(x => x.AdvanceId)
            .ToList();

        foreach (var advance in ordered)
        {
            var scheduled = ScheduledThrough(advance, year, month);

            // Kalan borç: onaylanan tutardan bugüne kadar kesilen düşülür.
            var balance = decimal.Round(
                advance.ApprovedAmount - advance.AlreadyDeducted, 2);

            var due = Math.Min(
                decimal.Round(scheduled - advance.AlreadyDeducted, 2),
                balance);

            if (due <= 0m)
                continue;

            var amount = Math.Min(due, remainingNet);

            if (amount > 0m)
            {
                lines.Add(new AdvanceDeductionLine(advance.AdvanceId, due, amount));
                remainingNet -= amount;
                total += amount;
            }

            uncovered += due - amount;
        }

        return new AdvanceDeductionResult(
            lines,
            decimal.Round(total, 2),
            decimal.Round(uncovered, 2));
    }
}
