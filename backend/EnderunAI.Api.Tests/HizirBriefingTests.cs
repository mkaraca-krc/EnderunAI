using EnderunAI.Api.Data;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hizir;
using EnderunAI.Api.Services.Hizir.Briefing;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Katman 3 testleri. Brifingin asıl riski yetki sızıntısı: kullanıcı
/// kendi ekranında göremeyeceği bir sayıyı brifingde görmemeli.
/// İkinci risk uydurma: veri yokken madde üretilmemeli.
/// </summary>
[Collection("Integration")]
public sealed class HizirBriefingTests(DatabaseFixture fixture)
{
    private static readonly CurrentDataScopeSnapshot GlobalScope = new(
        HasGlobalAccess: true,
        CompanyIds: new HashSet<Guid>(),
        BranchIds: new HashSet<Guid>(),
        ProjectIds: new HashSet<Guid>(),
        VisibleCompanyIds: new HashSet<Guid>(),
        VisibleBranchIds: new HashSet<Guid>(),
        SiteIds: new HashSet<Guid>());

    /// <summary>Finans ve ücret verisi görmemesi gereken roller.</summary>
    public static TheoryData<string> LowPrivilegeRoles =>
        new() { "Şantiye Şefi", "Formen", "Depo Sorumlusu" };

    /// <summary>Finansal bilgi taşıyan kaynaklar ve gerektirdikleri izin.</summary>
    public static TheoryData<string, string> FinancialSources =>
        new()
        {
            // "cek_vadeleri" KALDIRILDI: çek vadesi artık bildirim
            // motorunda hesaplanıyor ve brifinge köprüden geliyor.
            // Yetki orada bildirim bazında süzülüyor.
            { "teklif_gecerliligi", PermissionCatalog.Keys.EngineeringView }
        };

    private async Task<HizirToolContext> ContextForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var permissions = await db.Roles
            .Where(x => x.Name == roleName)
            .SelectMany(x => db.RolePermissions
                .Where(rp => rp.RoleId == x.Id)
                .Select(rp => rp.Permission.Key))
            .ToListAsync();

        Assert.NotEmpty(permissions);

