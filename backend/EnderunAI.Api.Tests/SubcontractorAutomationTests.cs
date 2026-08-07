using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Subcontractors;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Yansıtma otomasyonu ve otomatik tedarikçi faturası (T9 + T10).
///
/// YANSITMA ZİNCİRİ: işveren bizden keser → biz taşerondan keseriz.
/// Daha önce yemek/konaklama/malzeme tutarları elle giriliyordu; motor
/// hazırdı ama girdiyi kimse beslemiyordu. Buradaki güvence, tutarın
/// GERÇEK VERİDEN gelmesi: işveren hakedişindeki birim fiyat ve taşeron
/// ekibinin puantajı.
///
/// FATURA: hakediş onaylandığında borç kesinleşir; faturanın elle
/// girilmesine bırakılması hakediş ile muhasebeyi ayrıştırırdı. Elden
/// kısım bu faturaya HİÇ girmez.
/// </summary>
[Collection("Integration")]
public sealed class SubcontractorAutomationTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid ProjectId, Guid SiteId,
        Guid AccountId, Guid ContractId, Guid PersonnelId);

    /// <summary>
    /// Taşeron sözleşmesi + ekibinde bir işçi olan bir bağlam kurar.
    /// Kapsam tikleri çağırana göre ayarlanır.
    /// </summary>
    private async Task<Context> CreateContextAsync(
        SubcontractorResponsibility meal = SubcontractorResponsibility.Us,
        SubcontractorResponsibility accommodation = SubcontractorResponsibility.Us,
        SubcontractorResponsibility material = SubcontractorResponsibility.Us,
        bool attachSite = true)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var site = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"SNT-{suffix}",
            Name = "Test Şantiyesi"
        };
        db.ProjectSites.Add(site);

        var account = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"TAS-{suffix}",
            Title = $"Test Taşeron {suffix}",
            Roles = CurrentAccountRoles.Subcontractor,
            Status = CurrentAccountStatus.Approved
        };
        db.CurrentAccounts.Add(account);
        await db.SaveChangesAsync();

        var contract = new SubcontractorContract
        {
            CompanyId = project.CompanyId,
            CurrentAccountId = account.Id,
            ProjectId = project.Id,
            ProjectSiteId = attachSite ? site.Id : null,
            ContractNumber = $"TS-{suffix}",
            WorkDescription = "Kaba elektrik",
            ContractType = ProjectContractType.LumpSum,
            ContractAmount = 500_000m,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = SubcontractorContractStatus.Active,
            MealResponsibility = meal,
            AccommodationResponsibility = accommodation,
            MaterialResponsibility = material
        };
        db.SubcontractorContracts.Add(contract);
        await db.SaveChangesAsync();

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        // İşçiyi taşeron ekibine bağla.
        var tracked = await db.Personnel.SingleAsync(x => x.Id == personnel.Id);
        tracked.SubcontractorContractId = contract.Id;
        await db.SaveChangesAsync();

        return new Context(
            project.CompanyId, project.Id, site.Id,
            account.Id, contract.Id, personnel.Id);
    }

    /// <summary>
    /// İşveren hakedişimize alt kalemli bir kesinti yazar (yemek ya da
    /// konaklama), verilen birim fiyatlarla.
    /// </summary>
    private async Task AddEmployerDeductionAsync(
        Context context,
        HakedisDeductionType type,
        params (string Name, decimal UnitPrice)[] lines)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var deduction = new ProgressPaymentDeduction
        {
            LineNumber = 1,
            DeductionType = (int)type,
            Description = type.ToString(),
            Amount = 0m
        };

        var lineNumber = 1;

        foreach (var (name, unitPrice) in lines)
        {
            deduction.Lines.Add(new ProgressPaymentDeductionLine
            {
                LineNumber = lineNumber++,
                Name = name,
                UnitPrice = unitPrice,
                Quantity = 0m,
                NetAmount = 0m,
                GrossAmount = 0m
            });
        }

        db.ProgressPayments.Add(new ProgressPayment
        {
            CompanyId = context.CompanyId,
            ProjectId = context.ProjectId,
            ProgressPaymentNumber = $"IHK-{Guid.NewGuid():N}"[..14],
            ProgressPaymentDate = new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc),
            Status = ProgressPaymentStatus.Approved,
            Deductions = [deduction]
        });

        await db.SaveChangesAsync();
    }

    /// <summary>Taşeron işçisine sahada geçen puantaj günleri yazar.</summary>
    private async Task AddAttendanceAsync(
        Context context, int dayCount, int status = (int)AttendanceStatus.Worked,
        bool approved = true, int fromDay = 1)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        for (var i = 0; i < dayCount; i++)
        {
            db.AttendanceRecords.Add(new AttendanceRecord
            {
                CompanyId = context.CompanyId,
                ProjectId = context.ProjectId,
                ProjectSiteId = context.SiteId,
                PersonnelId = context.PersonnelId,
                WorkDate = new DateTime(2026, 3, fromDay + i, 0, 0, 0, DateTimeKind.Utc),
                Status = status,
                NormalHours = 7.5m,
                TotalHours = 7.5m,
                IsApproved = approved
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<PlannedDeduction>> PlanAsync(Context context)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var planner = scope.ServiceProvider
            .GetRequiredService<SubcontractorDeductionPlanner>();

        var contract = await db.SubcontractorContracts
            .AsNoTracking()
            .SingleAsync(x => x.Id == context.ContractId);

        return await planner.PlanAsync(
            contract, 2026, 3, cumulativeWorkAmount: 100_000m, default);
    }

    // ---------- T9: yemek / konaklama yansıtması ----------

    /// <summary>
    /// BU PAKETİN ASIL GÜVENCESİ: yemek kesintisi artık elle giriş
    /// beklemiyor — işveren birim fiyatı × taşeron puantaj günü.
    /// </summary>
    [Fact]
    public async Task MealReflection_IsCalculatedAutomatically()
    {
        var context = await CreateContextAsync();

        // İşveren bize kahvaltı 50, öğlen 120 uyguluyor
        await AddEmployerDeductionAsync(
            context, HakedisDeductionType.Meal, ("Kahvaltı", 50m), ("Öğlen", 120m));

        // Taşeron işçisi 10 gün sahada
        await AddAttendanceAsync(context, 10);

        var planned = await PlanAsync(context);

        var meal = planned.Single(
            x => x.DeductionType == (int)HakedisDeductionType.Meal);

        // (50 + 120) × 10 = 1.700
        Assert.Equal(1_700m, meal.Amount);
        Assert.Contains("Kahvaltı", meal.Basis);
        Assert.Contains("Öğlen", meal.Basis);
    }

    [Fact]
    public async Task AccommodationReflection_IsCalculatedAutomatically()
    {
        var context = await CreateContextAsync();

        await AddEmployerDeductionAsync(
            context, HakedisDeductionType.Accommodation, ("Yatılı", 200m));

        await AddAttendanceAsync(context, 8);

        var planned = await PlanAsync(context);

        var accommodation = planned.Single(
            x => x.DeductionType == (int)HakedisDeductionType.Accommodation);

        Assert.Equal(1_600m, accommodation.Amount);
    }

    /// <summary>
    /// Tik "taşerona ait" ise kesinti kalemi HİÇ açılmamalı — masrafı
    /// biz yapmıyoruz, kesecek bir şey yok.
    /// </summary>
    [Fact]
    public async Task WhenResponsibilityIsSubcontractor_NoDeductionIsPlanned()
    {
        var context = await CreateContextAsync(
            meal: SubcontractorResponsibility.Subcontractor,
            accommodation: SubcontractorResponsibility.Subcontractor,
            material: SubcontractorResponsibility.Subcontractor);

        await AddEmployerDeductionAsync(
            context, HakedisDeductionType.Meal, ("Öğlen", 120m));
        await AddAttendanceAsync(context, 10);

        var planned = await PlanAsync(context);

        Assert.DoesNotContain(
            planned, x => x.DeductionType == (int)HakedisDeductionType.Meal);
        Assert.DoesNotContain(
            planned, x => x.DeductionType == (int)HakedisDeductionType.Accommodation);
        Assert.DoesNotContain(
            planned,
            x => x.DeductionType == (int)HakedisDeductionType.MaterialDeduction);
    }

    /// <summary>
    /// Sahada geçmeyen günler (izin/rapor/hafta tatili) yemek adedine
    /// GİRMEMELİ: o günlerde şantiyede yemek yenmiyor.
    /// </summary>
    [Fact]
    public async Task OffSiteDays_AreNotCounted()
    {
        var context = await CreateContextAsync();

        await AddEmployerDeductionAsync(
            context, HakedisDeductionType.Meal, ("Öğlen", 100m));

        await AddAttendanceAsync(context, 3);
        await AddAttendanceAsync(
            context, 5, status: (int)AttendanceStatus.PaidLeave, fromDay: 10);

        var planned = await PlanAsync(context);

        var meal = planned.Single(
            x => x.DeductionType == (int)HakedisDeductionType.Meal);

        // Yalnızca 3 çalışılan gün
        Assert.Equal(300m, meal.Amount);
    }

    /// <summary>
    /// Onaylanmamış puantaj sayılmamalı; henüz kesinleşmemiş gün
    /// üzerinden taşerondan para kesilemez.
    /// </summary>
    [Fact]
    public async Task UnapprovedAttendance_IsExcluded()
    {
        var context = await CreateContextAsync();

        await AddEmployerDeductionAsync(
            context, HakedisDeductionType.Meal, ("Öğlen", 100m));

        await AddAttendanceAsync(context, 4);
        await AddAttendanceAsync(context, 6, approved: false, fromDay: 10);

        var planned = await PlanAsync(context);

        var meal = planned.Single(
            x => x.DeductionType == (int)HakedisDeductionType.Meal);

        Assert.Equal(400m, meal.Amount);
    }

    /// <summary>
    /// İşveren birim fiyatı yoksa tutar UYDURULMAMALI; kalem tutarsız
    /// açılıp nedeni yazılmalı.
    /// </summary>
    [Fact]
    public async Task WithoutEmployerUnitPrice_NoAmountIsInvented()
    {
        var context = await CreateContextAsync();

        await AddAttendanceAsync(context, 10);

        var planned = await PlanAsync(context);

        var meal = planned.Single(
            x => x.DeductionType == (int)HakedisDeductionType.Meal);

        Assert.Null(meal.Amount);
        Assert.Contains("birim fiyat", meal.Basis!);
    }

    /// <summary>
    /// Taşeron ekibinin puantajı yoksa adet hesaplanamaz; yine tutar
    /// üretilmemeli ve nedeni yazılmalı.
    /// </summary>
    [Fact]
    public async Task WithoutAttendance_NoAmountIsInvented()
    {
        var context = await CreateContextAsync();

        await AddEmployerDeductionAsync(
            context, HakedisDeductionType.Meal, ("Öğlen", 100m));

        var planned = await PlanAsync(context);

        var meal = planned.Single(
            x => x.DeductionType == (int)HakedisDeductionType.Meal);

        Assert.Null(meal.Amount);
        Assert.Contains("puantaj", meal.Basis!);
    }

    // ---------- T9b: malzeme ----------

    /// <summary>
    /// Taşerona ETİKETLENMİŞ depo çıkışının bedeli malzeme kesintisi
    /// olarak önerilmeli.
    /// </summary>
    [Fact]
    public async Task MaterialDeduction_ComesFromTaggedStockIssues()
    {
        var context = await CreateContextAsync();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var branchId = await db.Branches
                .Where(x => x.CompanyId == context.CompanyId)
                .Select(x => x.Id)
                .FirstAsync();

            var warehouse = new Warehouse
            {
                CompanyId = context.CompanyId,
                BranchId = branchId,
                Code = $"DEP-{Guid.NewGuid():N}"[..10],
                Name = "Test Depo"
            };
            var item = new InventoryItem
            {
                CompanyId = context.CompanyId,
                Code = $"STK-{Guid.NewGuid():N}"[..10],
                Name = "NYY kablo",
                Unit = "MTR"
            };
            db.Warehouses.Add(warehouse);
            db.InventoryItems.Add(item);
            await db.SaveChangesAsync();

            db.StockMovements.Add(new StockMovement
            {
                CompanyId = context.CompanyId,
                WarehouseId = warehouse.Id,
                InventoryItemId = item.Id,
                ProjectId = context.ProjectId,
                SubcontractorContractId = context.ContractId,
                Type = StockMovementType.Issue,
                Quantity = 100m,
                UnitCost = 25m,
                TotalCost = 2_500m,
                MovementDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                ReferenceNumber = "CIK-1"
            });

            await db.SaveChangesAsync();
        }

        var planned = await PlanAsync(context);

        var material = planned.Single(
            x => x.DeductionType == (int)HakedisDeductionType.MaterialDeduction);

        Assert.Equal(2_500m, material.Amount);
    }

    /// <summary>
    /// KRİTİK: etiketsiz sarf taşerona YAZILMAMALI. Projedeki tüm sarfı
    /// taşerona yüklemek, olmayan bir borç yaratmak olurdu.
    /// </summary>
    [Fact]
    public async Task UntaggedStockIssue_IsNotChargedToSubcontractor()
    {
        var context = await CreateContextAsync();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var branchId = await db.Branches
                .Where(x => x.CompanyId == context.CompanyId)
                .Select(x => x.Id)
                .FirstAsync();

            var warehouse = new Warehouse
            {
                CompanyId = context.CompanyId,
                BranchId = branchId,
                Code = $"DEP-{Guid.NewGuid():N}"[..10],
                Name = "Test Depo"
            };
            var item = new InventoryItem
            {
                CompanyId = context.CompanyId,
                Code = $"STK-{Guid.NewGuid():N}"[..10],
                Name = "NYY kablo",
                Unit = "MTR"
            };
            db.Warehouses.Add(warehouse);
            db.InventoryItems.Add(item);
            await db.SaveChangesAsync();

            db.StockMovements.Add(new StockMovement
            {
                CompanyId = context.CompanyId,
                WarehouseId = warehouse.Id,
                InventoryItemId = item.Id,
                ProjectId = context.ProjectId,
                // Taşeron etiketi YOK
                SubcontractorContractId = null,
                Type = StockMovementType.Issue,
                Quantity = 999m,
                UnitCost = 100m,
                TotalCost = 99_900m,
                MovementDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                ReferenceNumber = "CIK-2"
            });

            await db.SaveChangesAsync();
        }

        var planned = await PlanAsync(context);

        var material = planned.Single(
            x => x.DeductionType == (int)HakedisDeductionType.MaterialDeduction);

        Assert.Null(material.Amount);
        Assert.Contains("etiketlenmiş", material.Basis!);
    }
    // ---------- T9b uc: depo cikisinda taseron secimi ----------

    /// <summary>
    /// UÇTAN UCA: depo çıkışında taşeron seçilirse hareket etiketlenir
    /// ve tutar malzeme kesintisi önerisine girer. Alanın arayüzde
    /// olması yetmez; ucun kabul edip kaydetmesi gerekir.
    /// </summary>
    [Fact]
    public async Task StockIssue_WithSubcontractor_FeedsMaterialDeduction()
    {
        var context = await CreateContextAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var (warehouseId, itemId) = await SeedStockAsync(context, quantity: 500m);

        var response = await client.PostAsJsonAsync(
            "/api/inventory/issues",
            new
            {
                warehouseId,
                inventoryItemId = itemId,
                projectId = context.ProjectId,
                projectSiteId = context.SiteId,
                subcontractorContractId = context.ContractId,
                quantity = 100m,
                movementDate = new DateTime(2026, 3, 15),
                description = "Taşerona kablo"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var movement = await db.StockMovements
                .AsNoTracking()
                .SingleAsync(x => x.SubcontractorContractId == context.ContractId);

            Assert.Equal(StockMovementType.Issue, movement.Type);
            Assert.Equal(100m, movement.Quantity);
        }

        var planned = await PlanAsync(context);

        var material = planned.Single(
            x => x.DeductionType == (int)HakedisDeductionType.MaterialDeduction);

        // 100 × 25 = 2.500 (SeedStockAsync birim maliyeti 25)
        Assert.Equal(2_500m, material.Amount);
    }

    /// <summary>
    /// Taşeron seçilmezse hareket etiketsiz kalır ve kesinti önerisi
    /// üretilmez — "bizim sarfımız" varsayılanı korunmalı.
    /// </summary>
    [Fact]
    public async Task StockIssue_WithoutSubcontractor_StaysUntagged()
    {
        var context = await CreateContextAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var (warehouseId, itemId) = await SeedStockAsync(context, quantity: 500m);

        var response = await client.PostAsJsonAsync(
            "/api/inventory/issues",
            new
            {
                warehouseId,
                inventoryItemId = itemId,
                projectId = context.ProjectId,
                quantity = 100m,
                movementDate = new DateTime(2026, 3, 15)
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var planned = await PlanAsync(context);

        var material = planned.Single(
            x => x.DeductionType == (int)HakedisDeductionType.MaterialDeduction);

        Assert.Null(material.Amount);
    }

    /// <summary>
    /// Başka projenin taşeronu seçilemez: o taşeronun hakedişinden
    /// haksız kesinti önerirdi.
    /// </summary>
    [Fact]
    public async Task StockIssue_WithForeignContract_IsRejected()
    {
        var context = await CreateContextAsync();
        var other = await CreateContextAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var (warehouseId, itemId) = await SeedStockAsync(context, quantity: 500m);

        var response = await client.PostAsJsonAsync(
            "/api/inventory/issues",
            new
            {
                warehouseId,
                inventoryItemId = itemId,
                projectId = context.ProjectId,
                // Başka projenin sözleşmesi
                subcontractorContractId = other.ContractId,
                quantity = 10m,
                movementDate = new DateTime(2026, 3, 15)
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Depo, malzeme ve stok bakiyesi kurar.</summary>
    private async Task<(Guid WarehouseId, Guid ItemId)> SeedStockAsync(
        Context context, decimal quantity)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var branchId = await db.Branches
            .Where(x => x.CompanyId == context.CompanyId)
            .Select(x => x.Id)
            .FirstAsync();

        var warehouse = new Warehouse
        {
            CompanyId = context.CompanyId,
            BranchId = branchId,
            Code = $"DEP-{Guid.NewGuid():N}"[..10],
            Name = "Test Depo"
        };
        var item = new InventoryItem
        {
            CompanyId = context.CompanyId,
            Code = $"STK-{Guid.NewGuid():N}"[..10],
            Name = "NYY kablo",
            Unit = "MTR",
            AverageUnitCost = 25m
        };

        db.Warehouses.Add(warehouse);
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        db.WarehouseStocks.Add(new WarehouseStock
        {
            WarehouseId = warehouse.Id,
            InventoryItemId = item.Id,
            Quantity = quantity
        });

        await db.SaveChangesAsync();

        return (warehouse.Id, item.Id);
    }

    private async Task<HttpClient> CreateClientForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "SubAuto!2026";
        var username = $"test-subauto-{Guid.NewGuid():N}"[..40];
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
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
