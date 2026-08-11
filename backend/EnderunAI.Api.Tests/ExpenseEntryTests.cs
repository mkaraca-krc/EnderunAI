using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Expenses;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Elle girilen gider kaydı: merkez + kategori ekseni, elden
/// izolasyonu ve çift sayım kapısı.
///
/// Bu testlerin ikisi paketin ana kuralını koruyor:
/// - otomatik kategoriye elle gider girilemez (aynı gider iki
///   kaynaktan sayılmasın),
/// - elden ödenen kalem yetkisiz kullanıcıya HİÇ gelmez ve toplam
///   yalnızca görünen kalemlerden oluşur (tam toplam verilseydi
///   gizlenen tutar çıkarımla ele geçerdi).
/// </summary>
[Collection("Integration")]
public sealed class ExpenseEntryTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid BranchId, Guid ProjectId, Guid SiteId);

    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private static readonly string[] FullPermissions =
    [
        PermissionCatalog.Keys.ExpenseView,
        PermissionCatalog.Keys.ExpenseManage,
        PermissionCatalog.Keys.ExtraPaymentView
    ];

    /// <summary>Gideri yönetir ama elden kalemleri göremez.</summary>
    private static readonly string[] WithoutCashPermissions =
    [
        PermissionCatalog.Keys.ExpenseView,
        PermissionCatalog.Keys.ExpenseManage
    ];

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var site = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"SNT-{suffix}",
            Name = $"Şantiye {suffix}"
        };

        db.ProjectSites.Add(site);
        await db.SaveChangesAsync();

        await ExpenseCategoryProvisioner.EnsureAsync(
            db, project.CompanyId, CancellationToken.None);

        return new Context(project.CompanyId, project.BranchId, project.Id, site.Id);
    }

    private async Task<HttpClient> ClientWithAsync(string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "TestGiderK!2026";
        string username;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestGiderK-{suffix}" };
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

            username = $"giderk-{suffix}";
            var hash = passwords.Hash(password);

            db.Users.Add(new AppUser
            {
                Username = username,
                FullName = "Gider Kayıt Test",
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

    private async Task<Guid> CategoryIdAsync(Guid companyId, string code)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.ExpenseCategories
            .Where(x => x.CompanyId == companyId && x.Code == code)
            .Select(x => x.Id)
            .SingleAsync();
    }

    private static object Payload(
        Context context, Guid categoryId, decimal amount,
        ExpenseCenterType centerType, Guid centerId,
        ExpensePaymentMethod method = ExpensePaymentMethod.Bank,
        string description = "Test gideri",
        DateTime? date = null) =>
        new
        {
            companyId = context.CompanyId,
            centerType = (int)centerType,
            centerId,
            expenseCategoryId = categoryId,
            expenseDate = date ?? Today,
            amount,
            description,
            paymentMethod = (int)method,
            documentType = (int)ExpenseDocumentType.Receipt,
            documentNumber = "FIS-001",
            supplierCurrentAccountId = (Guid?)null
        };

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ---------------- Çift sayım kapısı ----------------

    /// <summary>
    /// ANA KURAL: otomatik akan bir kategoriye elle gider girilemez.
    /// Girilebilseydi satın almadan gelen malzeme gideri bir de elle
    /// yazılır ve merkez toplamı şişerdi.
    /// </summary>
    [Fact]
    public async Task AutomaticCategory_CannotBeEnteredByHand()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        foreach (var code in new[]
        {
            ExpenseCategoryCatalog.Material,
            ExpenseCategoryCatalog.Labor,
            ExpenseCategoryCatalog.Subcontractor,
            ExpenseCategoryCatalog.Travel
        })
        {
            var categoryId = await CategoryIdAsync(context.CompanyId, code);

            var response = await client.PostAsJsonAsync(
                "/api/expenses/kayitlar",
                Payload(context, categoryId, 1_000m,
                    ExpenseCenterType.Project, context.ProjectId));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("iki kez", body);
        }
    }

    /// <summary>Elle girilebilen kategori normal çalışıyor.</summary>
    [Fact]
    public async Task ManualCategory_IsAccepted()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        var created = await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            Payload(context, rent, 45_000m,
                ExpenseCenterType.Branch, context.BranchId,
                description: "Ofis kirası")));

        Assert.NotEqual(Guid.Empty, created.GetProperty("id").GetGuid());
    }

    // ---------------- Merkez ekseni ----------------

    /// <summary>
    /// ŞANTİYE gideri projesine de yazılıyor: "bu projeye ne
    /// harcadık" sorusu şantiye giderlerini dışarıda bırakamaz.
    /// Proje merkezi sorgusu şantiye kalemini de kapsıyor.
    /// </summary>
    [Fact]
    public async Task SiteExpense_RollsUpUnderItsProject()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var supplies = await CategoryIdAsync(
            context.CompanyId, ExpenseCategoryCatalog.Supplies);

        await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            Payload(context, supplies, 3_000m,
                ExpenseCenterType.ProjectSite, context.SiteId,
                description: "Şantiye çay-şeker")));

        // Proje merkezinden sorulunca şantiye kalemi görünüyor.
        var byProject = await ReadAsync(await client.GetAsync(
            $"/api/expenses/kayitlar?companyId={context.CompanyId}" +
            $"&centerType={(int)ExpenseCenterType.Project}&centerId={context.ProjectId}"));

        Assert.Equal(3_000m, byProject.GetProperty("total").GetDecimal());

        // Şubeden sorulunca görünmüyor: merkez ofis gideri değil.
        var byBranch = await ReadAsync(await client.GetAsync(
            $"/api/expenses/kayitlar?companyId={context.CompanyId}" +
            $"&centerType={(int)ExpenseCenterType.Branch}&centerId={context.BranchId}"));

        Assert.Equal(0m, byBranch.GetProperty("total").GetDecimal());
    }

    // ---------------- Elden izolasyonu ----------------

    /// <summary>
    /// ELDEN KALEM YETKİSİZDE HİÇ GELMEZ ve toplam yalnızca görünen
    /// kalemleri kapsar.
    ///
    /// "Tam toplam eksi gizli satır" yaklaşımı bilinçle
    /// kullanılmıyor: toplam tam verilseydi, görünen kalemlerin
    /// farkı gizlenen tutarı birebir ele verirdi.
    /// </summary>
    [Fact]
    public async Task CashExpense_IsInvisibleAndExcludedFromTheTotal()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var privileged = await ClientWithAsync(FullPermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);
        var meals = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Meals);

        await ReadAsync(await privileged.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            Payload(context, rent, 40_000m,
                ExpenseCenterType.Branch, context.BranchId,
                description: "Ofis kirası")));

        await ReadAsync(await privileged.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            Payload(context, meals, 7_500m,
                ExpenseCenterType.Branch, context.BranchId,
                ExpensePaymentMethod.Cash, "Elden yemek ödemesi")));

        var full = await ReadAsync(await privileged.GetAsync(
            $"/api/expenses/kayitlar?companyId={context.CompanyId}"));

        Assert.Equal(47_500m, full.GetProperty("total").GetDecimal());
        Assert.Equal(2, full.GetProperty("items").GetArrayLength());
        Assert.Equal(0, full.GetProperty("hiddenCount").GetInt32());

        var limited = await ClientWithAsync(WithoutCashPermissions);

        var masked = await ReadAsync(await limited.GetAsync(
            $"/api/expenses/kayitlar?companyId={context.CompanyId}"));

        // Satır hiç yok.
        Assert.Equal(1, masked.GetProperty("items").GetArrayLength());
        Assert.DoesNotContain(
            masked.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("paymentMethod").GetString() == "Cash");

        // Toplam yalnız görünen kalem.
        Assert.Equal(40_000m, masked.GetProperty("total").GetDecimal());

        // Ama eksik baktığını biliyor.
        Assert.Equal(1, masked.GetProperty("hiddenCount").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(
            masked.GetProperty("hiddenNote").GetString()));
    }

    /// <summary>
    /// Elden kalemi YAZMAK da yetki istiyor. Yalnız okuma
    /// maskelenseydi, yetkisiz kullanıcı bir gideri elden
    /// işaretleyip kendi görüşünden kaçırabilirdi.
    /// </summary>
    [Fact]
    public async Task CashExpense_CannotBeWrittenOrDeletedWithoutPermission()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var privileged = await ClientWithAsync(FullPermissions);
        var meals = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Meals);

        var existing = await ReadAsync(await privileged.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            Payload(context, meals, 5_000m,
                ExpenseCenterType.Branch, context.BranchId,
                ExpensePaymentMethod.Cash, "Elden ödeme")));

        var id = existing.GetProperty("id").GetGuid();

        var limited = await ClientWithAsync(WithoutCashPermissions);

        var create = await limited.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            Payload(context, meals, 1_000m,
                ExpenseCenterType.Branch, context.BranchId,
                ExpensePaymentMethod.Cash, "Gizli kalem"));

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        var delete = await limited.DeleteAsync($"/api/expenses/kayitlar/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);

        // Banka ödemesini elden'e çevirmek de aynı kapıda.
        var flip = await limited.PutAsJsonAsync(
            $"/api/expenses/kayitlar/{id}",
            Payload(context, meals, 5_000m,
                ExpenseCenterType.Branch, context.BranchId,
                ExpensePaymentMethod.Bank, "Görünür yapmayı dene"));

        Assert.Equal(HttpStatusCode.Forbidden, flip.StatusCode);
    }

    // ---------------- R4: tekrar uyarısı ----------------

    /// <summary>
    /// R4: aynı merkez + kategori + ay içinde yakın tutarlı bir kayıt
    /// varsa kullanıcı UYARILIYOR ama kayıt ENGELLENMİYOR — iki ayrı
    /// yakıt fişi meşrudur, sert engel doğru kayıtları da keserdi.
    /// </summary>
    [Fact]
    public async Task SimilarExpense_WarnsButDoesNotBlock()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        var first = Payload(context, rent, 40_000m,
            ExpenseCenterType.Branch, context.BranchId,
            description: "Ağustos kirası");

        await ReadAsync(await client.PostAsJsonAsync("/api/expenses/kayitlar", first));

        // %2 farklı tutar → tolerans içinde, uyarı çıkıyor.
        var similar = Payload(context, rent, 40_800m,
            ExpenseCenterType.Branch, context.BranchId,
            description: "Ağustos kirası (tekrar)");

        var hints = await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/kayitlar/benzer-kayitlar", similar));

        Assert.Equal(1, hints.GetArrayLength());
        Assert.Equal("Ağustos kirası",
            hints[0].GetProperty("description").GetString());

        // UYARI ENGEL DEĞİL: kayıt yine de açılıyor.
        var created = await client.PostAsJsonAsync("/api/expenses/kayitlar", similar);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        // Uzak tutar uyarı üretmiyor.
        var different = Payload(context, rent, 12_000m,
            ExpenseCenterType.Branch, context.BranchId,
            description: "Depo kirası");

        var none = await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/kayitlar/benzer-kayitlar", different));

        Assert.Equal(0, none.GetArrayLength());

        // Başka merkez de uyarı üretmiyor: aynı ay aynı tutar bile olsa
        // ofis kirası ile proje kirası ayrı giderlerdir.
        var otherCenter = Payload(context, rent, 40_000m,
            ExpenseCenterType.Project, context.ProjectId,
            description: "Proje ofisi kirası");

        var otherHints = await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/kayitlar/benzer-kayitlar", otherCenter));

        Assert.Equal(0, otherHints.GetArrayLength());
    }

    // ---------------- Doğrulama ve yetki ----------------

    [Fact]
    public async Task Validation_RejectsUnknownCenterAndBadAmount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        var unknownCenter = await client.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            Payload(context, rent, 1_000m,
                ExpenseCenterType.Project, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, unknownCenter.StatusCode);

        var zeroAmount = await client.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            Payload(context, rent, 0m,
                ExpenseCenterType.Branch, context.BranchId));

        Assert.Equal(HttpStatusCode.BadRequest, zeroAmount.StatusCode);
    }

    [Fact]
    public async Task Endpoints_RequireExpensePermissions()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var outsider = await ClientWithAsync([PermissionCatalog.Keys.ProjectsView]);

        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(
            $"/api/expenses/kayitlar?companyId={context.CompanyId}")).StatusCode);

        var readOnly = await ClientWithAsync([PermissionCatalog.Keys.ExpenseView]);
        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            Payload(context, rent, 1_000m,
                ExpenseCenterType.Branch, context.BranchId))).StatusCode);
    }
}
