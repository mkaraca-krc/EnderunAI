using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EnderunAI.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace EnderunAI.Api.Security;

public sealed class TokenService(IConfiguration configuration)
{
    public string Create(AppUser user, IEnumerable<string> roles)
    {
        var secret = configuration["Jwt:Secret"]
            ?? Environment.GetEnvironmentVariable("JWT_SECRET")
            ?? throw new InvalidOperationException("JWT_SECRET tanımlı değil.");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new("full_name", user.FullName)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

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
