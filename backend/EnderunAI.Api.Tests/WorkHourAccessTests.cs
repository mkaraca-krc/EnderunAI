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

[Collection("Integration")]
public sealed class WorkHourAccessTests(DatabaseFixture fixture)
{
    private static HttpRequestMessage LoginRequest(string username, string password, string ip) =>
        new(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username, password }),
            Headers = { { "X-Forwarded-For", ip } }
        };

    private static string NewTestIp() => $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";

    /// <summary>
    /// Dinamik olarak, seed listesinde olmayan rastgele isimli bir rol
    /// oluşturur — SeedRoleWorkHourWindowsAsync sabit rol adı listesiyle
    /// çalıştığından bu role asla pencere satırı eklenmez, yani testin
    /// çalıştığı gerçek saatten tamamen bağımsız, deterministik biçimde
    /// "her zaman pencere dışı" bir kullanıcı üretir.
    /// </summary>
    private async Task<(string Username, string Password, Guid UserId, Guid RoleId)> CreateNoWindowUserAsync(
        string suffix,
        bool workHoursExempt = false)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        var role = new AppRole { Name = $"TestNoWindow-{suffix}" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var username = $"test-wh-{suffix}-{Guid.NewGuid():N}"[..40];
        const string password = "TestWorkHour!2026";
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = $"Test Mesai {suffix}",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = workHoursExempt
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        db.UserDataScopes.Add(new UserDataScope { UserId = user.Id, ScopeType = DataScopeType.All });
        await db.SaveChangesAsync();

        return (username, password, user.Id, role.Id);
    }

    [Fact]
    public async Task Login_UserWithNoWorkHourWindow_Returns403AndLogsAudit()
    {
        var (username, password, _, _) = await CreateNoWindowUserAsync("no-window");
        var client = fixture.Factory.CreateClient();

        var response = await client.SendAsync(LoginRequest(username, password, NewTestIp()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("outsideWorkHours").GetBoolean());

        var adminClient = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var auditResponse = await adminClient.GetAsync(
            "/api/security-audit/events?entityType=WorkHourAccess&take=200");
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        var events = await auditResponse.Content.ReadFromJsonAsync<JsonElement>();

        // Uç artık sayfalı sonuç döndürüyor (kırpma kullanıcıya
        // söylensin diye): { items, total, take, hasMore }.
        var found = events.GetProperty("items").EnumerateArray().Any(e =>
            e.GetProperty("actorUsername").GetString() == username &&
            e.GetProperty("action").GetString() == "LoginRejectedOutsideWorkHours");
        Assert.True(found, "Mesai dışı giriş reddi audit log'a düşmedi.");
    }

    [Fact]
    public async Task Login_WorkHoursExemptUser_BypassesRoleWindow()
    {
        var (username, password, _, _) = await CreateNoWindowUserAsync("exempt", workHoursExempt: true);
        var client = fixture.Factory.CreateClient();

        var response = await client.SendAsync(LoginRequest(username, password, NewTestIp()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_GenelMudurRole_AlwaysAllowedRegardlessOfWindow()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        var role = await db.Roles.SingleAsync(r => r.Name == "Genel Müdür");
        var username = $"test-gm-{Guid.NewGuid():N}"[..40];
        const string password = "TestGenelMudur!2026";
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = "Test Genel Müdür",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        db.UserDataScopes.Add(new UserDataScope { UserId = user.Id, ScopeType = DataScopeType.All });
        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();
        var response = await client.SendAsync(LoginRequest(username, password, NewTestIp()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_ActiveTemporaryGrant_AllowsLoginDespiteNoWindow()
    {
        var (username, password, userId, _) = await CreateNoWindowUserAsync("active-grant");

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TemporaryAccessGrants.Add(new TemporaryAccessGrant
            {
                UserId = userId,
                GrantedByUserId = userId,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
            });
            await db.SaveChangesAsync();
        }

        var client = fixture.Factory.CreateClient();
        var response = await client.SendAsync(LoginRequest(username, password, NewTestIp()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_ExpiredTemporaryGrant_StillRejected()
    {
        var (username, password, userId, _) = await CreateNoWindowUserAsync("expired-grant");

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TemporaryAccessGrants.Add(new TemporaryAccessGrant
            {
                UserId = userId,
                GrantedByUserId = userId,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5)
            });
            await db.SaveChangesAsync();
        }

        var client = fixture.Factory.CreateClient();
        var response = await client.SendAsync(LoginRequest(username, password, NewTestIp()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AccessRequestFlow_SubmitApprove_GrantsTemporaryAccess_AndLoginSucceeds()
    {
        var (username, password, userId, _) = await CreateNoWindowUserAsync("flow-approve");
        var client = fixture.Factory.CreateClient();

        var rejected = await client.SendAsync(LoginRequest(username, password, NewTestIp()));
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

        var submitResponse = await client.PostAsJsonAsync("/api/auth/access-requests", new
        {
            username,
            password,
            reason = "Acil hakediş kesim tarihi, mesai dışı erişim gerekiyor."
        });
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var adminClient = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var pendingResponse = await adminClient.GetAsync("/api/access-requests");
        Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);
        var pending = await pendingResponse.Content.ReadFromJsonAsync<JsonElement>();
        var requestId = pending.GetProperty("items").EnumerateArray()
            .First(x => x.GetProperty("username").GetString() == username)
            .GetProperty("id").GetGuid();

        var approveResponse = await adminClient.PostAsJsonAsync(
            $"/api/access-requests/{requestId}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var grant = await db.TemporaryAccessGrants
                .Where(g => g.UserId == userId)
                .OrderByDescending(g => g.CreatedAtUtc)
                .FirstOrDefaultAsync();

            Assert.NotNull(grant);
            var minutesRemaining = (grant!.ExpiresAtUtc - DateTime.UtcNow).TotalMinutes;
            Assert.InRange(minutesRemaining, 115, 121);
        }

        var loginAfterApproval = await client.SendAsync(LoginRequest(username, password, NewTestIp()));
        Assert.Equal(HttpStatusCode.OK, loginAfterApproval.StatusCode);
    }

    [Fact]
    public async Task AccessRequestFlow_Reject_KeepsUserBlocked()
    {
        var (username, password, _, _) = await CreateNoWindowUserAsync("flow-reject");
        var client = fixture.Factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/access-requests", new
        {
            username,
            password,
            reason = "Test reddedilecek talep."
        });

        var adminClient = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var pendingResponse = await adminClient.GetAsync("/api/access-requests");
        var pending = await pendingResponse.Content.ReadFromJsonAsync<JsonElement>();
        var requestId = pending.GetProperty("items").EnumerateArray()
            .First(x => x.GetProperty("username").GetString() == username)
            .GetProperty("id").GetGuid();

        var rejectResponse = await adminClient.PostAsJsonAsync(
            $"/api/access-requests/{requestId}/reject",
            new { rejectionReason = "Gerekçe yetersiz." });
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);

        var loginAfterReject = await client.SendAsync(LoginRequest(username, password, NewTestIp()));
        Assert.Equal(HttpStatusCode.Forbidden, loginAfterReject.StatusCode);
    }

    [Fact]
    public async Task WorkHourAccessMiddleware_CutsOffActiveSessionWhenGrantExpires()
    {
        var (username, password, userId, _) = await CreateNoWindowUserAsync("session-cutoff");

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TemporaryAccessGrants.Add(new TemporaryAccessGrant
            {
                UserId = userId,
                GrantedByUserId = userId,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
            });
            await db.SaveChangesAsync();
        }

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var beforeExpiry = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, beforeExpiry.StatusCode);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var grant = await db.TemporaryAccessGrants.SingleAsync(g => g.UserId == userId);
            grant.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var afterExpiry = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterExpiry.StatusCode);
        var payload = await afterExpiry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("outsideWorkHours").GetBoolean());
    }

    [Fact]
    public async Task CompanySettings_WorkHourWindows_Get_ExcludesAdminAndGenelMudur()
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.GetAsync("/api/company-settings/work-hour-windows");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var roles = await response.Content.ReadFromJsonAsync<JsonElement>();
        var names = roles.EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToArray();

        Assert.DoesNotContain("Admin", names);
        Assert.DoesNotContain("Genel Müdür", names);
        Assert.Contains("Sekreterya", names);
    }

    [Fact]
    public async Task CompanySettings_WorkHourWindows_Put_UpdatesRole_AndRejectsForGenelMudur()
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Guid secretariatRoleId;
        Guid genelMudurRoleId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            secretariatRoleId = (await db.Roles.SingleAsync(r => r.Name == "Sekreterya")).Id;
            genelMudurRoleId = (await db.Roles.SingleAsync(r => r.Name == "Genel Müdür")).Id;
        }

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/company-settings/work-hour-windows/{secretariatRoleId}",
            new
            {
                windows = new[]
                {
                    new { dayOfWeek = 1, startTime = "08:00:00", endTime = "18:00:00" }
                }
            });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var windows = await db.RoleWorkHourWindows
                .Where(w => w.RoleId == secretariatRoleId)
                .ToListAsync();
            Assert.Single(windows);
            Assert.Equal(1, windows[0].DayOfWeek);
            Assert.Equal(new TimeOnly(8, 0), windows[0].StartTime);
            Assert.Equal(new TimeOnly(18, 0), windows[0].EndTime);
        }

        var rejectedResponse = await client.PutAsJsonAsync(
            $"/api/company-settings/work-hour-windows/{genelMudurRoleId}",
            new { windows = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, rejectedResponse.StatusCode);
    }

    [Fact]
    public async Task UserManagement_CreateUser_PersistsWorkHoursExempt()
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var username = $"test-exempt-toggle-{Guid.NewGuid():N}"[..40];

        var response = await client.PostAsJsonAsync("/api/user-management/users", new
        {
            username,
            fullName = "Test Exempt Toggle",
            email = (string?)null,
            roleNames = new[] { "Sekreterya" },
            password = "TestExemptToggle!2026",
            isActive = true,
            allowedPermissions = Array.Empty<string>(),
            deniedPermissions = Array.Empty<string>(),
            projectSiteIds = Array.Empty<Guid>(),
            workHoursExempt = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("user").GetProperty("workHoursExempt").GetBoolean());
    }
}
