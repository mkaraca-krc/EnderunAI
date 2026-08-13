using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Models.Fleet;
using EnderunAI.Api.Services.Expenses;
using EnderunAI.Api.Services.Fleet;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ARAÇ MASRAFININ YANSITILMASI — uçtan uca.
///
/// ÇİFT SAYIM YASAĞI bu paketin ana kuralı: araç masrafı ayrı bir
/// defterde tutulmuyor, gider kaydına araç bağı ekleniyor. Araç
/// kartındaki döküm o kayıtların filtrelenmiş görünümüdür; gider
/// merkezi raporunda aynı tutar BİR KEZ sayılır.
/// </summary>
[Collection("Integration")]
public sealed class VehicleExpenseReflectionTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId,
        Guid BranchId,
        Guid ProjectAId,
        Guid ProjectBId,
        Guid VehicleId,
        Guid CategoryId);

    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var second = new Project
        {
            CompanyId = project.CompanyId,
            BranchId = project.BranchId,
            EmployerCurrentAccountId = project.EmployerCurrentAccountId,
            Code = $"PRJ2-{suffix}",
            Name = $"İkinci proje {suffix}",
            CurrencyCode = "TRY",
            Status = ProjectStatus.Active
        };

        db.Projects.Add(second);

        // Merkez payının yazılabilmesi için merkez şube gerekiyor.
        var branch = await db.Branches.SingleAsync(x => x.Id == project.BranchId);
        branch.IsHeadOffice = true;

        var vehicle = new Vehicle
        {
            CompanyId = project.CompanyId,
            PlateNumber = $"06FLT{suffix[..3].ToUpperInvariant()}",
            Type = VehicleType.Pickup,
            Ownership = VehicleOwnership.Rented,
            RentAmount = 30_000m,
            RentPeriod = VehicleRentPeriod.Monthly
        };

        db.Vehicles.Add(vehicle);

        await ExpenseCategoryProvisioner.EnsureAsync(
            db, project.CompanyId, CancellationToken.None);

        await db.SaveChangesAsync();

        var category = await db.ExpenseCategories.FirstAsync(
            x => x.CompanyId == project.CompanyId &&
                 x.Code == ExpenseCategoryCatalog.Vehicle);

        return new Context(
            project.CompanyId, project.BranchId, project.Id, second.Id,
            vehicle.Id, category.Id);
    }

    private async Task<T> WithScopeAsync<T>(
        Func<IVehicleService, IVehicleExpenseService, Task<T>> action)
    {
        using var scope = fixture.Factory.Services.CreateScope();

        return await action(
            scope.ServiceProvider.GetRequiredService<IVehicleService>(),
            scope.ServiceProvider.GetRequiredService<IVehicleExpenseService>());
    }

    /// <summary>
    /// Belirli bir ROLDE istemci. Elden maskesi testinde gerekiyor:
    /// aracı görebilen ama ek ödeme yetkisi OLMAYAN bir kullanıcı
    /// lazım (Şantiye Şefi tam olarak bu).
    /// </summary>
    private async Task<HttpClient> CreateClientForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider
            .GetRequiredService<EnderunAI.Api.Security.PasswordService>();

        const string password = "Filo!2026Test";
        var username = $"test-filo-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = $"Test {roleName}",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var role = await db.Roles.SingleAsync(x => x.Name == roleName);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        db.UserDataScopes.Add(new UserDataScope
        {
            UserId = user.Id,
            ScopeType = DataScopeType.All
        });

        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static DateTime D(int day) => new(2026, 4, day);

    // ------------------------------------------------------------------
    // TARİHLİ TEKİL MASRAF
    // ------------------------------------------------------------------

    /// <summary>
    /// Yakıt/ceza/HGS gibi tarihli masraf, MASRAF TARİHİNDE aracın
    /// bulunduğu projeye önerilir — bugünkü atamaya değil. Aksi hâlde
    /// dün teslim edilen aracın dünkü yakıtı yeni projeye yazılırdı.
    /// </summary>
    [Fact]
    public async Task TekilMasraf_MasrafTarihindekiProjeyeOnerilir()
    {
        var context = await CreateContextAsync();

        await WithScopeAsync((service, _) => service.AssignAsync(
            context.VehicleId,
            new AssignVehicleRequest(context.ProjectAId, null, null, D(1)),
            CancellationToken.None));

        await WithScopeAsync((service, _) => service.AssignAsync(
            context.VehicleId,
            new AssignVehicleRequest(context.ProjectBId, null, null, D(20)),
            CancellationToken.None));

        var early = await WithScopeAsync((_, expenses) => expenses.SuggestCenterAsync(
            context.VehicleId, D(10), CancellationToken.None));

        var late = await WithScopeAsync((_, expenses) => expenses.SuggestCenterAsync(
            context.VehicleId, D(25), CancellationToken.None));

        Assert.Equal(ExpenseCenterType.Project, early!.CenterType);
        Assert.Equal(context.ProjectAId, early.CenterId);

        Assert.Equal(context.ProjectBId, late!.CenterId);
    }

    /// <summary>
    /// Araç o gün MERKEZ HAVUZUNDAYSA masraf merkeze yazılır.
    /// </summary>
    [Fact]
    public async Task TekilMasraf_MerkezHavuzundaysaMerkezeOnerilir()
    {
        var context = await CreateContextAsync();

        await WithScopeAsync((service, _) => service.AssignAsync(
            context.VehicleId,
            new AssignVehicleRequest(null, null, null, D(1)),
            CancellationToken.None));

        var suggestion = await WithScopeAsync((_, expenses) => expenses.SuggestCenterAsync(
            context.VehicleId, D(10), CancellationToken.None));

        Assert.Equal(ExpenseCenterType.Branch, suggestion!.CenterType);
        Assert.Equal(context.BranchId, suggestion.CenterId);
        Assert.Contains("merkez havuzunda", suggestion.Reason);
    }

    // ------------------------------------------------------------------
    // DÖNEMSEL MASRAF
    // ------------------------------------------------------------------

    private static VehiclePeriodicCostRequest RentRequest(
        Context context,
        decimal amount = 30_000m,
        IReadOnlyList<VehicleManualAllocation>? manual = null) =>
        new(D(1), D(30), amount, context.CategoryId, "Araç kirası",
            ExpensePaymentMethod.Bank, ExpenseDocumentType.Invoice,
            ManualAllocations: manual);

    /// <summary>
    /// Dönem içinde araç iki projedeyse kira gün oranıyla bölünür ve
    /// HER PAY İÇİN AYRI gider kaydı açılır — payları ayrı bir tabloda
    /// tutmak, gider merkezi raporunun okumadığı ikinci defter olurdu.
    /// </summary>
    [Fact]
    public async Task DonemselMasraf_GunOraniylaBolusur_ToplamKapanir()
    {
        var context = await CreateContextAsync();

        await WithScopeAsync((service, _) => service.AssignAsync(
            context.VehicleId,
            new AssignVehicleRequest(context.ProjectAId, null, null, D(1)),
            CancellationToken.None));

        await WithScopeAsync((service, _) => service.AssignAsync(
            context.VehicleId,
            new AssignVehicleRequest(context.ProjectBId, null, null, D(15)),
            CancellationToken.None));

        var result = await WithScopeAsync((_, expenses) => expenses.CreatePeriodicAsync(
            context.VehicleId, RentRequest(context), CancellationToken.None));

        Assert.Equal(2, result.CreatedEntryCount);
        Assert.Equal(30_000m, result.TotalAmount);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entries = await db.ExpenseEntries
            .Where(x => x.VehicleId == context.VehicleId)
            .ToListAsync();

        // Payların toplamı TUTARIN KENDİSİ: %100 kapanıyor.
        Assert.Equal(30_000m, entries.Sum(x => x.Amount));
        Assert.Equal(14_000m, entries.Single(x => x.ProjectId == context.ProjectAId).Amount);
        Assert.Equal(16_000m, entries.Single(x => x.ProjectId == context.ProjectBId).Amount);
    }

    /// <summary>
    /// Dönem tek projede geçtiyse bölüştürme TEK satır üretir; boş
    /// merkez payı açılmaz.
    /// </summary>
    [Fact]
    public async Task DonemselMasraf_TekProjedeyse_TekKayit()
    {
        var context = await CreateContextAsync();

        await WithScopeAsync((service, _) => service.AssignAsync(
            context.VehicleId,
            new AssignVehicleRequest(context.ProjectAId, null, null, new DateTime(2026, 3, 1)),
            CancellationToken.None));

        var result = await WithScopeAsync((_, expenses) => expenses.CreatePeriodicAsync(
            context.VehicleId, RentRequest(context), CancellationToken.None));

        Assert.Equal(1, result.CreatedEntryCount);
        Assert.Equal(30_000m, result.TotalAmount);
    }

    /// <summary>
    /// ELLE DÜZELTME: kullanıcı payları değiştirebilir ama toplam yine
    /// %100 kapanmalı. Otomatik düzeltilseydi kullanıcı girdiğini
    /// değil, sistemin uydurduğunu görürdü.
    /// </summary>
    [Fact]
    public async Task DonemselMasraf_ElleDuzeltilebilir_ToplamYineKapanir()
    {
        var context = await CreateContextAsync();

        await WithScopeAsync((service, _) => service.AssignAsync(
            context.VehicleId,
            new AssignVehicleRequest(context.ProjectAId, null, null, D(1)),
            CancellationToken.None));

        await WithScopeAsync((service, _) => service.AssignAsync(
            context.VehicleId,
            new AssignVehicleRequest(context.ProjectBId, null, null, D(15)),
            CancellationToken.None));

        var result = await WithScopeAsync((_, expenses) => expenses.CreatePeriodicAsync(
            context.VehicleId,
            RentRequest(context, manual:
            [
                new VehicleManualAllocation(context.ProjectAId, 20_000m),
                new VehicleManualAllocation(context.ProjectBId, 10_000m)
            ]),
            CancellationToken.None));

        Assert.Equal(30_000m, result.TotalAmount);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entries = await db.ExpenseEntries
            .Where(x => x.VehicleId == context.VehicleId)
            .ToListAsync();

        Assert.Equal(20_000m, entries.Single(x => x.ProjectId == context.ProjectAId).Amount);
        Assert.Equal(30_000m, entries.Sum(x => x.Amount));
    }

    [Fact]
    public async Task ElleDuzeltme_ToplamiKapatmiyorsa_Reddedilir()
    {
        var context = await CreateContextAsync();

        await WithScopeAsync((service, _) => service.AssignAsync(
            context.VehicleId,
            new AssignVehicleRequest(context.ProjectAId, null, null, D(1)),
            CancellationToken.None));

        var error = await Assert.ThrowsAsync<FleetValidationException>(() =>
            WithScopeAsync((_, expenses) => expenses.CreatePeriodicAsync(
                context.VehicleId,
                RentRequest(context, manual:
                [
                    new VehicleManualAllocation(context.ProjectAId, 25_000m)
                ]),
                CancellationToken.None)));

        Assert.Contains("birebir karşılamalıdır", error.Message);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Reddedilen kayıt HİÇ yazılmamalı: yarım dağıtım kalmasın.
        Assert.False(await db.ExpenseEntries.AnyAsync(x => x.VehicleId == context.VehicleId));
    }

    // ------------------------------------------------------------------
    // ÇİFT SAYIM VE MASKE
    // ------------------------------------------------------------------

    /// <summary>
    /// ÇİFT SAYIM YOK: araç kartındaki döküm ile gider merkezi raporu
    /// AYNI kayıtları okuyor. Araç masrafı için ayrı defter açılsaydı
    /// aynı tutar raporda iki kez görünürdü.
    /// </summary>
    [Fact]
    public async Task AracMasrafi_GiderRaporunda_BirKezSayilir()
    {
        var context = await CreateContextAsync();

        await WithScopeAsync((service, _) => service.AssignAsync(
            context.VehicleId,
            new AssignVehicleRequest(context.ProjectAId, null, null, D(1)),
            CancellationToken.None));

        await WithScopeAsync((_, expenses) => expenses.CreatePeriodicAsync(
            context.VehicleId, RentRequest(context), CancellationToken.None));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var report = scope.ServiceProvider.GetRequiredService<ExpenseCenterReportService>();

        var vehicleTotal = await db.ExpenseEntries
            .Where(x => x.VehicleId == context.VehicleId)
            .SumAsync(x => x.Amount);

        var built = await report.BuildAsync(
            context.CompanyId, D(1), D(30), CancellationToken.None);

        var projectRows = built.Rows
            .Where(x => x.CenterId == context.ProjectAId &&
                        x.CategoryCode == ExpenseCategoryCatalog.Vehicle)
            .ToList();

        Assert.Equal(30_000m, vehicleTotal);

        // Rapordaki araç/yakıt tutarı, araç kartındaki tutarın AYNISI —
        // katı değil.
        Assert.Equal(vehicleTotal, projectRows.Sum(x => x.Amount));
    }

    /// <summary>
    /// ELDEN MASKESİ: elden ödenen araç masrafını yetkisiz kullanıcı
    /// GÖRMEZ ve toplam yalnız görünen kalemlerden oluşur. Maske gider
    /// modülünün kendi kapısından (extra_payment.view + IsVisibleExpense)
    /// geçiyor; araç kartı kendi kuralını yazmıyor.
    /// </summary>
    [Fact]
    public async Task EldenAracMasrafi_YetkisizKullaniciyaGorunmez()
    {
        var context = await CreateContextAsync();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.ExpenseEntries.AddRange(
                new ExpenseEntry
                {
                    CompanyId = context.CompanyId,
                    CenterType = ExpenseCenterType.Project,
                    ProjectId = context.ProjectAId,
                    ExpenseCategoryId = context.CategoryId,
                    ExpenseDate = DateTime.SpecifyKind(D(5), DateTimeKind.Utc),
                    Amount = 4_000m,
                    Description = "Yakıt (faturalı)",
                    PaymentMethod = ExpensePaymentMethod.Bank,
                    DocumentType = ExpenseDocumentType.Invoice,
                    VehicleId = context.VehicleId
                },
                new ExpenseEntry
                {
                    CompanyId = context.CompanyId,
                    CenterType = ExpenseCenterType.Project,
                    ProjectId = context.ProjectAId,
                    ExpenseCategoryId = context.CategoryId,
                    ExpenseDate = DateTime.SpecifyKind(D(6), DateTimeKind.Utc),
                    Amount = 1_500m,
                    Description = "Yakıt (elden)",
                    PaymentMethod = ExpensePaymentMethod.Cash,
                    DocumentType = ExpenseDocumentType.None,
                    VehicleId = context.VehicleId
                });

            await db.SaveChangesAsync();
        }

        // Aracı görebilen ama ek ödeme yetkisi OLMAYAN rol.
        var restricted = await CreateClientForRoleAsync("Şantiye Şefi");

        var response = await restricted.GetAsync(
            $"/api/vehicles/{context.VehicleId}/expenses");

        // Rol aracı görebilmeli; göremiyorsa maske testi anlamını
        // yitirir ve sessizce yeşile döner.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(4_000m, body.GetProperty("total").GetDecimal());
        Assert.Equal(1, body.GetProperty("hiddenCount").GetInt32());

        Assert.DoesNotContain(
            body.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("description").GetString()!.Contains("elden"));
    }

    /// <summary>Yetkili kullanıcı elden kalemi ve tam toplamı görür.</summary>
    [Fact]
    public async Task EldenAracMasrafi_YetkiliKullaniciyaGorunur()
    {
        var context = await CreateContextAsync();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.ExpenseEntries.Add(new ExpenseEntry
            {
                CompanyId = context.CompanyId,
                CenterType = ExpenseCenterType.Project,
                ProjectId = context.ProjectAId,
                ExpenseCategoryId = context.CategoryId,
                ExpenseDate = DateTime.SpecifyKind(D(6), DateTimeKind.Utc),
                Amount = 1_500m,
                Description = "Yakıt (elden)",
                PaymentMethod = ExpensePaymentMethod.Cash,
                DocumentType = ExpenseDocumentType.None,
                VehicleId = context.VehicleId
            });

            await db.SaveChangesAsync();
        }

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var body = await client.GetFromJsonAsync<JsonElement>(
            $"/api/vehicles/{context.VehicleId}/expenses");

        Assert.Equal(1_500m, body.GetProperty("total").GetDecimal());
        Assert.Equal(0, body.GetProperty("hiddenCount").GetInt32());
    }
}
