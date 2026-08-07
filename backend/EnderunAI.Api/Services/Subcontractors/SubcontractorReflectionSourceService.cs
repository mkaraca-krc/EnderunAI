using EnderunAI.Api.Data;
using EnderunAI.Api.Formatting;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Subcontractors;

/// <summary>
/// Yansıtma girdisinin toplanma sonucu.
/// </summary>
/// <param name="Lines">Motora verilecek alt kalemler.</param>
/// <param name="SubcontractorWorkerDays">Taşeron ekibinin dönemdeki
/// fiilen sahada geçen gün sayısı.</param>
/// <param name="FailureReason">Girdi toplanamadıysa nedeni; başarılıysa
/// null.</param>
public sealed record ReflectionSource(
    IReadOnlyList<ReflectionLineInput> Lines,
    decimal SubcontractorWorkerDays,
    string? FailureReason);

/// <summary>
/// Yemek ve konaklama yansıtmasının GİRDİSİNİ toplar: işveren
/// hakedişimizdeki alt kalem birim fiyatları ve taşeron ekibinin
/// puantajdan çıkan gün adedi.
///
/// ZİNCİR: işveren bizden keser → biz taşerondan keseriz. Birim fiyat
/// işverenin bize uyguladığı fiyattır; adet ise taşeron işçilerinin
/// fiilen sahada geçirdiği gündür. İkisinin çarpımı, o taşeronun bize
/// çıkardığı yemek/konaklama yükünün karşılığıdır.
///
/// ADET TANIMI: yalnızca FİİLEN SAHADA geçen günler sayılır
/// (çalışıldı / yarım gün). İzin, rapor, hafta tatili ve uzaktan
/// çalışma sayılmaz — o günlerde şantiyede yemek yenmiyor, yatakhanede
/// kalınmıyor. Yarım gün tam gün sayılır: öğle yemeği yenmiş olur.
///
/// Sonuç ÖNERİDİR: taşeronla mutabakat farklı çıkabilir, ekrandan elle
/// düzeltilir. Uydurma yapılmaz — birim fiyat ya da adet yoksa öneri
/// üretilmez ve nedeni yazılır.
/// </summary>
public sealed class SubcontractorReflectionSourceService(AppDbContext db)
{
    /// <summary>
    /// Sahada fiilen bulunulan puantaj durumları. Yemek ve konaklama
    /// yükü yalnızca bu günlerde doğar.
    /// </summary>
    private static readonly int[] OnSiteStatuses =
    [
        (int)AttendanceStatus.Worked,
        (int)AttendanceStatus.HalfDay
    ];

    /// <summary>
    /// Verilen kesinti türü için yansıtma girdisini toplar.
    /// </summary>
    /// <param name="deductionType">
    /// <see cref="HakedisDeductionType.Meal"/> ya da
    /// <see cref="HakedisDeductionType.Accommodation"/>.</param>
    public async Task<ReflectionSource> BuildAsync(
        SubcontractorContract contract,
        int deductionType,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1);

        var workerDays = await CalculateWorkerDaysAsync(
            contract, periodStart, periodEnd, cancellationToken);

        if (workerDays <= 0m)
        {
            return new ReflectionSource(
                [], 0m,
                "Bu dönemde taşeron ekibine ait sahada geçen puantaj günü yok; " +
                "adet hesaplanamadı.");
        }

        // İşverenin bu dönem bize uyguladığı alt kalem birim fiyatları.
        var employerLines = await db.ProgressPayments
            .AsNoTracking()
            .Where(x => x.ProjectId == contract.ProjectId &&
                        x.ProgressPaymentDate >= periodStart &&
                        x.ProgressPaymentDate < periodEnd &&
                        x.Status != ProgressPaymentStatus.Cancelled)
            .SelectMany(x => x.Deductions)
            .Where(x => x.DeductionType == deductionType)
            .SelectMany(x => x.Lines)
            .Select(x => new { x.Name, x.UnitPrice })
            .ToListAsync(cancellationToken);

