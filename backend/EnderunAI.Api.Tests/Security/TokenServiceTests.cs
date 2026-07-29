using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace EnderunAI.Api.Tests.Security;

public sealed class TokenServiceTests
{
    private const string Secret =
        "authentication-test-secret-at-least-32-characters";

    [Fact]
    public void Create_ProducesSignedExpiringTokenWithSecurityStamp()
    {
        var user = CreateUser();
        var service = CreateTokenService();
        var before = DateTime.UtcNow;

        var token = service.Create(
            user,
            new[] { "Admin" },
            new[] { "system.users.manage" });
        var principal = Validate(token);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(
            user.Id.ToString(),
            principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(
            user.SecurityStamp,
            principal.FindFirstValue(
                TokenService.SecurityStampClaimType));
        Assert.True(principal.IsInRole("Admin"));
        Assert.Contains(
            principal.Claims,
            claim => claim.Type == "permissions" &&
                     claim.Value == "system.users.manage");
        Assert.InRange(
            jwt.ValidTo,
            before.Add(TokenService.SessionLifetime)
                .AddSeconds(-5),
            DateTime.UtcNow.Add(TokenService.SessionLifetime)
                .AddSeconds(5));
    }

    [Fact]
    public void Validation_RejectsExpiredToken()
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            SecurityAlgorithms.HmacSha256);
        var expired = new JwtSecurityToken(
            issuer: "EnderunAI",
            audience: "EnderunAI.Web",
            expires: DateTime.UtcNow.AddMinutes(-1),
            signingCredentials: credentials);
        var token =
            new JwtSecurityTokenHandler().WriteToken(expired);

        Assert.Throws<SecurityTokenExpiredException>(
            () => Validate(token));
    }

    private static TokenService CreateTokenService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Secret"] = Secret
                })
            .Build();
        return new TokenService(configuration);
    }

    private static ClaimsPrincipal Validate(string token)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidIssuer = "EnderunAI",
            ValidAudience = "EnderunAI.Web",
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(Secret)),
            ClockSkew = TimeSpan.Zero,
            NameClaimType = JwtRegisteredClaimNames.UniqueName,
            RoleClaimType = ClaimTypes.Role
        };

        return new JwtSecurityTokenHandler()
            .ValidateToken(token, parameters, out _);
    }

    private static AppUser CreateUser() =>
        new()
        {
            Id = Guid.NewGuid(),
            Username = "mehmet",
            FullName = "Mehmet Karacabey",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
}
