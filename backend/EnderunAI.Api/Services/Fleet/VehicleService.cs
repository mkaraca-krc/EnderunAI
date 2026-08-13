using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Fleet;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Fleet;

public sealed record SaveVehicleRequest(
    Guid CompanyId,
    string PlateNumber,
    int Type,
    int Ownership,
    string? Brand = null,
    string? Model = null,
    string? ChassisNumber = null,
    int? ModelYear = null,
    int? FuelType = null,
    Guid? LessorCurrentAccountId = null,
    decimal? RentAmount = null,
    int? RentPeriod = null,
    int? RentDueDay = null,
    DateTime? PurchaseDate = null,
    decimal? PurchaseCost = null,
    DateTime? InspectionDueDate = null,
    DateTime? InsuranceRenewalDate = null,
    DateTime? CascoRenewalDate = null,
    DateTime? MotorTaxDueDate = null,
    DateTime? NextMaintenanceDate = null,
    string? Notes = null);

public sealed record AssignVehicleRequest(
    /// <summary>Boşsa araç MERKEZ HAVUZUNA alınır.</summary>
    Guid? ProjectId,
    Guid? ProjectSiteId,
    Guid? DriverPersonnelId,
    DateTime StartDate,
    string? Notes = null,
    /// <summary>Tekrar anahtarı — aynı anahtarla ikinci atama açılmaz.</summary>
    string? ReferenceKey = null);

public sealed class FleetValidationException(string message) : Exception(message);

public interface IVehicleService
{
    Task<Vehicle> CreateAsync(SaveVehicleRequest request, CancellationToken cancellationToken);

    Task<Vehicle> UpdateAsync(
        Guid id, SaveVehicleRequest request, CancellationToken cancellationToken);

    Task<VehicleAssignment> AssignAsync(
        Guid vehicleId, AssignVehicleRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Aracın VERİLEN TARİHTEKİ ataması. Masraf yansıtmasının dayanağı:
    /// "bu masraf yapıldığında araç neredeydi".
    /// </summary>
    Task<VehicleAssignment?> GetAssignmentOnAsync(
        Guid vehicleId, DateTime date, CancellationToken cancellationToken);
}

/// <summary>
/// Araç kartı ve atamaları.
///
/// MASRAF BURADA TUTULMAZ: araç masrafı gider kaydına
/// (<c>ExpenseEntry.VehicleId</c>) yazılır. Bu servis yalnız kartı ve
/// aracın nerede olduğunu yönetir.
/// </summary>
public sealed class VehicleService(AppDbContext db) : IVehicleService
{
    public async Task<Vehicle> CreateAsync(
        SaveVehicleRequest request, CancellationToken cancellationToken)
    {
        Validate(request);

        var plate = NormalizePlate(request.PlateNumber);

        // Plaka çakışması veritabanı kısıtına düşmeden önce anlaşılır
        // bir mesajla dönmeli; kısıt son savunma hattı olarak duruyor.
        var exists = await db.Vehicles.AnyAsync(
            x => x.CompanyId == request.CompanyId && x.PlateNumber == plate,
            cancellationToken);

        if (exists)
            throw new FleetValidationException($"{plate} plakalı araç zaten kayıtlı.");

        var vehicle = new Vehicle { CompanyId = request.CompanyId };

        Apply(vehicle, request, plate);

        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(cancellationToken);

        return vehicle;
    }

    public async Task<Vehicle> UpdateAsync(
        Guid id, SaveVehicleRequest request, CancellationToken cancellationToken)
    {
        Validate(request);

        var vehicle = await db.Vehicles
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Araç bulunamadı.");

        var plate = NormalizePlate(request.PlateNumber);

        var exists = await db.Vehicles.AnyAsync(
            x => x.CompanyId == vehicle.CompanyId &&
                 x.PlateNumber == plate &&
                 x.Id != id,
            cancellationToken);

        if (exists)
            throw new FleetValidationException($"{plate} plakalı başka bir araç var.");

        Apply(vehicle, request, plate);

        await db.SaveChangesAsync(cancellationToken);

        return vehicle;
    }

    public async Task<VehicleAssignment> AssignAsync(
        Guid vehicleId, AssignVehicleRequest request, CancellationToken cancellationToken)
    {
        var vehicle = await db.Vehicles
            .SingleOrDefaultAsync(x => x.Id == vehicleId, cancellationToken)
            ?? throw new KeyNotFoundException("Araç bulunamadı.");

        if (request.ProjectId is null && request.ProjectSiteId is not null)
        {
            throw new FleetValidationException(
                "Şantiye seçildiyse proje de seçilmelidir.");
        }

        if (request.ProjectId is Guid projectId)
        {
            var projectExists = await db.Projects.AnyAsync(
                x => x.Id == projectId && x.CompanyId == vehicle.CompanyId,
                cancellationToken);

            if (!projectExists)
            {
                throw new FleetValidationException(
                    "Proje bulunamadı ya da aracın şirketine ait değil.");
            }
        }

        var startDate = AsUtcDate(request.StartDate);

        // TEKRAR ANAHTARI: aynı anahtarla gelen ikinci istek yeni atama
        // AÇMAZ, mevcudu döner. Ağ tekrarında ya da çift tıklamada araç
        // iki kez atanmış görünmesin.
        if (!string.IsNullOrWhiteSpace(request.ReferenceKey))
        {
            var key = request.ReferenceKey.Trim();

            var existing = await db.VehicleAssignments
                .SingleOrDefaultAsync(
                    x => x.VehicleId == vehicleId && x.ReferenceKey == key,
                    cancellationToken);

            if (existing is not null)
                return existing;
        }

        var open = await db.VehicleAssignments
            .Where(x => x.VehicleId == vehicleId && x.EndDate == null)
            .ToListAsync(cancellationToken);

        foreach (var previous in open)
        {
            if (previous.StartDate > startDate)
            {
                throw new FleetValidationException(
                    "Yeni atama, açık atamanın başlangıcından önce olamaz.");
            }

            // Önceki atama KAPATILIR, SİLİNMEZ: masraf yansıtması
            // geçmişe dönük "o tarihte araç neredeydi" diye soruyor.
            previous.EndDate = startDate;
        }

        var assignment = new VehicleAssignment
        {
            VehicleId = vehicleId,
            ProjectId = request.ProjectId,
            ProjectSiteId = request.ProjectSiteId,
            DriverPersonnelId = request.DriverPersonnelId,
            StartDate = startDate,
            Notes = request.Notes?.Trim(),
            ReferenceKey = string.IsNullOrWhiteSpace(request.ReferenceKey)
                ? null
                : request.ReferenceKey.Trim()
        };

        db.VehicleAssignments.Add(assignment);
        await db.SaveChangesAsync(cancellationToken);

        return assignment;
    }

