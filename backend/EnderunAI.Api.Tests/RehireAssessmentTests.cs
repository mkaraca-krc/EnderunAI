using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Ayrılış değerlendirmesi (tekrar işe alım kodu).
///
/// Yasal çıkış nedeninden AYRI bir katman: neden SGK/İş Kanunu
/// tarafını, kod İK'nın "bu kişiyi yeniden alır mıyız"
/// değerlendirmesini tutar.
///
/// GİZLİLİK: kod ve gerekçe İK/GM'ye özel. Saha personeli
/// (personnel.view — Şantiye Şefi, Formen, İSG Sorumlusu) hiçbir
/// uçtan göremez.
/// </summary>
[Collection("Integration")]
public sealed class RehireAssessmentTests(DatabaseFixture fixture)
{
    private const string Note = "Devamsızlık ve ekip içi uyumsuzluk";

    private sealed record Context(Guid CompanyId, Guid PersonnelId, Guid TerminationId);

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, company.Id, suffix);

        var termination = new PersonnelTermination
        {
            CompanyId = company.Id,
            PersonnelId = personnel.Id,
            TerminationDate = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc),
            Reason = TerminationReason.EmployerTerminationWithJustCause,
            Status = TerminationStatus.Finalized
        };

        db.PersonnelTerminations.Add(termination);
        await db.SaveChangesAsync();

        return new Context(company.Id, personnel.Id, termination.Id);
    }

    private async Task<HttpClient> ClientWithAsync(params string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "TestRehire!2026";
        string username;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestRehire-{suffix}" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            var permissions = await db.Permissions
                .Where(x => permissionKeys.Contains(x.Key))
                .ToListAsync();

            foreach (var permission in permissions)
            {
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permission.Id
                });
            }

            username = $"rehire-{suffix}";
            var hash = passwords.Hash(password);

            var user = new AppUser
            {
                Username = username,
                FullName = "Saha Kullanıcısı",
                PasswordHash = hash.Hash,
                PasswordSalt = hash.Salt,
                IsActive = true,
                WorkHoursExempt = true
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

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

    private static string Url(Context context) =>
        $"/api/personnel-terminations/{context.TerminationId}/rehire-degerlendirmesi";

    // ---------------- İşaretleme ----------------

    /// <summary>
    /// Kırmızı işaret kod, gerekçe ve damgayla birlikte kaydediliyor.
    /// </summary>
    [Fact]
    public async Task RedAssessment_IsStoredWithReasonAndStamp()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelEdit);

        var response = await client.PostAsJsonAsync(Url(context), new
        {
            rehireCode = 2,
            rehireNote = Note
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db.PersonnelTerminations.AsNoTracking()
            .SingleAsync(x => x.Id == context.TerminationId);

        Assert.Equal(RehireCode.Red, stored.RehireCode);
        Assert.Equal(Note, stored.RehireNote);
        Assert.NotNull(stored.RehireMarkedAtUtc);
        Assert.NotNull(stored.RehireMarkedByUserId);

        // Yasal çıkış nedenine dokunulmadı.
        Assert.Equal(
            TerminationReason.EmployerTerminationWithJustCause, stored.Reason);
    }

    /// <summary>
    /// Kırmızı ve sarıda gerekçe ZORUNLU: gerekçesiz bir engel, itiraz
    /// edilemez bir engeldir.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public async Task RedAndYellow_RequireAReason(int code)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelEdit);

        var response = await client.PostAsJsonAsync(Url(context), new
        {
            rehireCode = code,
            rehireNote = "   "
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("gerekçe zorunludur", raw);
    }

    /// <summary>Yeşil gerekçesiz işaretlenebiliyor.</summary>
    [Fact]
    public async Task Green_DoesNotRequireAReason()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelEdit);

        var response = await client.PostAsJsonAsync(Url(context), new
        {
            rehireCode = 0,
            rehireNote = (string?)null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Değerlendirme kaldırılabiliyor ve damga da temizleniyor:
    /// "değerlendirilmedi" nötr bir durumdur, eski damgayı taşımamalı.
    /// </summary>
    [Fact]
    public async Task Assessment_CanBeCleared()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelEdit);

        await client.PostAsJsonAsync(Url(context), new
        {
            rehireCode = 2,
            rehireNote = Note
        });

        var response = await client.PostAsJsonAsync(Url(context), new
        {
            rehireCode = (int?)null,
            rehireNote = (string?)null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db.PersonnelTerminations.AsNoTracking()
            .SingleAsync(x => x.Id == context.TerminationId);

        Assert.Null(stored.RehireCode);
        Assert.Null(stored.RehireMarkedAtUtc);
        Assert.Null(stored.RehireMarkedByUserId);
    }

    [Fact]
    public async Task InvalidCode_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelEdit);

        var response = await client.PostAsJsonAsync(Url(context), new
        {
            rehireCode = 7,
            rehireNote = Note
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------- Gizlilik ----------------

    /// <summary>
    /// NEGATİF TEST: saha personeli değerlendirmeyi okuyamıyor.
    /// personnel.view canlıda Şantiye Şefi, Formen ve İSG
    /// Sorumlusu'nda da var; ayrılış gerekçesi oralara gitmemeli.
    /// </summary>
    [Fact]
    public async Task PersonnelViewOnly_CannotReadTheAssessment()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var marker = await ClientWithAsync(PermissionCatalog.Keys.PersonnelEdit);

        await marker.PostAsJsonAsync(Url(context), new
        {
            rehireCode = 2,
            rehireNote = Note
        });

        var field = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var response = await field.GetAsync(Url(context));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Saha personeli değerlendirme de atayamıyor.</summary>
    [Fact]
    public async Task PersonnelViewOnly_CannotAssign()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var response = await client.PostAsJsonAsync(Url(context), new
        {
            rehireCode = 0,
            rehireNote = (string?)null
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Çıkış LİSTESİ gerekçeyi sızdırmıyor: liste salary.view ile
    /// açık ve ücret yetkisi olan herkes ayrılış gerekçesini
    /// görmemeli. Ham metinde gerekçe aranıyor.
    /// </summary>
    [Fact]
    public async Task TerminationList_DoesNotLeakTheReason()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var marker = await ClientWithAsync(PermissionCatalog.Keys.PersonnelEdit);

        await marker.PostAsJsonAsync(Url(context), new
        {
            rehireCode = 2,
            rehireNote = Note
        });

        var client = await ClientWithAsync(PermissionCatalog.Keys.SalaryView);

        var raw = await (await client.GetAsync("/api/personnel-terminations"))
            .Content.ReadAsStringAsync();

        Assert.DoesNotContain(Note, raw);
        Assert.DoesNotContain("rehireNote", raw);
        Assert.DoesNotContain("rehireCode", raw);
    }

    /// <summary>Yetkili okuduğunda kod, gerekçe ve damga geliyor.</summary>
    [Fact]
    public async Task Authorised_ReadsCodeAndReason()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelEdit);

        await client.PostAsJsonAsync(Url(context), new
        {
            rehireCode = 1,
            rehireNote = Note
        });

        var payload = JsonDocument.Parse(
            await (await client.GetAsync(Url(context))).Content.ReadAsStringAsync())
            .RootElement;

        Assert.Equal(1, payload.GetProperty("rehireCode").GetInt32());
        Assert.Equal(Note, payload.GetProperty("rehireNote").GetString());
        Assert.Contains("Sarı", payload.GetProperty("rehireCodeName").GetString()!);
        Assert.NotEqual(
            JsonValueKind.Null, payload.GetProperty("rehireMarkedAtUtc").ValueKind);
    }
}
