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
        IEnumerable<string> permissions)
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
            new("full_name", user.FullName)
        };

        foreach (var roleName in roleNames)
        {
            claims.Add(new Claim(ClaimTypes.Role, roleName));
            claims.Add(new Claim("roles", roleName));
        }

        // ÇEREZ SINIRI (4096 BAYT) — bu token çereze yazılıyor ve
        // tarayıcılar ad+değer toplamı 4096 baytı aşan çerezi
        // SESSİZCE atıyor. Hiçbir hata çıkmıyor: giriş 200 dönüyor,
        // Set-Cookie gidiyor, tarayıcı çerezi yok sayıyor, sonraki
        // istekte oturum görünmediği için kullanıcı login ekranına
        // geri düşüyor.
        //
        // Kataloğun TAMAMINA sahip kullanıcıda (Admin, Genel Müdür)
        // 129 izin anahtarı tek tek yazılınca token 5391 bayta
        // çıkıyordu; canlıda tam olarak bu yaşandı. Tam yetkide
        // listeyi yazmak zaten gereksiz: tüketici tarafta "hepsi
        // var" demekle aynı anlama geliyor.
        //
        // Kısmi yetkideki kullanıcılarda liste aynen yazılıyor —
        // en geniş özel rol 44 izinde ve sınırın çok altında.
        var permissionList = permissions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var hasEveryPermission = PermissionCatalog.Permissions.All(definition =>
            permissionList.Contains(definition.Key, StringComparer.OrdinalIgnoreCase));

        if (hasEveryPermission)
        {
            claims.Add(new Claim("all_permissions", "true"));
        }
        else
        {
            foreach (var permission in permissionList)
            {
                claims.Add(new Claim("permissions", permission));
            }
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
