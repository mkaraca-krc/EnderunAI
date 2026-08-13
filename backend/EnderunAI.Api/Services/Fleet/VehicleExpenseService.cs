using EnderunAI.Api.Data;
using EnderunAI.Api.Formatting;
using EnderunAI.Api.Models.Expenses;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Fleet;

/// <summary>
/// Tekil masrafın önerilen gider merkezi. <see cref="ProjectId"/> boşsa
/// araç o tarihte merkez havuzundaydı ve masraf merkeze yazılır.
/// </summary>
public sealed record VehicleExpenseCenterSuggestion(
    ExpenseCenterType CenterType,
    Guid CenterId,
    string CenterName,
    Guid? ProjectId,
    /// <summary>
    /// Öneri neye dayanıyor — kullanıcı gerekçeyi görmeden kabul
    /// etmemeli.
    /// </summary>
    string Reason);

public sealed record VehiclePeriodicCostLine(
    Guid? ProjectId,
    string CenterName,
    int Days,
    decimal SharePercent,
    decimal Amount);

public sealed record VehiclePeriodicCostPreview(
    Guid VehicleId,
    string PlateNumber,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    decimal Amount,
    int TotalDays,
    IReadOnlyList<VehiclePeriodicCostLine> Lines);

public sealed record VehiclePeriodicCostRequest(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    decimal Amount,
    Guid ExpenseCategoryId,
    string Description,
    ExpensePaymentMethod PaymentMethod,
    ExpenseDocumentType DocumentType,
    string? DocumentNumber = null,
    Guid? SupplierCurrentAccountId = null,
    Guid? CreditCardId = null,
    /// <summary>
    /// Kullanıcının elle düzelttiği dağıtım. Boşsa gün oranı
    /// kullanılır; doluysa toplamı tutarı BİREBİR kapatmalıdır.
    /// </summary>
    IReadOnlyList<VehicleManualAllocation>? ManualAllocations = null);

public sealed record VehicleManualAllocation(Guid? ProjectId, decimal Amount);

public sealed record VehiclePeriodicCostResult(
    int CreatedEntryCount,
    decimal TotalAmount,
    IReadOnlyList<VehiclePeriodicCostLine> Lines);

public interface IVehicleExpenseService
{
    Task<VehicleExpenseCenterSuggestion?> SuggestCenterAsync(
        Guid vehicleId, DateTime date, CancellationToken cancellationToken);

    Task<VehiclePeriodicCostPreview> PreviewPeriodicAsync(
        Guid vehicleId, DateTime periodStart, DateTime periodEnd, decimal amount,
        CancellationToken cancellationToken);

