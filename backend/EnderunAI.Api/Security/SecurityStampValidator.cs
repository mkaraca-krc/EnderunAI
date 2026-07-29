using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Security;

public interface ISecurityStampValidator
{
    Task<bool> ValidateAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}

public sealed class SecurityStampValidator(AppDbContext db)
    : ISecurityStampValidator
{
    public async Task<bool> ValidateAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var userIdValue =
            principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
            principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            principal.FindFirstValue("sub");
        var securityStamp =
            principal.FindFirstValue(TokenService.SecurityStampClaimType);

        if (!Guid.TryParse(userIdValue, out var userId) ||
            string.IsNullOrWhiteSpace(securityStamp))
        {
            return false;
        }

        return await db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId &&
                        user.IsActive &&
                        user.SecurityStamp == securityStamp,
                cancellationToken);
    }
}
