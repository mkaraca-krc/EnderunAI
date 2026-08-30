using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EnderunAI.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace EnderunAI.Api.Security;

public sealed class TokenService(IConfiguration configuration)
{
    /// <summary>`enderun_token=` — çerez boyutuna adı da dahil.</summary>
    private const int CerezAdiUzunlugu = 14;

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

        // KODLAMA TEK YERDE: JetonIzinKodlamasi.
        //
        // Üç kodlama (hepsi / tümleyen / liste) ve hangisinin
        // seçileceği kararı orada. Burada tekrar edilseydi biri
        // güncellenip diğeri kalırdı — bu programın en sık hatası.
        claims.AddRange(JetonIzinKodlamasi.Yaz(permissions));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "EnderunAI",
            audience: "EnderunAI.Web",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: credentials);

        var yazilan = new JwtSecurityTokenHandler().WriteToken(token);

        // ═══════════════════════════════════════════════════════════
        // REDDETME MUHAFIZI (JETON/1 · Ş3)
        // ═══════════════════════════════════════════════════════════
        //
        // SINIRI AŞAN JETON ÜRETİLMEZ. Tarayıcı, ad+değer toplamı 4096
        // baytı aşan çerezi SESSİZCE atıyor: giriş 200 dönüyor,
        // Set-Cookie gidiyor, çerez yok sayılıyor, kullanıcı login
        // ekranına geri düşüyor ve HİÇBİR KATMAN "bu çerez atıldı"
        // demiyor.
        //
        // Canlıda bu teşhis saatler aldı (2026-08-29). Bir daha sessiz
        // kalmayacak: sunucu jetonu göndermektense üretmeyi reddediyor
        // ve ne olduğunu açıkça söylüyor. Açık hata, sessiz arızadan
        // her zaman iyidir — kullanıcı yine giremez ama NEDEN
        // giremediği bellidir.
        //
        // EŞİK 4096 DEĞİL, PAYLI 3500: uçurumun kenarında değil,
        // yaklaşırken durmak istiyoruz.
        var cerezBaytlari = CerezAdiUzunlugu + yazilan.Length;

        if (cerezBaytlari > JetonIzinKodlamasi.PayliEsik)
        {
            throw new InvalidOperationException(
                $"Jeton çerez eşiğini aşıyor: {cerezBaytlari} bayt "
                + $"(eşik {JetonIzinKodlamasi.PayliEsik}, tarayıcı sınırı "
                + $"{JetonIzinKodlamasi.CerezSiniri}). Bu jeton gönderilseydi "
                + "tarayıcı çerezi SESSİZCE atar ve kullanıcı giriş "
                + "yapamazdı. İzinler jetona sığmıyor — izin listesinin "
                + "jetondan çıkarılması gerekiyor (DURUM.md · JETON/2).");
        }

        return yazilan;
    }
}