        return new HizirToolContext(
            Guid.NewGuid(), $"Test {roleName}", null,
            new[] { roleName }, permissions, GlobalScope);
    }

    private static async Task<HizirToolContext> ContextWithRealUserAsync(
        IServiceScope scope, string roleName)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var hash = passwordService.Hash("HizirBrifing!2026");

        var user = new Models.AppUser
        {
            Username = $"test-brifing-{suffix}",
            FullName = $"Brifing Testi {roleName}",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var permissions = await db.Roles
            .Where(x => x.Name == roleName)
            .SelectMany(x => db.RolePermissions
                .Where(rp => rp.RoleId == x.Id)
                .Select(rp => rp.Permission.Key))
            .ToListAsync();

        return new HizirToolContext(
            user.Id, user.FullName, null,
            new[] { roleName }, permissions, GlobalScope);
    }

    private static IReadOnlyList<IHizirBriefingSource> Sources(IServiceScope scope) =>
        scope.ServiceProvider.GetServices<IHizirBriefingSource>().ToList();

    /// <summary>
    /// Düşük yetkili rol, finansal kaynakların iznine sahip olmamalı;
    /// dolayısıyla o kaynaklar onun brifinginde hiç çalıştırılmaz.
    /// </summary>
    [Theory]
    [MemberData(nameof(LowPrivilegeRoles))]
    public async Task LowPrivilegeRoles_DoNotSeeFinancialSources(string roleName)
    {
        var context = await ContextForRoleAsync(roleName);

        using var scope = fixture.Factory.Services.CreateScope();

        foreach (var source in Sources(scope))
        {
            if (source.Key is not ("cek_vadeleri" or "teklif_gecerliligi"))
                continue;

            Assert.False(
                context.Has(source.RequiredPermission!),
                $"{roleName} rolü '{source.Key}' kaynağını görmemeli.");
        }
    }

    /// <summary>
    /// Her kaynak ya izin ister ya da içeride izin kırılımı yapar.
    /// İzinsiz ve kırılımsız bir kaynak eklenirse bu test kırılır.
    /// </summary>
    [Fact]
    public void EverySource_DeclaresItsPermission()
    {
        using var scope = fixture.Factory.Services.CreateScope();

        // İçeride izin kontrol ettiği için bilinçli olarak izin beyan
        // etmeyen kaynaklar.
        //
        // "bildirimler": bildirim motoru köprüsü. Her bildirim kendi
        // RequiredPermission'ını taşıyor ve okuma anında süzülüyor;
        // köprüye tek bir izin verilseydi, o izni olmayan kullanıcı
        // kendi modülünün bildirimini de göremezdi.
        var permissionlessByDesign = new[] { "bekleyen_onaylar", "bildirimler" };

        var offending = Sources(scope)
            .Where(x => x.RequiredPermission is null &&
                        !permissionlessByDesign.Contains(x.Key))
            .Select(x => x.Key)
            .ToList();

        Assert.True(
            offending.Count == 0,
            "İzin beyan etmeyen brifing kaynağı: " + string.Join(", ", offending));
    }

    /// <summary>Kaynak anahtarları benzersiz olmalı.</summary>
    [Fact]
    public void SourceKeys_AreUnique()
    {
        using var scope = fixture.Factory.Services.CreateScope();

        var keys = Sources(scope).Select(x => x.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    /// <summary>
    /// Yetkisi olmayan kullanıcı için kaynak doğrudan çalıştırılsa bile
    /// brifing servisinin filtresi devrede: servis o kaynağı hiç
    /// çağırmaz. Burada filtre mantığı doğrudan sınanır.
    /// </summary>
    [Theory]
    [MemberData(nameof(FinancialSources))]
    public async Task BriefingFilter_SkipsSourcesWithoutPermission(
        string sourceKey, string requiredPermission)
    {
        var context = await ContextForRoleAsync("Formen");

        using var scope = fixture.Factory.Services.CreateScope();

        var source = Sources(scope).Single(x => x.Key == sourceKey);

        Assert.Equal(requiredPermission, source.RequiredPermission);
        Assert.False(context.Has(requiredPermission));
    }

    /// <summary>
    /// Veri yoksa madde de olmamalı — brifing dolgu cümle uydurmaz.
    /// Kritik stok kaynağı boş veritabanında hiçbir şey döndürmemeli.
    /// </summary>
    [Fact]
    public async Task Sources_ReturnNothingWhenThereIsNoData()
    {
        var context = await ContextForRoleAsync("Genel Müdür");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Kritik stok yoksa madde çıkmamalı.
        // S8: eşik depo satırında; kaynakla AYNI karşılaştırma ("<=").
        var hasCriticalStock = await db.WarehouseStockLevels
            .AnyAsync(level =>
                (db.WarehouseStocks
                    .Where(stock => stock.WarehouseId == level.WarehouseId &&
                                    stock.InventoryItemId == level.InventoryItemId)
                    .Sum(stock => (decimal?)stock.Quantity) ?? 0m) <= level.MinimumQuantity);

        var source = Sources(scope).Single(x => x.Key == "kritik_stok");
        var items = await source.BuildAsync(context, CancellationToken.None);

        if (!hasCriticalStock)
            Assert.Empty(items);
        else
            Assert.NotEmpty(items);
    }

    /// <summary>
    /// Hızır'ın kendi bekleyen eylemleri yalnızca sahibinin brifinginde
    /// görünür — başkasının bekleyen eylemi sayıya girmemeli.
    /// </summary>
    [Fact]
    public async Task PendingActionSource_CountsOnlyOwnActions()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Bekleyen eylem gerçek bir kullanıcıya bağlanır (yabancı anahtar).
        var owner = await ContextWithRealUserAsync(scope, "Genel Müdür");
        var other = await ContextWithRealUserAsync(scope, "Genel Müdür");

        db.HizirPendingActions.Add(new Models.HizirPendingAction
        {
            UserId = owner.UserId,
            ActionName = "eposta_gonder",
            ArgumentsJson = "{}",
            Summary = "brifing testi",
            Status = Models.HizirPendingActionStatus.Pending,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
        });
        await db.SaveChangesAsync();

        var source = Sources(scope).Single(x => x.Key == "onay_bekleyen_hizir_eylemleri");

        Assert.NotEmpty(await source.BuildAsync(owner, CancellationToken.None));
        Assert.Empty(await source.BuildAsync(other, CancellationToken.None));
    }
}