    public async Task<VehicleAssignment?> GetAssignmentOnAsync(
        Guid vehicleId, DateTime date, CancellationToken cancellationToken)
    {
        var day = AsUtcDate(date);

        return await db.VehicleAssignments
            .AsNoTracking()
            .Where(x =>
                x.VehicleId == vehicleId &&
                x.StartDate <= day &&
                (x.EndDate == null || x.EndDate > day))
            .OrderByDescending(x => x.StartDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Plaka biçimi: boşluklar atılır, büyük harfe çevrilir. Aynı araç
    /// "06 ABC 123" ve "06ABC123" diye iki kez açılmasın.
    /// </summary>
    public static string NormalizePlate(string plate) =>
        plate.Replace(" ", string.Empty).Replace("-", string.Empty)
            .ToUpperInvariant();

    /// <summary>
    /// Tarihi UTC gününe sabitler — gider kaydındaki kuralın aynısı;
    /// Npgsql timestamptz kolonuna Kind=Unspecified yazmayı reddediyor.
    /// </summary>
    public static DateTime AsUtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private static void Validate(SaveVehicleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlateNumber))
            throw new FleetValidationException("Plaka girilmelidir.");

        if (!Enum.IsDefined(typeof(VehicleType), request.Type))
            throw new FleetValidationException("Geçersiz araç tipi.");

        if (!Enum.IsDefined(typeof(VehicleOwnership), request.Ownership))
            throw new FleetValidationException("Geçersiz sahiplik.");

        if (request.FuelType is int fuel && !Enum.IsDefined(typeof(VehicleFuelType), fuel))
            throw new FleetValidationException("Geçersiz yakıt tipi.");

        if (request.RentPeriod is int period &&
            !Enum.IsDefined(typeof(VehicleRentPeriod), period))
        {
            throw new FleetValidationException("Geçersiz kira dönemi.");
        }

        if (request.RentDueDay is int day and (< 1 or > 31))
            throw new FleetValidationException("Kira vadesi ayın 1-31'i arasında olmalı.");

        var ownership = (VehicleOwnership)request.Ownership;

        // KİRALIKTA KİRA BEDELİ ZORUNLU: bedeli olmayan kira nakit
        // akışa hiç düşmez ve araç "bedava" görünür.
        if (ownership == VehicleOwnership.Rented &&
            (request.RentAmount is null or <= 0))
        {
            throw new FleetValidationException(
                "Kiralık araçta kira bedeli girilmelidir.");
        }

        if (ownership == VehicleOwnership.Owned &&
            request.RentAmount is > 0)
        {
            throw new FleetValidationException(
                "Öz mal araçta kira bedeli olamaz.");
        }
    }

    private static void Apply(Vehicle vehicle, SaveVehicleRequest request, string plate)
    {
        vehicle.PlateNumber = plate;
        vehicle.Type = (VehicleType)request.Type;
        vehicle.Ownership = (VehicleOwnership)request.Ownership;
        vehicle.Brand = request.Brand?.Trim();
        vehicle.Model = request.Model?.Trim();
        vehicle.ChassisNumber = request.ChassisNumber?.Trim();
        vehicle.ModelYear = request.ModelYear;
        vehicle.FuelType = request.FuelType is int fuel
            ? (VehicleFuelType)fuel
            : null;

        vehicle.LessorCurrentAccountId = request.LessorCurrentAccountId;
        vehicle.RentAmount = request.RentAmount;
        vehicle.RentPeriod = request.RentPeriod is int period
            ? (VehicleRentPeriod)period
            : null;
        vehicle.RentDueDay = request.RentDueDay;

        vehicle.PurchaseDate = AsUtcDateOrNull(request.PurchaseDate);
        vehicle.PurchaseCost = request.PurchaseCost;

        vehicle.InspectionDueDate = AsUtcDateOrNull(request.InspectionDueDate);
        vehicle.InsuranceRenewalDate = AsUtcDateOrNull(request.InsuranceRenewalDate);
        vehicle.CascoRenewalDate = AsUtcDateOrNull(request.CascoRenewalDate);
        vehicle.MotorTaxDueDate = AsUtcDateOrNull(request.MotorTaxDueDate);
        vehicle.NextMaintenanceDate = AsUtcDateOrNull(request.NextMaintenanceDate);

        vehicle.Notes = request.Notes?.Trim();
    }

    private static DateTime? AsUtcDateOrNull(DateTime? value) =>
        value is DateTime date ? AsUtcDate(date) : null;
}
