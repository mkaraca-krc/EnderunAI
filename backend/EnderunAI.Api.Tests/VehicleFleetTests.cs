using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Fleet;
using EnderunAI.Api.Services.Fleet;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ARAÇ KARTI VE ATAMA (V1).
///
/// Aracın nerede olduğu, masrafın hangi merkeze yansıyacağını
/// belirleyecek; bu yüzden atama kuralları burada sıkı sınanıyor:
/// aynı anda tek açık atama, geçmişin korunması, tekrar anahtarıyla
/// idempotentlik ve "o tarihte araç neredeydi" sorgusu.
/// </summary>
[Collection("Integration")]
public sealed class VehicleFleetTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid ProjectId, Guid OtherProjectId);

    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var other = new Project
        {
            CompanyId = project.CompanyId,
            BranchId = project.BranchId,
            EmployerCurrentAccountId = project.EmployerCurrentAccountId,
            Code = $"PRJ2-{suffix}",
            Name = $"İkinci proje {suffix}",
            CurrencyCode = "TRY",
            Status = ProjectStatus.Active
        };

        db.Projects.Add(other);
        await db.SaveChangesAsync();

        return new Context(project.CompanyId, project.Id, other.Id);
    }

    private async Task<T> WithServiceAsync<T>(Func<IVehicleService, Task<T>> action)
    {
        using var scope = fixture.Factory.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<IVehicleService>());
    }

    private static SaveVehicleRequest OwnedVehicle(Guid companyId, string plate) =>
        new(companyId, plate, (int)VehicleType.Pickup, (int)VehicleOwnership.Owned);

    [Fact]
    public async Task Arac_ElleAcilir()
    {
        var context = await CreateContextAsync();

        var vehicle = await WithServiceAsync(service => service.CreateAsync(
            OwnedVehicle(context.CompanyId, "06 ABC 123") with
            {
                Brand = "Ford",
                Model = "Ranger",
                ModelYear = 2022,
                InspectionDueDate = new DateTime(2027, 3, 1)
            },
            CancellationToken.None));

        // Plaka boşluksuz ve büyük harf saklanır: aynı araç iki farklı
        // yazımla iki kez açılmasın.
        Assert.Equal("06ABC123", vehicle.PlateNumber);
        Assert.Equal(VehicleOwnership.Owned, vehicle.Ownership);
    }

    [Fact]
    public async Task AyniPlaka_IkinciKezAcilamaz()
    {
        var context = await CreateContextAsync();

        await WithServiceAsync(service => service.CreateAsync(
            OwnedVehicle(context.CompanyId, "34XYZ99"), CancellationToken.None));

        var error = await Assert.ThrowsAsync<FleetValidationException>(() =>
            WithServiceAsync(service => service.CreateAsync(
                // Boşluklu yazım da aynı plakadır.
                OwnedVehicle(context.CompanyId, "34 XYZ 99"), CancellationToken.None)));

        Assert.Contains("zaten kayıtlı", error.Message);
    }

    /// <summary>
    /// Kiralık araçta kira bedeli zorunlu: bedeli olmayan kira nakit
    /// akışa hiç düşmez ve araç "bedava" görünür.
    /// </summary>
    [Fact]
    public async Task KiralikArac_KiraBedelsizAcilamaz()
    {
        var context = await CreateContextAsync();

        var error = await Assert.ThrowsAsync<FleetValidationException>(() =>
            WithServiceAsync(service => service.CreateAsync(
                new SaveVehicleRequest(
                    context.CompanyId, "35KRA01",
                    (int)VehicleType.Van, (int)VehicleOwnership.Rented),
                CancellationToken.None)));

        Assert.Contains("kira bedeli", error.Message);
    }

    [Fact]
    public async Task OzMalArac_KiraBedeliKabulEtmez()
    {
        var context = await CreateContextAsync();

        await Assert.ThrowsAsync<FleetValidationException>(() =>
            WithServiceAsync(service => service.CreateAsync(
                OwnedVehicle(context.CompanyId, "06OZM01") with { RentAmount = 5000m },
                CancellationToken.None)));
    }

    /// <summary>
    /// Yeni atama açılınca öncekinin bitiş tarihi yazılır — SİLİNMEZ.
    /// Masraf yansıtması geçmişe dönük "o tarihte araç neredeydi" diye
    /// soruyor; geçmiş silinseydi eski masraf yanlış projeye düşerdi.
    /// </summary>
    [Fact]
    public async Task YeniAtama_EskisiniKapatir_GecmisKorunur()
    {
        var context = await CreateContextAsync();

        var vehicle = await WithServiceAsync(service => service.CreateAsync(
            OwnedVehicle(context.CompanyId, "06GEC01"), CancellationToken.None));

        await WithServiceAsync(service => service.AssignAsync(
            vehicle.Id,
            new AssignVehicleRequest(
                context.ProjectId, null, null, new DateTime(2026, 1, 1)),
            CancellationToken.None));

        await WithServiceAsync(service => service.AssignAsync(
            vehicle.Id,
            new AssignVehicleRequest(
                context.OtherProjectId, null, null, new DateTime(2026, 4, 1)),
            CancellationToken.None));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var assignments = await db.VehicleAssignments
            .Where(x => x.VehicleId == vehicle.Id)
            .OrderBy(x => x.StartDate)
            .ToListAsync();

        Assert.Equal(2, assignments.Count);
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), assignments[0].EndDate);
        Assert.Null(assignments[1].EndDate);
    }

    /// <summary>
    /// Aynı anda tek AÇIK atama. İkinci açık atama olsaydı araç iki
    /// projede birden görünür ve masraf hangisine düşeceği belirsiz
    /// kalırdı.
    /// </summary>
    [Fact]
    public async Task AracinAyniAnda_TekAcikAtamasiOlur()
    {
        var context = await CreateContextAsync();

        var vehicle = await WithServiceAsync(service => service.CreateAsync(
            OwnedVehicle(context.CompanyId, "06TEK01"), CancellationToken.None));

        await WithServiceAsync(service => service.AssignAsync(
            vehicle.Id,
            new AssignVehicleRequest(context.ProjectId, null, null, new DateTime(2026, 1, 1)),
            CancellationToken.None));

        await WithServiceAsync(service => service.AssignAsync(
            vehicle.Id,
            new AssignVehicleRequest(context.OtherProjectId, null, null, new DateTime(2026, 2, 1)),
            CancellationToken.None));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var openCount = await db.VehicleAssignments
            .CountAsync(x => x.VehicleId == vehicle.Id && x.EndDate == null);

        Assert.Equal(1, openCount);
    }

    /// <summary>
    /// TEKRAR ANAHTARI: aynı anahtarla ikinci istek yeni atama açmaz.
    /// Çift tıklama ya da ağ tekrarı aracı iki kez atamış göstermemeli.
    /// </summary>
    [Fact]
    public async Task AyniReferansAnahtari_IkinciAtamaAcmaz()
    {
        var context = await CreateContextAsync();

        var vehicle = await WithServiceAsync(service => service.CreateAsync(
            OwnedVehicle(context.CompanyId, "06IDM01"), CancellationToken.None));

        var request = new AssignVehicleRequest(
            context.ProjectId, null, null, new DateTime(2026, 1, 1),
            ReferenceKey: "atama-1");

        var first = await WithServiceAsync(service => service.AssignAsync(
            vehicle.Id, request, CancellationToken.None));

        var second = await WithServiceAsync(service => service.AssignAsync(
            vehicle.Id, request, CancellationToken.None));

        Assert.Equal(first.Id, second.Id);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(
            1,
            await db.VehicleAssignments.CountAsync(x => x.VehicleId == vehicle.Id));
    }

    /// <summary>
    /// MERKEZ HAVUZU: proje boş bırakılırsa araç merkeze alınır. Ayrı
    /// bir bayrak yok — "proje dolu ama merkez işaretli" gibi çelişkili
    /// bir satır kurulamasın.
    /// </summary>
    [Fact]
    public async Task ProjesizAtama_MerkezHavuzuDemektir()
    {
        var context = await CreateContextAsync();

        var vehicle = await WithServiceAsync(service => service.CreateAsync(
            OwnedVehicle(context.CompanyId, "06MRK01"), CancellationToken.None));

        var assignment = await WithServiceAsync(service => service.AssignAsync(
            vehicle.Id,
            new AssignVehicleRequest(null, null, null, new DateTime(2026, 1, 1)),
            CancellationToken.None));

        Assert.Null(assignment.ProjectId);
    }

    /// <summary>
    /// MASRAF YANSITMASININ DAYANAĞI: verilen tarihte araç neredeydi.
    /// Sınırlar önemli — atamanın bittiği gün araç artık yeni yerdedir.
    /// </summary>
    [Fact]
    public async Task TariheGoreAtama_DogruDonulur()
    {
        var context = await CreateContextAsync();

        var vehicle = await WithServiceAsync(service => service.CreateAsync(
            OwnedVehicle(context.CompanyId, "06TAR01"), CancellationToken.None));

        await WithServiceAsync(service => service.AssignAsync(
            vehicle.Id,
            new AssignVehicleRequest(context.ProjectId, null, null, new DateTime(2026, 1, 1)),
            CancellationToken.None));

        await WithServiceAsync(service => service.AssignAsync(
            vehicle.Id,
            new AssignVehicleRequest(context.OtherProjectId, null, null, new DateTime(2026, 4, 1)),
            CancellationToken.None));

        var inFirst = await WithServiceAsync(service => service.GetAssignmentOnAsync(
            vehicle.Id, new DateTime(2026, 2, 15), CancellationToken.None));

        var onSwitchDay = await WithServiceAsync(service => service.GetAssignmentOnAsync(
            vehicle.Id, new DateTime(2026, 4, 1), CancellationToken.None));

        var beforeAny = await WithServiceAsync(service => service.GetAssignmentOnAsync(
            vehicle.Id, new DateTime(2025, 12, 1), CancellationToken.None));

        Assert.Equal(context.ProjectId, inFirst!.ProjectId);
        Assert.Equal(context.OtherProjectId, onSwitchDay!.ProjectId);
        Assert.Null(beforeAny);
    }

    /// <summary>
    /// Araç masrafı AYRI TABLOYA yazılmaz: gider kaydına araç bağı
    /// eklenir. Bu test bağın var olduğunu ve araç kartı dökümünün
    /// filtrelenmiş bir görünüm olabileceğini sabitler.
    /// </summary>
    [Fact]
    public async Task GiderKaydi_AracaBaglanabilir()
    {
        var context = await CreateContextAsync();

        var vehicle = await WithServiceAsync(service => service.CreateAsync(
            OwnedVehicle(context.CompanyId, "06GID01"), CancellationToken.None));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = await db.ExpenseCategories
            .FirstOrDefaultAsync(x => x.CompanyId == context.CompanyId);

        if (category is null)
        {
            category = new Models.Expenses.ExpenseCategory
            {
                CompanyId = context.CompanyId,
                Code = "arac-yakit",
                Name = "Araç / Yakıt",
                SortOrder = 50
            };

            db.ExpenseCategories.Add(category);
            await db.SaveChangesAsync();
        }

        db.ExpenseEntries.Add(new Models.Expenses.ExpenseEntry
        {
            CompanyId = context.CompanyId,
            CenterType = Models.Expenses.ExpenseCenterType.Project,
            ProjectId = context.ProjectId,
            ExpenseCategoryId = category.Id,
            ExpenseDate = DateTime.SpecifyKind(new DateTime(2026, 2, 10), DateTimeKind.Utc),
            Amount = 3_500m,
            Description = "Yakıt",
            PaymentMethod = Models.Expenses.ExpensePaymentMethod.Bank,
            DocumentType = Models.Expenses.ExpenseDocumentType.Invoice,
            VehicleId = vehicle.Id
        });

        await db.SaveChangesAsync();

        var total = await db.ExpenseEntries
            .Where(x => x.VehicleId == vehicle.Id)
            .SumAsync(x => x.Amount);

        Assert.Equal(3_500m, total);
    }
}
