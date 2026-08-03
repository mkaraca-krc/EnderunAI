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
/// Karşılama kişiselleştirmesi: her kullanıcı KENDİ adını görmeli.
///
/// Dashboard karşılaması eskiden sabit "Merhaba Mehmet" yazıyordu;
/// bu testler adın oturumdaki gerçek kullanıcıdan geldiğini doğruluyor.
/// </summary>
[Collection("Integration")]
public sealed class UserGreetingTests(DatabaseFixture fixture)
{
    private async Task<(HttpClient Client, string FullName, string? Honorific)>
        CreateUserAsync(string roleName, string fullName, string? honorific)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "Karsilama!2026Test";
        var username = $"test-greet-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = fullName,
            Honorific = honorific,
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var role = await db.Roles.SingleAsync(x => x.Name == roleName);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
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

        return (client, fullName, honorific);
    }

    /// <summary>
    /// Karşılamanın kaynağı: /api/auth/me her kullanıcı için o
    /// kullanıcının kendi adını ve hitabını dönmeli.
    /// </summary>
    [Theory]
    [InlineData("İK Sorumlusu", "Ahmet Yılmaz", "Bey")]
    [InlineData("Finans Sorumlusu", "Ayşe Demir", "Hanım")]
    [InlineData("Formen", "Zeynep Kaya", null)]
    public async Task Me_ReturnsOwnNameAndHonorific(
        string roleName, string fullName, string? honorific)
    {
        var (client, _, _) = await CreateUserAsync(roleName, fullName, honorific);

        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(fullName, payload.GetProperty("fullName").GetString());

        var returnedHonorific = payload.GetProperty("honorific");
        if (honorific is null)
            Assert.Equal(JsonValueKind.Null, returnedHonorific.ValueKind);
        else
            Assert.Equal(honorific, returnedHonorific.GetString());

        // Sabit kalmış eski ad hiçbir kullanıcıya dönmemeli.
        Assert.DoesNotContain("Mehmet", payload.GetProperty("fullName").GetString()!);
    }

    /// <summary>
    /// İki farklı kullanıcı aynı anda giriş yaptığında her biri kendi
    /// adını almalı — asıl şikâyet buydu.
    /// </summary>
    [Fact]
    public async Task DifferentUsers_EachSeeTheirOwnName()
    {
        var (firstClient, firstName, _) =
            await CreateUserAsync("Teknik Ofis", "Ahmet Yılmaz", "Bey");
        var (secondClient, secondName, _) =
            await CreateUserAsync("Sekreterya", "Ayşe Demir", "Hanım");

        var first = await firstClient.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var second = await secondClient.GetFromJsonAsync<JsonElement>("/api/auth/me");

        Assert.Equal(firstName, first.GetProperty("fullName").GetString());
        Assert.Equal(secondName, second.GetProperty("fullName").GetString());

        Assert.NotEqual(
            first.GetProperty("fullName").GetString(),
            second.GetProperty("fullName").GetString());
    }

    /// <summary>
    /// Hitap yalnızca "Bey" veya "Hanım" olabilir; başka bir değer
    /// gönderilirse kaydedilmez ve nötr hitap kullanılır. Cinsiyet
    /// isimden tahmin edilmiyor.
    /// </summary>
    [Theory]
    [InlineData("Bey", "Bey")]
    [InlineData("Hanım", "Hanım")]
    [InlineData("bey", "Bey")]
    [InlineData("Bay", null)]
    [InlineData("", null)]
    public async Task UserManagement_NormalizesHonorific(
        string input, string? expected)
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var username = $"test-hitap-{Guid.NewGuid():N}"[..30];

        var response = await client.PostAsJsonAsync("/api/user-management/users", new
        {
            username,
            fullName = "Hitap Testi",
            honorific = input,
            roleNames = new[] { "Sekreterya" },
            password = "HitapTesti!2026",
            isActive = true,
            allowedPermissions = Array.Empty<string>(),
            deniedPermissions = Array.Empty<string>(),
            projectSiteIds = Array.Empty<Guid>(),
            workHoursExempt = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db.Users
            .Where(x => x.Username == username)
            .Select(x => x.Honorific)
            .SingleAsync();

        Assert.Equal(expected, stored);
    }
}
