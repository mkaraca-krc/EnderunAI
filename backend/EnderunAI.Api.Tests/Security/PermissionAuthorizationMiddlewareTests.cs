using System.Security.Claims;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Tests.Security;

public sealed class PermissionAuthorizationMiddlewareTests
{
    [Fact]
    public async Task RejectsTokenWhenSecurityStampWasRotated()
    {
        var user = new AppUser
        {
            Username = "mehmet",
            FullName = "Mehmet Karacabey",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            SecurityStamp = "current-stamp"
        };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim("security_stamp", "old-stamp")
            ], "Test"));
        var accessor = new HttpContextAccessor { HttpContext = context };
        var nextCalled = false;
        var middleware = new PermissionAuthorizationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

        await middleware.InvokeAsync(
            context,
            db,
            new CurrentUserService(accessor));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }
}
