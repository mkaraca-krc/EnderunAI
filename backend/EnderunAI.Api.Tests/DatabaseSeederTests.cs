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
