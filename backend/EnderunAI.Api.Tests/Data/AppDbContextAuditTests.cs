using System.Security.Claims;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Tests.Data;

public sealed class AppDbContextAuditTests
{
    [Fact]
    public async Task SaveChangesWritesCurrentUserToAuditFields()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateContext(userId);
        var company = new Company
        {
            Code = "END",
            Name = "Enderun Enerji"
        };

        db.Companies.Add(company);
        await db.SaveChangesAsync();

        Assert.Equal(userId, company.CreatedByUserId);

        company.Name = "Enderun Enerji A.Ş.";
        await db.SaveChangesAsync();

        Assert.Equal(userId, company.UpdatedByUserId);
        Assert.NotNull(company.UpdatedAtUtc);
    }

    [Fact]
    public async Task DeleteIsConvertedToSoftDeleteWithCurrentUser()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateContext(userId);
        var company = new Company
        {
            Code = "BIR",
            Name = "Birun Savunma"
        };

        db.Companies.Add(company);
        await db.SaveChangesAsync();

        db.Companies.Remove(company);
        await db.SaveChangesAsync();

        Assert.True(company.IsDeleted);
        Assert.False(company.IsActive);
        Assert.Equal(userId, company.DeletedByUserId);
        Assert.NotNull(company.DeletedAtUtc);
    }

    private static AppDbContext CreateContext(Guid userId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        ], "Test");
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return new AppDbContext(
            options,
            new CurrentUserService(accessor));
    }
}
