using System.Security.Claims;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Http;

namespace EnderunAI.Api.Tests.Security;

public sealed class CurrentUserServiceTests
{
    [Fact]
    public void ReadsAuthenticatedUserClaims()
    {
        var userId = Guid.NewGuid();
        var service = CreateService(
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, "mehmet"),
            new Claim("full_name", "Mehmet Karacabey"),
            new Claim("security_stamp", "stamp-1"),
            new Claim(ClaimTypes.Role, "Teknik Koordinatör"));

        Assert.True(service.IsAuthenticated);
        Assert.Equal(userId, service.UserId);
        Assert.Equal("mehmet", service.Username);
        Assert.Equal("Mehmet Karacabey", service.FullName);
        Assert.Equal("stamp-1", service.SecurityStamp);
        Assert.True(service.IsInRole("teknik koordinatör"));
    }

    [Fact]
    public void ReadsServerValidatedPermissionClaims()
    {
        var service = CreateService(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "Teknik Koordinatör"),
            new Claim("permissions", PermissionCatalog.Keys.ProjectsManage));

        Assert.Contains(
            PermissionCatalog.Keys.ProjectsManage,
            service.Permissions);
        Assert.True(
            service.HasPermission(PermissionCatalog.Keys.ProjectsManage));
    }

    [Fact]
    public void AllowOverrideAddsKnownPermission()
    {
        var service = CreateService(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "Tekniker"),
            new Claim("permissions", PermissionCatalog.Keys.FinanceView));

        Assert.True(service.HasPermission(PermissionCatalog.Keys.FinanceView));
    }

    private static CurrentUserService CreateService(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "Test");
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return new CurrentUserService(accessor);
    }
}
