using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EnderunAI.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace EnderunAI.Api.Security;

public sealed class TokenService(IConfiguration configuration)
{
    public string Create(
        AppUser user,
        IEnumerable<string> roles,
        IEnumerable<string>? permissions = null)
    {
        var secret = configuration["Jwt:Secret"]
            ?? Environment.GetEnvironmentVariable("JWT_SECRET")
            ?? throw new InvalidOperationException("JWT_SECRET tanımlı değil.");

        var roleNames = roles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new("full_name", user.FullName),
            new("security_stamp", user.SecurityStamp)
        };

        foreach (var roleName in roleNames)
        {
            claims.Add(new Claim(ClaimTypes.Role, roleName));
            claims.Add(new Claim("roles", roleName));
        }

        foreach (var permission in permissions ?? PermissionCatalog.Resolve(roleNames))
        {
            claims.Add(new Claim("permissions", permission));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "EnderunAI",
            audience: "EnderunAI.Web",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