    Task<VehiclePeriodicCostResult> CreatePeriodicAsync(
        Guid vehicleId, VehiclePeriodicCostRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// ARAÇ MASRAFININ YANSITILMASI.
///
/// İki tip masraf iki farklı kural izler:
///
/// TARİHLİ TEKİL (yakıt, tek bakım, ceza, HGS): masraf tarihinde araç
/// hangi projedeyse oraya. Bu bir ÖNERİdir — kayıt yine normal gider
/// ucundan açılır, kullanıcı merkezi değiştirebilir. Zorla yazılsaydı
/// "aracı dün teslim ettim ama fişi bugün giriyorum" durumu
/// düzeltilemezdi.
///
/// DÖNEMSEL/SABİT (kira, sigorta, kasko, MTV): dönem içinde araç birden
/// çok projedeyse gün oranına göre bölüşür ve HER PAY İÇİN AYRI gider
/// kaydı açılır. Tek kayıt açıp payları başka bir tabloda tutmak,
/// gider merkezi raporunun okumadığı ikinci bir defter demek olurdu.
///
/// ÇİFT SAYIM YOK: payların toplamı tutarın kendisidir; masraf tek
/// defterde (ExpenseEntry) bir kez durur.
/// </summary>
public sealed class VehicleExpenseService(AppDbContext db) : IVehicleExpenseService
{
    /// <summary>Hesaplamada gereken en az araç bilgisi.</summary>
    private sealed record VehicleRef(Guid Id, Guid CompanyId, string PlateNumber);

    public async Task<VehicleExpenseCenterSuggestion?> SuggestCenterAsync(
        Guid vehicleId, DateTime date, CancellationToken cancellationToken)
    {
        var vehicle = await db.Vehicles
            .AsNoTracking()
            .Where(x => x.Id == vehicleId)
            .Select(x => new VehicleRef(x.Id, x.CompanyId, x.PlateNumber))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Araç bulunamadı.");

        var day = VehicleService.AsUtcDate(date);

        var assignment = await db.VehicleAssignments
            .AsNoTracking()
            .Where(x =>
                x.VehicleId == vehicleId &&
                x.StartDate <= day &&
                (x.EndDate == null || x.EndDate > day))
            .OrderByDescending(x => x.StartDate)
            .Select(x => new
            {
                x.ProjectId,
                x.ProjectSiteId,
                ProjectName = x.Project != null ? x.Project.Name : null,
                SiteName = x.ProjectSite != null ? x.ProjectSite.Name : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (assignment?.ProjectSiteId is Guid siteId)
        {
            return new VehicleExpenseCenterSuggestion(
                ExpenseCenterType.ProjectSite, siteId,
                assignment.SiteName ?? "Şantiye",
                assignment.ProjectId,
                $"Araç {day:dd.MM.yyyy} tarihinde bu şantiyedeydi.");
        }

        if (assignment?.ProjectId is Guid projectId)
        {
            return new VehicleExpenseCenterSuggestion(
                ExpenseCenterType.Project, projectId,
                assignment.ProjectName ?? "Proje",
                projectId,
                $"Araç {day:dd.MM.yyyy} tarihinde bu projedeydi.");
        }

        // Atama yoksa ya da merkez havuzundaysa masraf MERKEZ OFİSE
        // önerilir. Şirketin merkez şubesi tanımlı değilse öneri
        // üretilmez: rastgele bir şube seçmek, masrafı yanlış merkeze
        // yazmanın sessiz yolu olurdu.
        var headOffice = await db.Branches
            .AsNoTracking()
            .Where(x => x.CompanyId == vehicle.CompanyId && x.IsHeadOffice)
            .Select(x => new { x.Id, x.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (headOffice is null)
            return null;

        return new VehicleExpenseCenterSuggestion(
            ExpenseCenterType.Branch, headOffice.Id, headOffice.Name, null,
            assignment is null
                ? $"Araç {day:dd.MM.yyyy} tarihinde bir projeye atanmamıştı."
                : $"Araç {day:dd.MM.yyyy} tarihinde merkez havuzundaydı.");
    }

    public async Task<VehiclePeriodicCostPreview> PreviewPeriodicAsync(
        Guid vehicleId, DateTime periodStart, DateTime periodEnd, decimal amount,
        CancellationToken cancellationToken)
    {
        var (vehicle, lines, totalDays) = await BuildAllocationAsync(
            vehicleId, periodStart, periodEnd, amount, null, cancellationToken);

        return new VehiclePeriodicCostPreview(
            vehicle.Id, vehicle.PlateNumber,
            VehicleService.AsUtcDate(periodStart),
            VehicleService.AsUtcDate(periodEnd),
            amount, totalDays, lines);
    }

    public async Task<VehiclePeriodicCostResult> CreatePeriodicAsync(
        Guid vehicleId, VehiclePeriodicCostRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new FleetValidationException("Açıklama girilmelidir.");

        var (vehicle, lines, _) = await BuildAllocationAsync(
            vehicleId, request.PeriodStart, request.PeriodEnd, request.Amount,
            request.ManualAllocations, cancellationToken);

        var categoryExists = await db.ExpenseCategories.AnyAsync(
            x => x.Id == request.ExpenseCategoryId && x.CompanyId == vehicle.CompanyId,
            cancellationToken);

        if (!categoryExists)
            throw new FleetValidationException("Gider kategorisi bulunamadı.");

        var headOfficeId = await db.Branches
            .AsNoTracking()
            .Where(x => x.CompanyId == vehicle.CompanyId && x.IsHeadOffice)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var start = VehicleService.AsUtcDate(request.PeriodStart);
        var end = VehicleService.AsUtcDate(request.PeriodEnd);

        foreach (var line in lines)
        {
            if (line.Amount == 0m)
                continue;

            var entry = new ExpenseEntry
            {
                CompanyId = vehicle.CompanyId,
                ExpenseCategoryId = request.ExpenseCategoryId,

                // Masraf DÖNEM BAŞINDA tarihlenir: dönemsel bir gider
                // tek bir güne yazılmak zorunda ve dönem başı, o dönemin
                // raporuna düşmesini garanti eder.
                ExpenseDate = start,
                Amount = line.Amount,
                Description =
                    $"{request.Description.Trim()} — {vehicle.PlateNumber} " +
                    $"({start:dd.MM.yyyy}-{end:dd.MM.yyyy}, {line.Days} gün)",
                PaymentMethod = request.PaymentMethod,
                DocumentType = request.DocumentType,
                DocumentNumber = string.IsNullOrWhiteSpace(request.DocumentNumber)
                    ? null
                    : request.DocumentNumber.Trim(),
                SupplierCurrentAccountId = request.SupplierCurrentAccountId,
                CreditCardId = request.PaymentMethod == ExpensePaymentMethod.CreditCard
                    ? request.CreditCardId
                    : null,
                VehicleId = vehicle.Id
            };

            if (line.ProjectId is Guid projectId)
            {
                entry.CenterType = ExpenseCenterType.Project;
                entry.ProjectId = projectId;
            }
            else
            {
                if (headOfficeId is null)
                {
                    throw new FleetValidationException(
                        "Merkez payı yazılacak merkez şube tanımlı değil. " +
                        "Şirket ayarlarından merkez şubeyi işaretleyin.");
                }

                entry.CenterType = ExpenseCenterType.Branch;
                entry.BranchId = headOfficeId;
            }

            db.ExpenseEntries.Add(entry);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new VehiclePeriodicCostResult(
            lines.Count(x => x.Amount != 0m),
            lines.Sum(x => x.Amount),
            lines);
    }

    private async Task<(VehicleRef Vehicle, List<VehiclePeriodicCostLine> Lines, int TotalDays)>
        BuildAllocationAsync(
            Guid vehicleId,
            DateTime periodStart,
            DateTime periodEnd,
            decimal amount,
            IReadOnlyList<VehicleManualAllocation>? manual,
            CancellationToken cancellationToken)
    {
        var vehicle = await db.Vehicles
            .AsNoTracking()
            .Where(x => x.Id == vehicleId)
            .Select(x => new VehicleRef(x.Id, x.CompanyId, x.PlateNumber))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Araç bulunamadı.");

        if (amount <= 0m)
            throw new FleetValidationException("Tutar sıfırdan büyük olmalıdır.");

        var start = VehicleService.AsUtcDate(periodStart);
        var end = VehicleService.AsUtcDate(periodEnd);

        if (end < start)
            throw new FleetValidationException("Dönem bitişi başlangıcından önce olamaz.");

        var assignments = await db.VehicleAssignments
            .AsNoTracking()
            .Where(x => x.VehicleId == vehicleId && x.StartDate <= end)
            .Select(x => new { x.ProjectId, x.StartDate, x.EndDate })
            .ToListAsync(cancellationToken);

        var segments = VehicleCostAllocationCalculator.BuildSegments(
            assignments.Select(x => (x.ProjectId, x.StartDate, x.EndDate)).ToList(),
            start, end);

        var allocation = VehicleCostAllocationCalculator.Allocate(segments, amount);

        var projectIds = allocation
            .Where(x => x.ProjectId.HasValue)
            .Select(x => x.ProjectId!.Value)
            .ToList();

        var projectNames = await db.Projects
            .AsNoTracking()
            .Where(x => projectIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => $"{x.Code} · {x.Name}", cancellationToken);

        var lines = allocation
            .Select(x => new VehiclePeriodicCostLine(
                x.ProjectId,
                x.ProjectId is Guid id
                    ? projectNames.GetValueOrDefault(id, "Proje")
                    : "Merkez",
                x.Days,
                x.SharePercent,
                x.Amount))
            .ToList();

        if (manual is { Count: > 0 })
            lines = ApplyManual(lines, manual, amount);

        return (vehicle, lines, segments.Sum(x => x.Days));
    }

    /// <summary>
    /// Kullanıcının elle düzelttiği dağıtım.
    ///
    /// TOPLAM YİNE %100 KAPANMALI: elle girilen paylar tutarı birebir
    /// karşılamıyorsa kayıt REDDEDİLİR. Otomatik düzeltme yapılsaydı
    /// kullanıcı girdiği rakamı değil, sistemin uydurduğunu görürdü.
    /// </summary>
    private static List<VehiclePeriodicCostLine> ApplyManual(
        List<VehiclePeriodicCostLine> lines,
        IReadOnlyList<VehicleManualAllocation> manual,
        decimal amount)
    {
        var total = manual.Sum(x => x.Amount);

        if (decimal.Round(total, 2) != decimal.Round(amount, 2))
        {
            throw new FleetValidationException(
                $"Elle girilen payların toplamı {TurkishFormat.Amount(total)}, " +
                $"tutar {TurkishFormat.Amount(amount)}. " +
                "Dağıtım tutarı birebir karşılamalıdır.");
        }

        if (manual.Any(x => x.Amount < 0m))
            throw new FleetValidationException("Pay tutarı negatif olamaz.");

        var byProject = lines.ToDictionary(x => x.ProjectId ?? Guid.Empty);

        return manual
            .Select(x =>
            {
                var key = x.ProjectId ?? Guid.Empty;

                // Elle eklenen merkez, otomatik dağıtımda yoksa da
                // kabul edilir: kullanıcı aracın kayıtta görünmeyen bir
                // kullanımını biliyor olabilir. Gün sayısı sıfır kalır.
                return byProject.TryGetValue(key, out var line)
                    ? line with { Amount = decimal.Round(x.Amount, 2) }
                    : new VehiclePeriodicCostLine(
                        x.ProjectId,
                        x.ProjectId is null ? "Merkez" : "Proje",
                        0, 0m, decimal.Round(x.Amount, 2));
            })
            .ToList();
    }
}
