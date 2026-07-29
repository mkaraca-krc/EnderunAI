using System.Security.Claims;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Tests.Security;

public sealed class SecurityStampValidatorTests
{
    [Fact]
    public async Task ValidateAsync_AcceptsActiveUserWithMatchingStamp()
    {
        await using var db = CreateDbContext();
        var user = CreateUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var validator = new SecurityStampValidator(db);

        Assert.True(await validator.ValidateAsync(
            CreatePrincipal(user.Id, user.SecurityStamp)));
    }

    [Fact]
    public async Task ValidateAsync_RejectsChangedSecurityStamp()
    {
        await using var db = CreateDbContext();
        var user = CreateUser();
        var issuedStamp = user.SecurityStamp;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();

        var validator = new SecurityStampValidator(db);

        Assert.False(await validator.ValidateAsync(
            CreatePrincipal(user.Id, issuedStamp)));
    }

    [Fact]
    public async Task ValidateAsync_RejectsInactiveUser()
    {
        await using var db = CreateDbContext();
        var user = CreateUser();
        user.IsActive = false;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var validator = new SecurityStampValidator(db);

        Assert.False(await validator.ValidateAsync(
            CreatePrincipal(user.Id, user.SecurityStamp)));
    }

    [Fact]
    public async Task ValidateAsync_RejectsMissingIdentityClaims()
    {
        await using var db = CreateDbContext();
        var validator = new SecurityStampValidator(db);
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(authenticationType: "test"));

        Assert.False(await validator.ValidateAsync(principal));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static AppUser CreateUser() =>
        new()
        {
            Username = $"user-{Guid.NewGuid():N}",
            FullName = "Test User",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true
        };

    private static ClaimsPrincipal CreatePrincipal(
        Guid userId,
        string securityStamp) =>
        new(
            new ClaimsIdentity(
                new[]
                {
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        userId.ToString()),
                    new Claim(
                        TokenService.SecurityStampClaimType,
                        securityStamp)
                },
                "test"));
}
