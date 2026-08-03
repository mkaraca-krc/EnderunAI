using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

[Collection("Integration")]
public sealed class DatabaseSeederTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task SeedAsync_ExistingRoleWithStaleDescription_IsCorrectedToMatchRoleCatalog()
    {
        // Regresyon testi: SeedRolesAsync önceden add-only'ydi (rol
        // zaten varsa hiç dokunmuyordu) — bu yüzden canlıda 5 rolün
        // açıklaması RoleCatalog.cs'deki güncel tanımdan sapmış, hiçbir
        // sonraki deploy bunu düzeltmemişti. Description hiçbir ekrandan
        // düzenlenemediği için artık her boot'ta senkronlanıyor.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var role = await db.Roles.SingleAsync(r => r.Name == "Sekreterya");
        var expectedDescription = role.Description;

        role.Description = "Eski / bozulmuş bir açıklama metni.";
        await db.SaveChangesAsync();

        await DatabaseSeeder.SeedAsync(db, passwordService, configuration);

        var reloaded = await db.Roles
            .AsNoTracking()
            .SingleAsync(r => r.Name == "Sekreterya");

        Assert.Equal(expectedDescription, reloaded.Description);
    }

    /// <summary>
    /// Regresyon testi: finans ayarları satırı bir kez oluştuktan sonra
    /// seed saf add-only olsaydı, sonradan eklenen yeni bir ayar alanı
    /// (Faz B'de gelen kesinti hesabı gibi) mevcut şirketlerde boş kalır
    /// ve akış çalışma anında hata verirdi. Seed artık yalnızca null
    /// alanları tamamlıyor.
    /// </summary>
    [Fact]
    public async Task SeedAsync_BackfillsNullFinanceAccountsButKeepsAdminChoice()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var seedService = scope.ServiceProvider
            .GetRequiredService<Api.Services.Accounting.IAccountingAccountSeedService>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var company = new Company { Code = $"FIN-{suffix}", Name = $"Finans Backfill {suffix}" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        await seedService.SeedAsync(company.Id, CancellationToken.None);

        // Admin bir hesabı bilinçli seçmiş, diğerleri hiç doldurulmamış.
        var adminChosenAccountId = await db.AccountingAccounts
            .Where(x => x.CompanyId == company.Id && x.Code == "770")
            .Select(x => x.Id)
            .SingleAsync();

        db.CompanyFinanceSettings.Add(new CompanyFinanceSettings
        {
            CompanyId = company.Id,
            ExpenseAccountId = adminChosenAccountId
        });
        await db.SaveChangesAsync();

        await DatabaseSeeder.SeedAsync(db, passwordService, configuration);

        var settings = await db.CompanyFinanceSettings
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == company.Id);

        // Admin seçimi korunmalı (740'a geri dönmemeli)
        Assert.Equal(adminChosenAccountId, settings.ExpenseAccountId);
        // Boş olanlar tamamlanmalı
        Assert.NotNull(settings.DeductionAccountId);
        Assert.NotNull(settings.SalesAccountId);
        Assert.NotNull(settings.VatOutAccountId);
    }

    [Fact]
    public async Task SeedAsync_ExistingRoleWithCustomDataScopePolicy_IsNotReverted()
    {
        // DataScopePolicy admin tarafından Yetki Matrisi'nden bilinçli
        // değiştirilebiliyor (PermissionMatrixController.UpdateScopePolicy)
        // — seeder bu seçimi asla geri almamalı.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var role = await db.Roles.SingleAsync(r => r.Name == "Depo Sorumlusu");
        role.DataScopePolicy = RoleDataScopePolicy.SiteOnly;
        await db.SaveChangesAsync();

        await DatabaseSeeder.SeedAsync(db, passwordService, configuration);

        var reloaded = await db.Roles
            .AsNoTracking()
            .SingleAsync(r => r.Name == "Depo Sorumlusu");

        Assert.Equal(RoleDataScopePolicy.SiteOnly, reloaded.DataScopePolicy);
    }
}
