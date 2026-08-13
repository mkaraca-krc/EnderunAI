using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Fleet;
using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Notifications;
using EnderunAI.Api.Services.Notifications.Sources;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ARAÇ YENİLEME HATIRLATMALARI: muayene, sigorta, kasko, MTV,
/// periyodik bakım.
///
/// İki kural burada sınanıyor:
/// - İDEMPOTENT: aynı yenileme için ikinci tarama YENİ kayıt açmaz
///   (Tür + KaynakId=araç + Dönem).
/// - OTOMATİK KAPANIŞ: yenileme yapılıp tarih ileri alınınca kaynak
///   aday üretmez ve kayıt kapanır. Ayrı bir "kapat" düğmesi olsaydı
///   yenileme yapılır ama bildirim açık kalır, ya da tersi olurdu.
/// </summary>
[Collection("Integration")]
public sealed class FleetNotificationSourceTests(DatabaseFixture fixture)
{
    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private async Task<(Guid CompanyId, Guid VehicleId)> CreateVehicleAsync(
        int? inspectionInDays = null,
        int? insuranceInDays = null,
        int? motorTaxInDays = null,
        int? maintenanceInDays = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        DateTime? At(int? days) => days is int value
            ? DateTime.SpecifyKind(Today.AddDays(value), DateTimeKind.Utc)
            : null;

        var vehicle = new Vehicle
        {
            CompanyId = project.CompanyId,
            PlateNumber = $"06BLD{suffix[..3].ToUpperInvariant()}",
            Type = VehicleType.Car,
            Ownership = VehicleOwnership.Owned,
            InspectionDueDate = At(inspectionInDays),
            InsuranceRenewalDate = At(insuranceInDays),
            MotorTaxDueDate = At(motorTaxInDays),
            NextMaintenanceDate = At(maintenanceInDays)
        };

        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();

        return (project.CompanyId, vehicle.Id);
    }

    private async Task<IReadOnlyList<NotificationCandidate>> BuildAsync(Guid companyId)
    {
        using var scope = fixture.Factory.Services.CreateScope();

        var source = scope.ServiceProvider
            .GetRequiredService<IEnumerable<INotificationSource>>()
            .OfType<VehicleRenewalNotificationSource>()
            .Single();

        return await source.BuildAsync(
            new NotificationScanContext(companyId, Today), CancellationToken.None);
    }

    private async Task<NotificationScanReport> ScanAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();

        var scanner = scope.ServiceProvider.GetRequiredService<NotificationScanner>();

        return await scanner.RunAsync(DateTime.UtcNow, CancellationToken.None);
    }

    [Fact]
    public async Task YaklasanMuayene_AdayUretir()
    {
        var (companyId, vehicleId) = await CreateVehicleAsync(inspectionInDays: 3);

        var candidates = await BuildAsync(companyId);

        var candidate = Assert.Single(candidates);

        Assert.Equal(VehicleRenewalNotificationSource.InspectionTypeKey, candidate.Type);
        Assert.Equal(vehicleId, candidate.SourceId);
        Assert.Contains("muayene", candidate.Title);
        Assert.Equal(NotificationSeverity.Warning, candidate.Severity);

        // Yetki bildirimin üzerinde taşınıyor: tarama kullanıcısız
        // koşuyor, süzme okuma anında yapılıyor.
        Assert.Equal(PermissionCatalog.Keys.VehicleView, candidate.RequiredPermission);
    }

    /// <summary>
    /// Her yenileme türü AYRI bildirim: sigorta yenilenince o satır
    /// kapanmalı, muayene açık kalmalı.
    /// </summary>
    [Fact]
    public async Task HerTur_AyriAdayUretir()
    {
        var (companyId, _) = await CreateVehicleAsync(
            inspectionInDays: 2, insuranceInDays: 4,
            motorTaxInDays: 6, maintenanceInDays: 1);

        var candidates = await BuildAsync(companyId);

        Assert.Equal(4, candidates.Count);
        Assert.Equal(4, candidates.Select(x => x.Type).Distinct().Count());
    }

    /// <summary>Uzaktaki yenileme gürültü yapmaz.</summary>
    [Fact]
    public async Task UzakTarih_AdayUretmez()
    {
        var (companyId, _) = await CreateVehicleAsync(inspectionInDays: 60);

        Assert.Empty(await BuildAsync(companyId));
    }

    /// <summary>Geçmiş tarih KRİTİK: geciken muayene yaklaşandan acildir.</summary>
    [Fact]
    public async Task GecmisTarih_KritikSayilir()
    {
        var (companyId, _) = await CreateVehicleAsync(inspectionInDays: -5);

        var candidate = Assert.Single(await BuildAsync(companyId));

        Assert.Equal(NotificationSeverity.Critical, candidate.Severity);
        Assert.Contains("gecikti", candidate.Title);
    }

    /// <summary>
    /// İDEMPOTENT: iki tarama arasında hiçbir şey değişmediyse ikinci
    /// tur yeni kayıt açmaz. Açsaydı bildirim merkezi her turda aynı
    /// işi bir kez daha listeler ve okunmaz hâle gelirdi.
    /// </summary>
    [Fact]
    public async Task IkinciTarama_YeniKayitAcmaz()
    {
        var (companyId, vehicleId) = await CreateVehicleAsync(inspectionInDays: 3);

        await ScanAsync();
        await ScanAsync();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var count = await db.Notifications
            .CountAsync(x =>
                x.SourceId == vehicleId &&
                x.Type == VehicleRenewalNotificationSource.InspectionTypeKey);

        Assert.Equal(1, count);
    }

    /// <summary>
    /// OTOMATİK KAPANIŞ: muayene yapılıp tarih bir yıl ileri alınınca
    /// kaynak aday üretmez ve açık kayıt kapanır.
    /// </summary>
    [Fact]
    public async Task YenilemeYapilinca_BildirimKapanir()
    {
        var (companyId, vehicleId) = await CreateVehicleAsync(inspectionInDays: 3);

        await ScanAsync();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var open = await db.Notifications.SingleAsync(x =>
                x.SourceId == vehicleId &&
                x.Type == VehicleRenewalNotificationSource.InspectionTypeKey);

            Assert.Equal(NotificationStatus.Open, open.Status);

            // Muayene yapıldı: tarih ileri alındı.
            var vehicle = await db.Vehicles.SingleAsync(x => x.Id == vehicleId);
            vehicle.InspectionDueDate = DateTime.SpecifyKind(
                Today.AddYears(1), DateTimeKind.Utc);

            await db.SaveChangesAsync();
        }

        Assert.Empty(await BuildAsync(companyId));

        await ScanAsync();

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var notification = await verifyDb.Notifications.SingleAsync(x =>
            x.SourceId == vehicleId &&
            x.Type == VehicleRenewalNotificationSource.InspectionTypeKey);

        Assert.NotEqual(NotificationStatus.Open, notification.Status);
    }

    /// <summary>
    /// Pasif araç hatırlatma üretmez: satılan aracın muayenesi bizim
    /// işimiz değil.
    /// </summary>
    [Fact]
    public async Task PasifArac_AdayUretmez()
    {
        var (companyId, vehicleId) = await CreateVehicleAsync(inspectionInDays: 3);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var vehicle = await db.Vehicles.SingleAsync(x => x.Id == vehicleId);
            vehicle.IsActive = false;

            await db.SaveChangesAsync();
        }

        Assert.Empty(await BuildAsync(companyId));
    }
}
