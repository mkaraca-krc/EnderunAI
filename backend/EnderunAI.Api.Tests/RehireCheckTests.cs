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
/// İşe alım öncesi TC kontrolü.
///
/// Form dolmadan, TC doğrulanır doğrulanmaz çalışır. Kırmızı
/// eşleşmede engel KÖRLEMESİNE değil: kim, ne zaman ayrıldı, hangi
/// kod ve gerekçe birlikte döner.
/// </summary>
[Collection("Integration")]
public sealed class RehireCheckTests(DatabaseFixture fixture)
{
    private const string Note = "Devamsızlık ve ekip içi uyumsuzluk";

    private sealed record Context(Guid PersonnelId, string IdentityNumber);

    /// <summary>Geçerli, benzersiz bir TC üretir (checksum'lı).</summary>
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

    private async Task<Context> CreateContextAsync(
        string suffix,
        RehireCode? code = null,
        bool withTermination = true,
        bool deleteRecord = false)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, company.Id, suffix);

        var identity = NewValidIdentity();

        personnel.IdentityNumber = identity;
        personnel.Status = PersonnelStatus.Terminated;
        personnel.EmploymentEndDate =
            new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);

        if (withTermination)
        {
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
        }

        await db.SaveChangesAsync();

        if (deleteRecord)
        {
            // Ayrı kapsamda siliniyor: aynı bağlamda izlenen çıkış
            // kaydı yüzünden EF zorunlu ilişkiyi koparmaya çalışıyor.
            using var deleteScope = fixture.Factory.Services.CreateScope();
            var deleteDb = deleteScope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var target = await deleteDb.Personnel
                .SingleAsync(x => x.Id == personnel.Id);

            deleteDb.Personnel.Remove(target);
            await deleteDb.SaveChangesAsync();
        }

        return new Context(personnel.Id, identity);
    }

    private async Task<HttpClient> ClientWithAsync(params string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "TestKontrol!2026";
        string username;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestKontrol-{suffix}" };
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

            username = $"kontrol-{suffix}";
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

    private static string Url(string identity) =>
        $"/api/hr/ise-alim/tc-kontrol?identityNumber={identity}";

    private async Task<JsonElement> CheckAsync(HttpClient client, string identity)
    {
        var response = await client.GetAsync(Url(identity));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()).RootElement;
    }

    // ---------------- Kararlar ----------------

    /// <summary>
    /// KIRMIZI eşleşme engel kararı veriyor ve gerekçeyi birlikte
    /// döndürüyor — körlemesine engel değil.
    /// </summary>
    [Fact]
    public async Task RedMatch_BlocksAndExplainsWhy()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, RehireCode.Red);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelEdit);

        var payload = await CheckAsync(client, context.IdentityNumber);

        Assert.Equal("blocked", payload.GetProperty("decision").GetString());
        Assert.True(payload.GetProperty("matched").GetBoolean());
        Assert.Equal(2, payload.GetProperty("rehireCode").GetInt32());
        Assert.Equal(Note, payload.GetProperty("rehireNote").GetString());

        // Kim, ne zaman ayrıldı da görünüyor.
        Assert.NotEqual(
            JsonValueKind.Null, payload.GetProperty("terminationDate").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(
            payload.GetProperty("personnelFullName").GetString()));
    }

    [Fact]
    public async Task YellowMatch_WarnsButDoesNotBlock()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, RehireCode.Yellow);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelEdit);

        var payload = await CheckAsync(client, context.IdentityNumber);

        Assert.Equal("warning", payload.GetProperty("decision").GetString());
        Assert.Equal(Note, payload.GetProperty("rehireNote").GetString());
    }

    [Fact]
    public async Task GreenMatch_IsClear()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, RehireCode.Green);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelEdit);

        var payload = await CheckAsync(client, context.IdentityNumber);

        Assert.Equal("clear", payload.GetProperty("decision").GetString());
    }

    /// <summary>
    /// Değerlendirilmemiş çıkış NÖTR: ne engel ne uyarı. Sessizce
    /// "sorunsuz" saymak yerine karar "clear" ama kullanıcı çıkışın
    /// varlığını görür.
    /// </summary>
    [Fact]
    public async Task UnassessedTermination_IsNeutral()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, code: null);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelEdit);

        var payload = await CheckAsync(client, context.IdentityNumber);

        Assert.Equal("clear", payload.GetProperty("decision").GetString());
        Assert.True(payload.GetProperty("hasTermination").GetBoolean());
        Assert.Equal(
            JsonValueKind.Null, payload.GetProperty("rehireCode").ValueKind);
        Assert.Contains("Değerlendirilmedi",
            payload.GetProperty("rehireCodeName").GetString()!);
    }

    [Fact]
    public async Task UnknownIdentity_HasNoMatch()
    {
        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelEdit);

        var payload = await CheckAsync(client, NewValidIdentity());

        Assert.Equal("no-match", payload.GetProperty("decision").GetString());
        Assert.False(payload.GetProperty("matched").GetBoolean());
    }

    /// <summary>
    /// SİLİNMİŞ kayıt da eşleşiyor: yumuşak silme kişinin bizde
    /// çalışmış olduğu gerçeğini değiştirmez ve silinmiş kaydın
    /// arkasına saklanarak yeniden giriş yapılmamalı.
    /// </summary>
    [Fact]
    public async Task DeletedRecord_StillMatches()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(
            suffix, RehireCode.Red, deleteRecord: true);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelEdit);

        var payload = await CheckAsync(client, context.IdentityNumber);

        Assert.Equal("blocked", payload.GetProperty("decision").GetString());
        Assert.True(payload.GetProperty("recordDeleted").GetBoolean());
        Assert.Equal(Note, payload.GetProperty("rehireNote").GetString());
    }

    /// <summary>Çıkış kaydı olmayan eski personel engel üretmiyor.</summary>
    [Fact]
    public async Task MatchWithoutTermination_IsClear()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, withTermination: false);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelEdit);

        var payload = await CheckAsync(client, context.IdentityNumber);

        Assert.Equal("clear", payload.GetProperty("decision").GetString());
        Assert.False(payload.GetProperty("hasTermination").GetBoolean());
    }

    // ---------------- Doğrulama ve yetki ----------------

    /// <summary>
    /// Geçersiz TC ile arama yapılmıyor: yanlış numara yanlış kişiyi
    /// eşleştirebilir.
    /// </summary>
    [Theory]
    [InlineData("12345678901")]
    [InlineData("123")]
    [InlineData("")]
    public async Task InvalidIdentity_IsRejected(string identity)
    {
        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelEdit);

        var response = await client.GetAsync(Url(identity));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// NEGATİF TEST: saha personeli eşleşme uyarısını göremiyor.
    /// personnel.view canlıda Şantiye Şefi, Formen ve İSG
    /// Sorumlusu'nda da var.
    /// </summary>
    [Fact]
    public async Task PersonnelViewOnly_CannotUseTheCheck()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, RehireCode.Red);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var response = await client.GetAsync(Url(context.IdentityNumber));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Note, raw);
    }
}
