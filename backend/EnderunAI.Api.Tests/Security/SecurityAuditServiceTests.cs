using System.Net;
using EnderunAI.Api.Data;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Tests.Security;

public sealed class SecurityAuditServiceTests
{
    [Fact]
    public async Task WritesAuthenticatedActorAndRequestMetadata()
    {
        var actorId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        context.Request.Headers.UserAgent = "Enderun-Test";
        var service = new SecurityAuditService(
            db,
            new FakeCurrentUser(actorId, "mehmet"),
            new HttpContextAccessor { HttpContext = context });
        var targetId = Guid.NewGuid();

        await service.WriteAsync(
            "user.updated",
            "AppUser",
            targetId,
            new { IsActive = true });

        var auditEvent = await db.SecurityAuditEvents.SingleAsync();
        Assert.Equal(actorId, auditEvent.ActorUserId);
        Assert.Equal("mehmet", auditEvent.ActorUsername);
        Assert.Equal(targetId, auditEvent.EntityId);
        Assert.Equal("127.0.0.1", auditEvent.IpAddress);
        Assert.Contains("IsActive", auditEvent.DetailsJson);
    }

    private sealed class FakeCurrentUser(
        Guid userId,
        string username) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => userId;
        public string? Username => username;
        public string? FullName => username;
        public string? SecurityStamp => "stamp";
        public IReadOnlyCollection<string> Roles => ["Admin"];
        public IReadOnlyCollection<string> Permissions => [];
        public bool IsInRole(string role) =>
            role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
        public bool HasPermission(string permission) => true;
    }
}
