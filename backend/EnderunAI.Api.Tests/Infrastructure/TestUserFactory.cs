using System.Net.Http.Headers;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnderunAI.Api.Tests.Infrastructure;

/// <summary>
/// Belirli ROLLERLE oturum açmış istemci üretir.
///
/// İzin testlerinin tek gerçek yolu bu: `RequirePermission` gerçekten
/// veritabanından okunan izinlere bakıyor, sahte bir kimlik üretmek
/// kuralı atlatırdı ve test hiçbir şey kanıtlamazdı.
///
/// Aynı desen `PermissionAndScopeTests` içinde özel bir metot olarak
/// da duruyor; oradaki kapsam (şantiye bazlı veri kapsamı) bu ortak
/// yardımcının kapsamından geniş olduğu için birleştirilmedi.
/// </summary>
public static class TestUserFactory
{
    public static async Task<HttpClient> CreateClientWithRolesAsync(
        DatabaseFixture fixture, string usernameSuffix, string[] roleNames)
    {
        const string password = "TestRole!2026Secure";

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        var username = $"test-{usernameSuffix}-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = $"Test {usernameSuffix}",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            // İzin mantığı sınanıyor, mesai saati mantığı değil:
            // test gecenin hangi saatinde koşarsa koşsun aynı sonucu
            // versin diye kullanıcı mesai istisnalı.
            WorkHoursExempt = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var roles = await db.Roles
            .Where(role => roleNames.Contains(role.Name))
            .ToListAsync();

        if (roles.Count != roleNames.Length)
        {
            var missing = roleNames.Except(roles.Select(x => x.Name));
            throw new InvalidOperationException(
                $"Rol bulunamadı: {string.Join(", ", missing)}. "
                + "Rol adı RoleCatalog ile birebir eşleşmeli.");
        }

        db.UserRoles.AddRange(roles.Select(role => new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id
        }));

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
