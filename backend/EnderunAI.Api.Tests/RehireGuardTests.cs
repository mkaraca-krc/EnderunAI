using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Tekrar işe alım kapısı: kırmızı engeli ve gerekçeli override.
///
/// İki giriş noktası da aynı kuralı uygular — yeni personel kaydı ve
/// çıkmış personelin yeniden aktifleştirilmesi. Ayrı bir "yeniden
/// işe al" ucu yok: kimlik benzersiz olduğu için yeni kayıt
/// açılamıyor, rehire mevcut kaydın durumunu değiştirmekten geçiyor.
/// </summary>
[Collection("Integration")]
public sealed class RehireGuardTests(DatabaseFixture fixture)
{
    private const string Note = "Devamsızlık ve ekip içi uyumsuzluk";
    private const string OverrideReason = "Şantiye şefi kefil oldu, deneme süreli";

    private sealed record Context(
        Guid CompanyId, Guid PersonnelId, string IdentityNumber);

    private static string NewValidIdentity()
    {
        var random = Random.Shared;
        var digits = new int[11];

        digits[0] = random.Next(1, 10);

        for (var i = 1; i < 9; i++)
            digits[i] = random.Next(0, 10);

        var odd = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
        var even = digits[1] + digits[3] + digits[5] + digits[7];

        digits[9] = ((odd * 7 - even) % 10 + 10) % 10;
        digits[10] = (odd + even + digits[9]) % 10;

        return string.Concat(digits);
    }

