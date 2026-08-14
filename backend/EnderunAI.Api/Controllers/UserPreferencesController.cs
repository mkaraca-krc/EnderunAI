using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record UserUiPreferenceResponse(
    bool SidebarCollapsed,
    IReadOnlyList<string> FavoritePaths);

public sealed record SaveUserUiPreferenceRequest(
    bool SidebarCollapsed,
    List<string>? FavoritePaths);

/// <summary>
/// Kullanıcının KENDİ arayüz tercihleri.
///
/// AYRI İZİN ANAHTARI YOK ve OLMAMALI: burada saklanan şey kullanıcının
/// kendi menü tercihi. Bir izne bağlansaydı, izni olmayan kullanıcı
/// menüsünü daraltamaz hale gelirdi. Kimlik doğrulaması yeterli.
///
/// BAŞKASININ TERCİHİNE ERİŞİM YOK: kullanıcı kimliği yalnızca
/// oturumdan okunur, istekten ASLA alınmaz. Uç bir kullanıcı kimliği
/// parametresi kabul etseydi, herkes herkesin favorilerini okuyup
/// yazabilirdi.
/// </summary>
[ApiController]
[Authorize]
[Route("api/user-preferences")]
public sealed class UserPreferencesController(
    AppDbContext db,
    ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// Favori sayısı tavanı. Sınırsız bırakılsaydı tek bir istek
    /// satırı megabaytlarca büyütebilirdi; kısayol listesi zaten
    /// gözle taranacak kadar kısa olmalı.
    /// </summary>
    private const int MaxFavorites = 20;

    private const int MaxPathLength = 200;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;

        if (userId is null) return Unauthorized();

        var preference = await db.UserUiPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId.Value, cancellationToken);

        // KAYIT YOKSA VARSAYILAN DÖNER, 404 DEĞİL: tercih belirtmemiş
        // olmak bir hata değil. 404 dönseydi her ekran açılışında
        // arayüzün hata yolunu koşması gerekirdi.
        return Ok(new UserUiPreferenceResponse(
            preference?.SidebarCollapsed ?? false,
            preference?.FavoritePaths ?? []));
    }

    [HttpPut]
    public async Task<IActionResult> Save(
        [FromBody] SaveUserUiPreferenceRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;

        if (userId is null) return Unauthorized();

        var favorites = (request.FavoritePaths ?? [])
            .Select(path => path?.Trim() ?? string.Empty)
            .Where(path => path.Length > 0 && path.Length <= MaxPathLength)
            // Yalnızca uygulama içi yollar. "https://…" ya da
            // "//baska-site" kabul edilseydi, favori çubuğu dış siteye
            // giden bir bağlantıya dönüşürdü.
            .Where(path => path.StartsWith('/') && !path.StartsWith("//"))
            .Distinct()
            .Take(MaxFavorites)
            .ToList();

        var preference = await db.UserUiPreferences
            .FirstOrDefaultAsync(x => x.UserId == userId.Value, cancellationToken);

        if (preference is null)
        {
            preference = new UserUiPreference { UserId = userId.Value };

            // DbSet üzerinden eklenir: izlenen bir üst kaydın
            // koleksiyonuna eklemek EF'te Added yerine Modified
            // işaretlenmesine ve 0 satır güncelleyen UPDATE'e yol açıyor.
            db.UserUiPreferences.Add(preference);
        }

        preference.SidebarCollapsed = request.SidebarCollapsed;
        preference.FavoritePaths = favorites;
        preference.UpdatedAtUtc = DateTime.UtcNow;
        preference.UpdatedByUserId = userId;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new UserUiPreferenceResponse(
            preference.SidebarCollapsed,
            preference.FavoritePaths));
    }
}
