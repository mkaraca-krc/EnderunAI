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
/// Tekrarlayan gider şablonu: TAHMİNİ → GERÇEKLEŞEN.
///
/// R5 ÇİFT SAYIM: bir dönem için tahmini ve gerçekleşen asla
/// birlikte sayılmaz. Bu testler kuralı iki yönden sabitliyor —
/// gerçekleşen girilince o dönemin tahminisi düşer, ve aynı dönem
/// ikinci kez kesinleştirilemez.
/// </summary>
[Collection("Integration")]
public sealed class RecurringExpenseTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid BranchId, Guid ProjectId);

    private static readonly string[] FullPermissions =
    [
        PermissionCatalog.Keys.ExpenseView,
        PermissionCatalog.Keys.ExpenseManage,
        PermissionCatalog.Keys.ExtraPaymentView
    ];

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

        await ExpenseCategoryProvisioner.EnsureAsync(
            db, project.CompanyId, CancellationToken.None);

        return new Context(project.CompanyId, project.BranchId, project.Id);
    }

    private async Task<HttpClient> ClientWithAsync(string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "TestTekrar!2026";
        string username;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestTekrar-{suffix}" };
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

            username = $"tekrar-{suffix}";
            var hash = passwords.Hash(password);

            db.Users.Add(new AppUser
            {
                Username = username,
                FullName = "Tekrarlayan Gider Test",
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

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object TemplatePayload(
        Context context, Guid categoryId, decimal estimated,
        int startYear, int startMonth,
        ExpensePaymentMethod method = ExpensePaymentMethod.Bank,
        string description = "Ofis elektriği",
        int? endYear = null, int? endMonth = null) =>
        new
        {
            companyId = context.CompanyId,
            centerType = (int)ExpenseCenterType.Branch,
            centerId = context.BranchId,
            expenseCategoryId = categoryId,
            description,
            estimatedAmount = estimated,
            paymentMethod = (int)method,
            supplierCurrentAccountId = (Guid?)null,
            startYear,
            startMonth,
            endYear,
            endMonth,
            paymentDay = 15
        };

    // ---------------- R5 ----------------

    /// <summary>
    /// ANA TEST: gerçekleşen girilince o dönemin TAHMİNİSİ DÜŞER.
    /// Düşmeseydi elektrik gideri hem tahmini hem gerçek tutarıyla
    /// sayılır, merkez toplamı neredeyse iki katına çıkardı.
    /// </summary>
    [Fact]
    public async Task ConfirmedPeriod_ReplacesItsEstimate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var utilities = await CategoryIdAsync(
            context.CompanyId, ExpenseCategoryCatalog.Utilities);

        var today = DateTime.UtcNow.Date;

        var created = await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/tekrarlayan",
            TemplatePayload(context, utilities, 5_000m, today.Year, today.Month)));

        var templateId = created.GetProperty("id").GetGuid();

        // Onaydan ÖNCE: dönem tahmini bekliyor.
        var before = await ReadAsync(await client.GetAsync(
            $"/api/expenses/tekrarlayan?companyId={context.CompanyId}" +
            $"&year={today.Year}&month={today.Month}"));

        var beforePeriod = before.GetProperty("periods").EnumerateArray()
            .Single(x => x.GetProperty("templateId").GetGuid() == templateId);

        Assert.False(beforePeriod.GetProperty("isConfirmed").GetBoolean());
        Assert.Equal(5_000m, beforePeriod.GetProperty("estimatedAmount").GetDecimal());

        // Gerçekleşen: fatura 6.240 geldi.
        var confirmed = await ReadAsync(await client.PostAsJsonAsync(
            $"/api/expenses/tekrarlayan/{templateId}/gerceklesen", new
            {
                year = today.Year,
                month = today.Month,
                actualAmount = 6_240m,
                documentType = (int)ExpenseDocumentType.Invoice,
                documentNumber = "ELK-2026-08"
            }));

        Assert.NotEqual(Guid.Empty, confirmed.GetProperty("entryId").GetGuid());

        // Onaydan SONRA: dönem kesinleşmiş, gerçek tutarı taşıyor.
        var after = await ReadAsync(await client.GetAsync(
            $"/api/expenses/tekrarlayan?companyId={context.CompanyId}" +
            $"&year={today.Year}&month={today.Month}"));

        var afterPeriod = after.GetProperty("periods").EnumerateArray()
            .Single(x => x.GetProperty("templateId").GetGuid() == templateId);

        Assert.True(afterPeriod.GetProperty("isConfirmed").GetBoolean());
        Assert.Equal(6_240m, afterPeriod.GetProperty("actualAmount").GetDecimal());

        // Gider kayıtlarında TEK satır var — tahmini ayrı bir kayıt
        // olarak yazılmıyor.
        var entries = await ReadAsync(await client.GetAsync(
            $"/api/expenses/kayitlar?companyId={context.CompanyId}"));

        Assert.Equal(1, entries.GetProperty("items").GetArrayLength());
        Assert.Equal(6_240m, entries.GetProperty("total").GetDecimal());
    }

    /// <summary>
    /// AYNI DÖNEM İKİNCİ KEZ KESİNLEŞMEZ: ikinci onay aynı ayı iki
    /// kez saydırırdı. Düzeltme gider kaydından yapılır.
    /// </summary>
    [Fact]
    public async Task Period_CannotBeConfirmedTwice()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);
        var today = DateTime.UtcNow.Date;

        var created = await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/tekrarlayan",
            TemplatePayload(context, rent, 40_000m, today.Year, today.Month,
                description: "Ofis kirası")));

        var templateId = created.GetProperty("id").GetGuid();

        var body = new
        {
            year = today.Year,
            month = today.Month,
            actualAmount = 40_000m,
            documentType = (int)ExpenseDocumentType.Receipt,
            documentNumber = (string?)null
        };

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/expenses/tekrarlayan/{templateId}/gerceklesen", body)).StatusCode);

        var second = await client.PostAsJsonAsync(
            $"/api/expenses/tekrarlayan/{templateId}/gerceklesen", body);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("zaten kesinleşmiş",
            await second.Content.ReadAsStringAsync());

        // Tek gider kaydı kaldı.
        var entries = await ReadAsync(await client.GetAsync(
            $"/api/expenses/kayitlar?companyId={context.CompanyId}"));

        Assert.Equal(1, entries.GetProperty("items").GetArrayLength());
    }

    /// <summary>
    /// Şablonun başlangıcından önceki ve bitişinden sonraki dönem
    /// kesinleştirilemez: kapsam dışı bir ayı onaylamak, olmayan bir
    /// gideri deftere sokardı.
    /// </summary>
    [Fact]
    public async Task PeriodsOutsideTheTemplateRange_AreRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        var created = await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/tekrarlayan",
            TemplatePayload(context, rent, 10_000m, 2026, 3,
                description: "Kısa süreli kira",
                endYear: 2026, endMonth: 5)));

        var templateId = created.GetProperty("id").GetGuid();

        var early = await client.PostAsJsonAsync(
            $"/api/expenses/tekrarlayan/{templateId}/gerceklesen", new
            {
                year = 2026,
                month = 2,
                actualAmount = 10_000m,
                documentType = (int)ExpenseDocumentType.Receipt,
                documentNumber = (string?)null
            });

        Assert.Equal(HttpStatusCode.BadRequest, early.StatusCode);

        var late = await client.PostAsJsonAsync(
            $"/api/expenses/tekrarlayan/{templateId}/gerceklesen", new
            {
                year = 2026,
                month = 6,
                actualAmount = 10_000m,
                documentType = (int)ExpenseDocumentType.Receipt,
                documentNumber = (string?)null
            });

        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
    }

    /// <summary>
    /// Dönemler şablonun aralığına göre üretiliyor: başlangıçtan
    /// önce yok, bitişten sonra yok.
    /// </summary>
    [Fact]
    public async Task PeriodStates_FollowTheTemplateRange()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/tekrarlayan",
            TemplatePayload(context, rent, 10_000m, 2026, 3,
                description: "Aralıklı kira",
                endYear: 2026, endMonth: 5)));

        using var scope = fixture.Factory.Services.CreateScope();
        var recurring = scope.ServiceProvider.GetRequiredService<RecurringExpenseService>();

        var states = await recurring.GetPeriodStatesAsync(
            context.CompanyId,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        Assert.Equal(3, states.Count);
        Assert.Equal([3, 4, 5], states.Select(x => x.Month).OrderBy(x => x).ToArray());

        // Ödeme günü ayın 15'i.
        Assert.All(states, x => Assert.Equal(15, x.DueDate.Day));
    }

    /// <summary>
    /// Durdurulan şablon dönem üretmiyor ama geçmişte doğmuş
    /// gerçekleşen kayıtlar duruyor — silme yerine durdurma bunun
    /// için.
    /// </summary>
    [Fact]
    public async Task StoppedTemplate_ProducesNoNewPeriodsButKeepsHistory()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);
        var today = DateTime.UtcNow.Date;

        var created = await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/tekrarlayan",
            TemplatePayload(context, rent, 20_000m, today.Year, today.Month,
                description: "Durdurulacak kira")));

        var templateId = created.GetProperty("id").GetGuid();

        await ReadAsync(await client.PostAsJsonAsync(
            $"/api/expenses/tekrarlayan/{templateId}/gerceklesen", new
            {
                year = today.Year,
                month = today.Month,
                actualAmount = 20_000m,
                documentType = (int)ExpenseDocumentType.Receipt,
                documentNumber = (string?)null
            }));

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            $"/api/expenses/tekrarlayan/{templateId}/durdur", null)).StatusCode);

        var after = await ReadAsync(await client.GetAsync(
            $"/api/expenses/tekrarlayan?companyId={context.CompanyId}" +
            $"&year={today.Year}&month={today.Month}"));

        // Durdurulan şablon dönem üretmiyor.
        Assert.DoesNotContain(after.GetProperty("periods").EnumerateArray(),
            x => x.GetProperty("templateId").GetGuid() == templateId);

        // Ama gider kaydı yerinde.
        var entries = await ReadAsync(await client.GetAsync(
            $"/api/expenses/kayitlar?companyId={context.CompanyId}"));

        Assert.Equal(20_000m, entries.GetProperty("total").GetDecimal());
    }

    // ---------------- Ortak kurallar ----------------

    /// <summary>
    /// Şablon da gider kaydıyla AYNI doğrulamadan geçiyor: otomatik
    /// kategoriye tekrarlayan gider tanımlanamaz.
    /// </summary>
    [Fact]
    public async Task Template_RejectsAutomaticCategory()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var labor = await CategoryIdAsync(
            context.CompanyId, ExpenseCategoryCatalog.Labor);

        var response = await client.PostAsJsonAsync(
            "/api/expenses/tekrarlayan",
            TemplatePayload(context, labor, 100_000m, 2026, 3,
                description: "Aylık işçilik"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("iki kez", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Elden ödenen şablon da elden kalemdir: yetkisizde hiç
    /// görünmez ve açılamaz. Görünseydi tutarı şablondan okunurdu.
    /// </summary>
    [Fact]
    public async Task CashTemplate_IsHiddenAndCannotBeCreatedWithoutPermission()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var privileged = await ClientWithAsync(FullPermissions);
        var meals = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Meals);
        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        await ReadAsync(await privileged.PostAsJsonAsync(
            "/api/expenses/tekrarlayan",
            TemplatePayload(context, meals, 8_000m, 2026, 3,
                ExpensePaymentMethod.Cash, "Elden yemek")));

        await ReadAsync(await privileged.PostAsJsonAsync(
            "/api/expenses/tekrarlayan",
            TemplatePayload(context, rent, 30_000m, 2026, 3,
                description: "Banka kirası")));

        var limited = await ClientWithAsync(WithoutCashPermissions);

        var masked = await ReadAsync(await limited.GetAsync(
            $"/api/expenses/tekrarlayan?companyId={context.CompanyId}"));

        Assert.Equal(1, masked.GetProperty("templates").GetArrayLength());
        Assert.Equal(1, masked.GetProperty("hiddenCount").GetInt32());

        var forbidden = await limited.PostAsJsonAsync(
            "/api/expenses/tekrarlayan",
            TemplatePayload(context, meals, 2_000m, 2026, 3,
                ExpensePaymentMethod.Cash, "Gizli şablon"));

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Endpoints_RequireExpensePermissions()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var outsider = await ClientWithAsync([PermissionCatalog.Keys.ProjectsView]);

        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(
            $"/api/expenses/tekrarlayan?companyId={context.CompanyId}")).StatusCode);

        var readOnly = await ClientWithAsync([PermissionCatalog.Keys.ExpenseView]);
        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.PostAsJsonAsync(
            "/api/expenses/tekrarlayan",
            TemplatePayload(context, rent, 1_000m, 2026, 3))).StatusCode);
    }

    // ---------------- Nakit akış stopgap devri ----------------

    /// <summary>
    /// DEVİR: eski "tahmini gider" satırı gider merkezine taşınıyor
    /// ve ESKİSİ SİLİNİYOR.
    ///
    /// Tek işlemde olması şart: taşıma ile silme ayrı adımlar
    /// olsaydı, aradaki pencerede aynı kira hem eski tabloda hem
    /// şablonda durur ve nakit akışta iki kez çıkardı (R6).
    /// </summary>
    [Fact]
    public async Task LegacyEstimatedExpense_IsAdoptedAndTheOldRowIsRemoved()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);
        var today = DateTime.UtcNow.Date;

        Guid legacyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var legacy = new EnderunAI.Api.Models.CashFlowEstimatedExpense
            {
                CompanyId = context.CompanyId,
                Description = $"Eski kira {suffix}",
                Amount = 25_000m,
                StartYear = today.Year,
                StartMonth = today.Month,
                RecurrenceCount = 6,
                PaymentDay = 10
            };

            db.CashFlowEstimatedExpenses.Add(legacy);
            await db.SaveChangesAsync();

            legacyId = legacy.Id;
        }

        var adopted = await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/tekrarlayan/devral", new
            {
                estimatedExpenseId = legacyId,
                centerType = (int)ExpenseCenterType.Branch,
                centerId = context.BranchId,
                expenseCategoryId = rent,
                paymentMethod = (int)ExpensePaymentMethod.Bank
            }));

        var templateId = adopted.GetProperty("id").GetGuid();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Eski satır gitti.
            Assert.False(await db.CashFlowEstimatedExpenses
                .AnyAsync(x => x.Id == legacyId));

            var template = await db.RecurringExpenseTemplates
                .SingleAsync(x => x.Id == templateId);

            Assert.Equal(25_000m, template.EstimatedAmount);
            Assert.Equal(10, template.PaymentDay);
            Assert.Equal($"Eski kira {suffix}", template.Description);

            // TEKRAR SAYISI BİTİŞ DÖNEMİNE ÇEVRİLDİ: 6 tekrar =
            // başlangıç + 5 ay. Süresiz akan bir tahmin, kimsenin
            // gözden geçirmediği bir varsayıma dönüşürdü.
            var expectedEnd = new DateTime(today.Year, today.Month, 1, 0, 0, 0,
                DateTimeKind.Utc).AddMonths(5);

            Assert.Equal(expectedEnd.Year, template.EndYear);
            Assert.Equal(expectedEnd.Month, template.EndMonth);
        }
    }

    /// <summary>
    /// Devirde de aynı doğrulama: otomatik kategoriye taşınamaz.
    /// Taşınabilseydi eski kira satırı "işçilik" olarak akar ve
    /// puantajdan gelenle çakışırdı.
    /// </summary>
    [Fact]
    public async Task Adoption_RejectsAnAutomaticCategory()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var labor = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Labor);
        var today = DateTime.UtcNow.Date;

        Guid legacyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var legacy = new EnderunAI.Api.Models.CashFlowEstimatedExpense
            {
                CompanyId = context.CompanyId,
                Description = $"Eski gider {suffix}",
                Amount = 1_000m,
                StartYear = today.Year,
                StartMonth = today.Month,
                RecurrenceCount = 2,
                PaymentDay = 1
            };

            db.CashFlowEstimatedExpenses.Add(legacy);
            await db.SaveChangesAsync();

            legacyId = legacy.Id;
        }

        var response = await client.PostAsJsonAsync(
            "/api/expenses/tekrarlayan/devral", new
            {
                estimatedExpenseId = legacyId,
                centerType = (int)ExpenseCenterType.Branch,
                centerId = context.BranchId,
                expenseCategoryId = labor,
                paymentMethod = (int)ExpensePaymentMethod.Bank
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Başarısız devir eski satırı SİLMEDİ.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.CashFlowEstimatedExpenses.AnyAsync(x => x.Id == legacyId));
        }
    }

    /// <summary>
    /// G2 DERSİ: dönem parametresi GERÇEK değerle sınanıyor. Boş
    /// çağrı, dönem hesabındaki bir hatayı göstermezdi.
    /// </summary>
    [Fact]
    public async Task PeriodQuery_WorksWithRealYearAndMonth()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/tekrarlayan",
            TemplatePayload(context, rent, 12_000m, 2026, 4,
                description: "Nisan-Haziran kirası",
                endYear: 2026, endMonth: 6)));

        // Kapsam İÇİ ay: dönem var.
        var inside = await ReadAsync(await client.GetAsync(
            $"/api/expenses/tekrarlayan?companyId={context.CompanyId}&year=2026&month=5"));

        Assert.Equal(1, inside.GetProperty("periods").GetArrayLength());
        Assert.Equal(12_000m, inside.GetProperty("periods")[0]
            .GetProperty("estimatedAmount").GetDecimal());

        // Kapsam DIŞI ay: dönem yok ama şablon listede duruyor.
        var outside = await ReadAsync(await client.GetAsync(
            $"/api/expenses/tekrarlayan?companyId={context.CompanyId}&year=2026&month=9"));

        Assert.Equal(0, outside.GetProperty("periods").GetArrayLength());
        Assert.Equal(1, outside.GetProperty("templates").GetArrayLength());
    }
}