    /// <summary>
    /// Kırmızı işaretli, çıkmış bir personel. Kayıt SİLİNMİŞ olarak da
    /// kurulabiliyor — yeni kayıt yolunu sınamak için gerekli, çünkü
    /// silinmemiş kayıtta kimlik benzersizliği zaten devreye girer.
    /// </summary>
    private async Task<Context> CreateContextAsync(
        string suffix, RehireCode? code = RehireCode.Red, bool deleteRecord = false)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, company.Id, suffix);

        var identity = NewValidIdentity();

        personnel.IdentityNumber = identity;
        personnel.Status = PersonnelStatus.Terminated;

        db.PersonnelTerminations.Add(new PersonnelTermination
        {
            CompanyId = company.Id,
            PersonnelId = personnel.Id,
            TerminationDate = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc),
            Reason = TerminationReason.EmployerTerminationWithJustCause,
            Status = TerminationStatus.Finalized,
            RehireCode = code,
            RehireNote = code is RehireCode.Red or RehireCode.Yellow ? Note : null,
            RehireMarkedAtUtc = code is null ? null : DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        if (deleteRecord)
        {
            using var deleteScope = fixture.Factory.Services.CreateScope();
            var deleteDb = deleteScope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var target = await deleteDb.Personnel.SingleAsync(x => x.Id == personnel.Id);

            deleteDb.Personnel.Remove(target);
            await deleteDb.SaveChangesAsync();
        }

        return new Context(company.Id, personnel.Id, identity);
    }

    private async Task<HttpClient> ClientWithAsync(
        string[] permissionKeys, string? roleName = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "TestKapi!2026";
        string username;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

            // İzinler için her zaman kendi test rolümüz: seed'li
            // rollere izin eklemek tüm süiti etkilerdi.
            var role = new AppRole { Name = $"TestKapi-{suffix}" };
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

            username = $"kapi-{suffix}";
            var hash = passwords.Hash(password);

            var user = new AppUser
            {
                Username = username,
                FullName = "Kapı Test Kullanıcısı",
                PasswordHash = hash.Hash,
                PasswordSalt = hash.Salt,
                IsActive = true,
                WorkHoursExempt = true
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

            // Override yetkisi ROL ADINA bakıyor: mevcut rol
            // DEĞİŞTİRİLMEDEN kullanıcıya ikinci rol olarak bağlanıyor.
            if (roleName is not null)
            {
                var namedRole = await db.Roles
                    .SingleOrDefaultAsync(x => x.Name == roleName);

                if (namedRole is null)
                {
                    namedRole = new AppRole { Name = roleName };
                    db.Roles.Add(namedRole);
                    await db.SaveChangesAsync();
                }

                db.UserRoles.Add(new UserRole
                {
                    UserId = user.Id,
                    RoleId = namedRole.Id
                });
            }
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

    private static readonly string[] HrPermissions =
        [PermissionCatalog.Keys.PersonnelView, PermissionCatalog.Keys.PersonnelCreate,
         PermissionCatalog.Keys.PersonnelEdit];

    private static object CreateBody(
        Context context, string suffix, string? overrideReason = null) => new
    {
        companyId = context.CompanyId,
        employeeNumber = $"YENI-{suffix}",
        firstName = "Yeniden",
        lastName = "Alınan",
        identityNumber = context.IdentityNumber,
        rehireOverrideReason = overrideReason
    };

    private async Task<HttpResponseMessage> ReactivateAsync(
        HttpClient client, Context context, string? overrideReason = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var personnel = await db.Personnel.AsNoTracking()
            .SingleAsync(x => x.Id == context.PersonnelId);

        return await client.PutAsJsonAsync($"/api/personnel/{context.PersonnelId}", new
        {
            firstName = personnel.FirstName,
            lastName = personnel.LastName,
            identityNumber = personnel.IdentityNumber,
            status = (int)PersonnelStatus.Active,
            isActive = true,
            rehireOverrideReason = overrideReason
        });
    }

    private async Task<int> OverrideCountAsync(Context context)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.PersonnelRehireOverrides
            .AsNoTracking()
            .CountAsync(x => x.MatchedPersonnelId == context.PersonnelId);
    }

    // ---------------- Yeniden aktifleştirme kapısı ----------------

    /// <summary>
    /// KIRMIZI işaretli personel yeniden aktifleştirilemiyor ve
    /// gerekçe yanıtta görünüyor — körlemesine engel değil.
    /// </summary>
    [Fact]
    public async Task Reactivation_IsBlockedForRedCode()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(HrPermissions);

        var response = await ReactivateAsync(client, context);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();

        Assert.Contains("KIRMIZI", raw);
        Assert.Contains(Note, raw);

        // Durum gerçekten değişmemiş olmalı.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db.Personnel.AsNoTracking()
            .SingleAsync(x => x.Id == context.PersonnelId);

        Assert.Equal(PersonnelStatus.Terminated, stored.Status);
    }

    /// <summary>
    /// Gerekçe verilse bile yetkisiz kullanıcı geçemiyor: kırmızıyı
    /// yalnız GM ve Admin geçer.
    /// </summary>
    [Fact]
    public async Task Override_IsRejectedWithoutTheRole()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        // İK Sorumlusu izinlere sahip ama override rolü değil.
        var client = await ClientWithAsync(HrPermissions, roleName: "İK Sorumlusu");

        var response = await ReactivateAsync(client, context, OverrideReason);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("yetkiniz yok", raw);

        Assert.Equal(0, await OverrideCountAsync(context));
    }

    /// <summary>
    /// Genel Müdür gerekçeyle geçebiliyor ve geçiş DENETİM İZİ
    /// bırakıyor: kim, ne zaman, hangi gerekçeyle.
    /// </summary>
    [Fact]
    public async Task Override_PassesWithReasonAndLeavesAnAuditTrail()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(HrPermissions, roleName: "Genel Müdür");

        var response = await ReactivateAsync(client, context, OverrideReason);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db.Personnel.AsNoTracking()
            .SingleAsync(x => x.Id == context.PersonnelId);

        Assert.Equal(PersonnelStatus.Active, stored.Status);

        var audit = await db.PersonnelRehireOverrides.AsNoTracking()
            .SingleAsync(x => x.MatchedPersonnelId == context.PersonnelId);

        Assert.Equal(OverrideReason, audit.Reason);
        Assert.Equal(RehireCode.Red, audit.OverriddenCode);
        Assert.Equal(context.IdentityNumber, audit.IdentityNumber);
        Assert.NotNull(audit.OverriddenByUserId);
        Assert.Equal(context.PersonnelId, audit.TargetPersonnelId);
    }

    /// <summary>
    /// Her geçiş AYRI kayıt: ikinci override birincisini silmiyor.
    /// Denetim izinin anlamı bu.
    /// </summary>
    [Fact]
    public async Task EachOverride_IsRecordedSeparately()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(HrPermissions, roleName: "Genel Müdür");

        await ReactivateAsync(client, context, OverrideReason);

        // Tekrar çıkarıp tekrar alma: ikinci geçiş.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var personnel = await db.Personnel.SingleAsync(
                x => x.Id == context.PersonnelId);

            personnel.Status = PersonnelStatus.Terminated;
            await db.SaveChangesAsync();
        }

        await ReactivateAsync(client, context, "İkinci kez, farklı gerekçe");

        Assert.Equal(2, await OverrideCountAsync(context));
    }

    /// <summary>SARI işaret engellemiyor: uyarıdır, akışı durdurmaz.</summary>
    [Fact]
    public async Task YellowCode_DoesNotBlock()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, RehireCode.Yellow);

        var client = await ClientWithAsync(HrPermissions);

        var response = await ReactivateAsync(client, context);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await OverrideCountAsync(context));
    }

    /// <summary>Değerlendirilmemiş çıkış nötr: engel yok.</summary>
    [Fact]
    public async Task UnassessedTermination_DoesNotBlock()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, code: null);

        var client = await ClientWithAsync(HrPermissions);

        var response = await ReactivateAsync(client, context);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------- Yeni kayıt kapısı ----------------

    /// <summary>
    /// SİLİNMİŞ kırmızı kayıt yeni personel açılışını engelliyor.
    /// Kimlik benzersizliği silinmiş kaydı görmediği için kapı
    /// olmasaydı bu yol açık kalırdı.
    /// </summary>
    [Fact]
    public async Task NewPersonnel_IsBlockedByDeletedRedRecord()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, deleteRecord: true);

        var client = await ClientWithAsync(HrPermissions);

        var response = await client.PostAsJsonAsync(
            "/api/personnel", CreateBody(context, suffix));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains(Note, raw);
    }

    /// <summary>
    /// Silinmiş kayıt çakışmasında AÇIK mesaj dönüyor.
    ///
    /// Kimlik benzersiz indeksi filtresiz olduğu için yumuşak silinmiş
    /// satır TC'yi işgal etmeye devam ediyor; kapı olmadan bu yol
    /// veritabanı hatasıyla 500 dönüyordu. Yeniden işe alım zaten
    /// mevcut kaydın aktifleştirilmesinden geçer.
    /// </summary>
    [Fact]
    public async Task DeletedRecordConflict_IsExplainedNotCrashed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(
            suffix, RehireCode.Green, deleteRecord: true);

        var client = await ClientWithAsync(HrPermissions, roleName: "Genel Müdür");

        var response = await client.PostAsJsonAsync(
            "/api/personnel", CreateBody(context, suffix, OverrideReason));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("aktifleştirilmesiyle", raw);
    }

    /// <summary>
    /// Eşleşmeyen kimlikte kapı hiç devreye girmiyor ve iz de
    /// bırakmıyor.
    /// </summary>
    [Fact]
    public async Task UnknownIdentity_PassesWithoutAudit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(
                db, suffix);
            companyId = company.Id;
        }

        var client = await ClientWithAsync(HrPermissions);
        var identity = NewValidIdentity();

        var response = await client.PostAsJsonAsync("/api/personnel", new
        {
            companyId,
            employeeNumber = $"TMZ-{suffix}",
            firstName = "Temiz",
            lastName = "Aday",
            identityNumber = identity
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Sayım bu kimliğe özgü: süitteki diğer testler de kayıt
        // bırakıyor, küresel sayım yanıltırdı.
        Assert.Equal(0, await verifyDb.PersonnelRehireOverrides
            .CountAsync(x => x.IdentityNumber == identity));
    }
}
