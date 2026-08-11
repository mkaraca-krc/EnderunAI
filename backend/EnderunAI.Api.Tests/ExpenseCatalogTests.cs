using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Expenses;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Gider merkezinin iki ekseni: KATEGORİ (neye harcadık) ve MERKEZ
/// (nereye harcadık).
///
/// Kategori parametrik, merkez türetilmiş. Bu testler ikisinin de
/// sözleşmesini sabitliyor: sistem kategorisinin kodu değişmez ve
/// silinmez, otomatik kategoriler elle giriş için işaretlidir,
/// merkez listesi şube + proje + şantiyeden türer ve başka şirketin
/// merkezi kabul edilmez.
/// </summary>
[Collection("Integration")]
public sealed class ExpenseCatalogTests(DatabaseFixture fixture)
{
    private static readonly string[] ExpensePermissions =
    [
        PermissionCatalog.Keys.ExpenseView,
        PermissionCatalog.Keys.ExpenseManage
    ];

    private async Task<HttpClient> ClientWithAsync(string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "TestGider!2026";
        string username;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestGider-{suffix}" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            var permissions = await db.Permissions
                .Where(x => permissionKeys.Contains(x.Key))
                .ToListAsync();

            foreach (var permission in permissions)
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permission.Id
                });

            username = $"gider-{suffix}";
            var hash = passwords.Hash(password);

            db.Users.Add(new AppUser
            {
                Username = username,
                FullName = "Gider Test Kullanıcısı",
                PasswordHash = hash.Hash,
                PasswordSalt = hash.Salt,
                IsActive = true,
                WorkHoursExempt = true
            });

            await db.SaveChangesAsync();

            var user = await db.Users.SingleAsync(x => x.Username == username);

            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            db.UserDataScopes.Add(new UserDataScope
            {
                UserId = user.Id,
                ScopeType = DataScopeType.All
            });

            await db.SaveChangesAsync();
        }

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ---------------- Kategori ----------------

    /// <summary>
    /// SONRADAN AÇILAN ŞİRKET: seeder yalnızca açılışta koşuyor.
    /// Kategori listesi okunurken tamamlanmasaydı, bugün açılan bir
    /// şirkette gider kaydı hiç açılamazdı.
    /// </summary>
    [Fact]
    public async Task Categories_AreProvisionedForACompanyCreatedAfterStartup()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await TestDataFactory.CreateProjectAsync(db, suffix);
            companyId = project.CompanyId;

            // Şirket az önce açıldı: hiç kategorisi yok.
            Assert.False(await db.ExpenseCategories
                .AnyAsync(x => x.CompanyId == companyId));
        }

        var client = await ClientWithAsync(ExpensePermissions);

        var payload = await ReadAsync(await client.GetAsync(
            $"/api/expenses/kategoriler?companyId={companyId}"));

        var codes = payload.EnumerateArray()
            .Select(x => x.GetProperty("code").GetString())
            .ToList();

        Assert.Equal(ExpenseCategoryCatalog.Defaults.Count, codes.Count);
        Assert.Contains(ExpenseCategoryCatalog.Rent, codes);
        Assert.Contains(ExpenseCategoryCatalog.Utilities, codes);
        Assert.Contains(ExpenseCategoryCatalog.Allowance, codes);
    }

    /// <summary>
    /// Tamamlama İDEMPOTENT: ikinci okuma kategorileri çoğaltmıyor.
    /// </summary>
    [Fact]
    public async Task CategoryProvisioning_IsIdempotent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            companyId = (await TestDataFactory.CreateProjectAsync(db, suffix)).CompanyId;
        }

        var client = await ClientWithAsync(ExpensePermissions);

        for (var i = 0; i < 3; i++)
            await ReadAsync(await client.GetAsync(
                $"/api/expenses/kategoriler?companyId={companyId}"));

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var count = await db.ExpenseCategories
                .CountAsync(x => x.CompanyId == companyId);

            Assert.Equal(ExpenseCategoryCatalog.Defaults.Count, count);
        }
    }

    /// <summary>
    /// Otomatik kategoriler (malzeme, işçilik, taşeron, yol) elle
    /// giriş için İŞARETLİ. İşaret olmasaydı kullanıcı "malzeme"yi
    /// elle girer, aynı gider satın almadan da akar ve çift sayılırdı.
    /// </summary>
    [Fact]
    public async Task AutomaticCategories_AreFlaggedSoTheyCannotBeTypedByHand()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            companyId = (await TestDataFactory.CreateProjectAsync(db, suffix)).CompanyId;
        }

        var client = await ClientWithAsync(ExpensePermissions);

        var payload = await ReadAsync(await client.GetAsync(
            $"/api/expenses/kategoriler?companyId={companyId}"));

        var automatic = payload.EnumerateArray()
            .Where(x => x.GetProperty("isAutomaticOnly").GetBoolean())
            .Select(x => x.GetProperty("code").GetString())
            .ToList();

        Assert.Equal(4, automatic.Count);
        Assert.Contains(ExpenseCategoryCatalog.Material, automatic);
        Assert.Contains(ExpenseCategoryCatalog.Labor, automatic);
        Assert.Contains(ExpenseCategoryCatalog.Subcontractor, automatic);
        Assert.Contains(ExpenseCategoryCatalog.Travel, automatic);

        // Kira gibi elle girilen kategoriler işaretli DEĞİL.
        var rent = payload.EnumerateArray()
            .Single(x => x.GetProperty("code").GetString() == ExpenseCategoryCatalog.Rent);

        Assert.False(rent.GetProperty("isAutomaticOnly").GetBoolean());
    }

    /// <summary>
    /// Sistem kategorisi SİLİNMEZ: silinseydi ona bağlı geçmiş
    /// kayıtlar kategorisiz kalır, otomatik akış kategoriyi bulamazdı.
    /// Pasife alma serbest.
    /// </summary>
    [Fact]
    public async Task SystemCategory_CannotBeDeletedButCanBeDeactivated()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            companyId = (await TestDataFactory.CreateProjectAsync(db, suffix)).CompanyId;
        }

        var client = await ClientWithAsync(ExpensePermissions);

        var payload = await ReadAsync(await client.GetAsync(
            $"/api/expenses/kategoriler?companyId={companyId}"));

        var stationery = payload.EnumerateArray()
            .Single(x => x.GetProperty("code").GetString() == ExpenseCategoryCatalog.Stationery);

        var id = stationery.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.DeleteAsync($"/api/expenses/kategoriler/{id}")).StatusCode);

        // Ad ve aktiflik değişebiliyor.
        var updated = await client.PutAsJsonAsync(
            $"/api/expenses/kategoriler/{id}", new
            {
                companyId,
                name = "Kırtasiye ve baskı",
                sortOrder = 45,
                isActive = false
            });

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var visible = await ReadAsync(await client.GetAsync(
            $"/api/expenses/kategoriler?companyId={companyId}"));

        Assert.DoesNotContain(visible.EnumerateArray(),
            x => x.GetProperty("id").GetGuid() == id);

        var all = await ReadAsync(await client.GetAsync(
            $"/api/expenses/kategoriler?companyId={companyId}&includeInactive=true"));

        var again = all.EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == id);

        // KOD DEĞİŞMEDİ: otomatik kalemler koda bağlı.
        Assert.Equal(ExpenseCategoryCatalog.Stationery,
            again.GetProperty("code").GetString());
        Assert.Equal("Kırtasiye ve baskı", again.GetProperty("name").GetString());
    }

    /// <summary>
    /// Şirkete özel kategori açılabiliyor ve adı Türkçe karakterlerden
    /// arınmış bir koda dönüşüyor.
    /// </summary>
    [Fact]
    public async Task CustomCategory_GetsATurkishSafeCode()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            companyId = (await TestDataFactory.CreateProjectAsync(db, suffix)).CompanyId;
        }

        var client = await ClientWithAsync(ExpensePermissions);

        var created = await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/kategoriler", new
            {
                companyId,
                name = "Güvenlik Hizmeti",
                sortOrder = 200,
                isActive = true
            }));

        Assert.Equal("guvenlik-hizmeti", created.GetProperty("code").GetString());

        // Aynı ad ikinci kez açılamaz.
        var duplicate = await client.PostAsJsonAsync(
            "/api/expenses/kategoriler", new
            {
                companyId,
                name = "güvenlik hizmeti",
                sortOrder = 210,
                isActive = true
            });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    // ---------------- Merkez ----------------

    /// <summary>
    /// Merkez listesi TÜRETİLİYOR: şube (merkez ofis), proje ve
    /// şantiye. Ayrı bir tanım tablosu tutulsaydı yeni açılan şantiye
    /// listede görünmezdi.
    /// </summary>
    [Fact]
    public async Task CenterList_IsDerivedFromBranchesProjectsAndSites()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId, projectId, siteId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await TestDataFactory.CreateProjectAsync(db, suffix);

            companyId = project.CompanyId;
            projectId = project.Id;

            var site = new ProjectSite
            {
                ProjectId = project.Id,
                Code = $"SNT-{suffix}",
                Name = $"Test Şantiye {suffix}"
            };

            db.ProjectSites.Add(site);
            await db.SaveChangesAsync();

            siteId = site.Id;
        }

        var client = await ClientWithAsync(ExpensePermissions);

        var payload = await ReadAsync(await client.GetAsync(
            $"/api/expenses/merkezler?companyId={companyId}"));

        var rows = payload.EnumerateArray().ToList();

        // Merkez ofis başta ve "(Merkez)" etiketli.
        var head = rows[0];
        Assert.Equal("Branch", head.GetProperty("type").GetString());
        Assert.True(head.GetProperty("isHeadOffice").GetBoolean());
        Assert.Contains("(Merkez)", head.GetProperty("name").GetString()!);

        Assert.Contains(rows, x =>
            x.GetProperty("type").GetString() == "Project" &&
            x.GetProperty("id").GetGuid() == projectId);

        var siteRow = rows.Single(x =>
            x.GetProperty("type").GetString() == "ProjectSite" &&
            x.GetProperty("id").GetGuid() == siteId);

        // Şantiye kendi projesine bağlı: rapor proje altında toplayabilsin.
        Assert.Equal(projectId, siteRow.GetProperty("parentProjectId").GetGuid());
    }

    /// <summary>
    /// BAŞKA ŞİRKETİN MERKEZİ KABUL EDİLMEZ. Doğrulama tek yerde
    /// (ExpenseCenterResolver) durduğu için gider kaydı, şablon ve
    /// rapor aynı kurala uyuyor.
    /// </summary>
    [Fact]
    public async Task Resolver_RejectsACenterFromAnotherCompany()
    {
        var mine = Guid.NewGuid().ToString("N")[..8];
        var other = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var myProject = await TestDataFactory.CreateProjectAsync(db, mine);
        var otherProject = await TestDataFactory.CreateProjectAsync(db, other);

        var resolver = scope.ServiceProvider.GetRequiredService<ExpenseCenterResolver>();

        Assert.NotNull(await resolver.ResolveAsync(
            myProject.CompanyId, Models.Expenses.ExpenseCenterType.Project,
            myProject.Id, CancellationToken.None));

        Assert.Null(await resolver.ResolveAsync(
            myProject.CompanyId, Models.Expenses.ExpenseCenterType.Project,
            otherProject.Id, CancellationToken.None));
    }

    // ---------------- Yetki ----------------

    /// <summary>
    /// NEGATİF TEST: gider merkezi ayrı anahtarda. Proje maliyeti
    /// görebilen biri (projects.view) şirket geneli gider ekseninden
    /// okuyamaz.
    /// </summary>
    [Fact]
    public async Task Endpoints_RequireExpensePermissions()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            companyId = (await TestDataFactory.CreateProjectAsync(db, suffix)).CompanyId;
        }

        var limited = await ClientWithAsync(
            [PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.FinanceView]);

        Assert.Equal(HttpStatusCode.Forbidden, (await limited.GetAsync(
            $"/api/expenses/kategoriler?companyId={companyId}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await limited.GetAsync(
            $"/api/expenses/merkezler?companyId={companyId}")).StatusCode);

        // Okuyabilen ama yönetemeyen kullanıcı katalog değiştiremez.
        var readOnly = await ClientWithAsync([PermissionCatalog.Keys.ExpenseView]);

        Assert.Equal(HttpStatusCode.OK, (await readOnly.GetAsync(
            $"/api/expenses/kategoriler?companyId={companyId}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.PostAsJsonAsync(
            "/api/expenses/kategoriler", new
            {
                companyId,
                name = "Yetkisiz kategori",
                sortOrder = 300,
                isActive = true
            })).StatusCode);
    }
}