        if (employerLines.Count == 0)
        {
            return new ReflectionSource(
                [], workerDays,
                "Bu dönemde işveren hakedişimizde bu başlıkta alt kalem yok; " +
                "yansıtılacak birim fiyat bulunamadı.");
        }

        // Aynı alt kalem birden çok hakedişte geçebilir (ara hakediş);
        // en yüksek birim fiyat değil, ORTALAMA alınır — tek bir
        // dönemde iki farklı fiyat varsa ikisi de gerçektir.
        var lines = employerLines
            .Where(x => x.UnitPrice > 0m)
            .GroupBy(x => x.Name)
            .Select(g => new ReflectionLineInput(
                Name: g.Key,
                EmployerUnitPrice: decimal.Round(g.Average(x => x.UnitPrice), 4),
                SubcontractorQuantity: workerDays))
            .ToList();

        if (lines.Count == 0)
        {
            return new ReflectionSource(
                [], workerDays,
                "İşveren alt kalemlerinde birim fiyat girilmemiş; " +
                "yansıtma hesaplanamadı.");
        }

        return new ReflectionSource(lines, workerDays, null);
    }

    /// <summary>
    /// Taşeron ekibinin dönemde sahada geçirdiği gün sayısı.
    ///
    /// Sözleşme bir şantiyeye bağlıysa yalnızca o şantiyedeki günler
    /// sayılır; aksi hâlde aynı taşeronun başka şantiyedeki günü bu
    /// sözleşmeye yazılırdı.
    /// </summary>
    private async Task<decimal> CalculateWorkerDaysAsync(
        SubcontractorContract contract,
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken cancellationToken)
    {
        var teamIds = await db.Personnel
            .AsNoTracking()
            .Where(x => x.SubcontractorContractId == contract.Id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (teamIds.Count == 0)
            return 0m;

        var query = db.AttendanceRecords
            .AsNoTracking()
            .Where(x => teamIds.Contains(x.PersonnelId) &&
                        x.IsApproved &&
                        x.WorkDate >= periodStart &&
                        x.WorkDate < periodEnd &&
                        OnSiteStatuses.Contains(x.Status));

        query = contract.ProjectSiteId is Guid siteId
            ? query.Where(x => x.ProjectSiteId == siteId)
            : query.Where(x => x.ProjectId == contract.ProjectId);

        return await query.CountAsync(cancellationToken);
    }

    /// <summary>
    /// Taşerona bu dönem çıkılan malzemenin bedeli.
    ///
    /// Depo çıkışında taşeron seçilmemişse tutar üretilmez: projedeki
    /// tüm sarfı taşerona yazmak, olmayan bir borç yaratmak olurdu.
    /// </summary>
    public async Task<(decimal Amount, string Basis)?> BuildMaterialAsync(
        SubcontractorContract contract,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        if (contract.MaterialResponsibility != SubcontractorResponsibility.Us)
            return null;

        var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1);

        var rows = await db.StockMovements
            .AsNoTracking()
            .Where(x => x.SubcontractorContractId == contract.Id &&
                        x.Type == StockMovementType.Issue &&
                        x.MovementDate >= periodStart &&
                        x.MovementDate < periodEnd)
            // TotalCost hareket anında donmuş tutardır; sonradan
            // ortalama maliyet değişse bile geçmiş sarfın bedeli
            // kaymaz. Boşsa miktar × birim maliyetten türetilir.
            .Select(x => new { x.Quantity, x.UnitCost, x.TotalCost })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return null;

        var amount = decimal.Round(
            rows.Sum(x => x.TotalCost ?? (x.Quantity * (x.UnitCost ?? 0m))), 2);

        if (amount <= 0m)
            return null;

        return (amount,
            $"{rows.Count} depo çıkışı, toplam {TurkishFormat.Amount(amount)} " +
            "(taşerona etiketlenmiş sarf)");
    }
}
