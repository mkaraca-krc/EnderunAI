using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.HumanResources;

/// <summary>
/// Kapının kararı. <see cref="Blocked"/> ise işlem durur; gerekçeli
/// override ile geçilebilir.
/// </summary>
public sealed record RehireGuardResult(
    bool Blocked,
    string? Message,
    Guid? MatchedPersonnelId,
    RehireCode? Code,
    string? Note);

/// <summary>
/// Tekrar işe alım kapısı.
///
/// İki giriş noktası var — yeni personel kaydı ve çıkmış personelin
/// yeniden aktifleştirilmesi — ve ikisi de AYNI kuralı uygulamak
/// zorunda. Kural tek yerde: iki denetleyiciye kopyalansaydı biri
/// güncellenip diğeri unutulurdu.
///
/// Yalnızca KIRMIZI engeller. Sarı uyarıdır ve akışı durdurmaz:
/// uyarıyı gösterme işi kontrol ucunun (tc-kontrol), engelleme işi
/// buranın.
///
/// Override yalnız GM ve Admin'e açıktır ve GEREKÇE ZORUNLUDUR;
/// gerekçesiz bir override, engeli olmayan bir sisteme eşdeğerdir.
/// Her geçiş ayrı bir kayıt bırakır.
/// </summary>
public sealed class RehireGuardService(
    AppDbContext db,
    ICurrentUserService currentUser,
    IUserAuthorizationService authorization)
{
    /// <summary>Engeli geçebilen roller.</summary>
    private static readonly string[] OverrideRoles = ["Admin", "Genel Müdür"];

    /// <summary>
    /// Kimlik numarası için kapıyı çalıştırır.
    /// </summary>
    /// <param name="targetPersonnelId">
    /// Engelin geçilerek açıldığı/aktifleştirildiği kayıt. Denetim
    /// izine yazılır; yeni personelde kayıt henüz oluşmadığı için
    /// boş gelir.
    /// </param>
    public async Task<RehireGuardResult> EvaluateAsync(
        string? identityNumber,
        string? overrideReason,
        Guid? targetPersonnelId,
        CancellationToken cancellationToken)
    {
        var identity = identityNumber?.Trim();

        if (string.IsNullOrWhiteSpace(identity))
            return new RehireGuardResult(false, null, null, null, null);

        // Silinmiş kayıt da taranır: yumuşak silme, kişinin bizde
        // çalışmış olduğu gerçeğini değiştirmez.
        var match = await db.Personnel
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.IdentityNumber == identity)
            .Select(x => new { x.Id, FullName = x.FirstName + " " + x.LastName })
            .FirstOrDefaultAsync(cancellationToken);

        if (match is null)
            return new RehireGuardResult(false, null, null, null, null);

        var termination = await db.PersonnelTerminations
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.PersonnelId == match.Id && !x.IsDeleted)
            .OrderByDescending(x => x.TerminationDate)
            .Select(x => new
            {
                x.TerminationDate,
                x.RehireCode,
                x.RehireNote
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (termination?.RehireCode != RehireCode.Red)
        {
            return new RehireGuardResult(
                false, null, match.Id, termination?.RehireCode, termination?.RehireNote);
        }

        // --- Kırmızı: engel, override edilebilir ---
        var reason = overrideReason?.Trim();

        if (string.IsNullOrWhiteSpace(reason))
        {
            return new RehireGuardResult(
                Blocked: true,
                Message:
                    $"{match.FullName} {termination.TerminationDate:dd.MM.yyyy} " +
                    "tarihinde KIRMIZI (işe alınamaz) olarak işaretlenerek " +
                    $"ayrılmış. Gerekçe: {termination.RehireNote}. " +
                    "Bu engeli yalnızca Genel Müdür ya da Admin, gerekçe " +
                    "girerek geçebilir.",
                MatchedPersonnelId: match.Id,
                Code: RehireCode.Red,
                Note: termination.RehireNote);
        }

        if (!await CanOverrideAsync(cancellationToken))
        {
            return new RehireGuardResult(
                Blocked: true,
                Message:
                    "Bu engeli geçme yetkiniz yok. Kırmızı işaret yalnızca " +
                    "Genel Müdür ya da Admin tarafından geçilebilir.",
                MatchedPersonnelId: match.Id,
                Code: RehireCode.Red,
                Note: termination.RehireNote);
        }

        // Geçiş kaydı: her override ayrı satır. Tek alan olsaydı
        // ikinci geçiş birincisini silerdi.
        db.PersonnelRehireOverrides.Add(new PersonnelRehireOverride
        {
            MatchedPersonnelId = match.Id,
            TargetPersonnelId = targetPersonnelId,
            IdentityNumber = identity,
            OverriddenCode = RehireCode.Red,
            Reason = reason,
            OverriddenByUserId = currentUser.UserId,
            OverriddenAtUtc = DateTime.UtcNow
        });

        return new RehireGuardResult(
            false, null, match.Id, RehireCode.Red, termination.RehireNote);
    }

    private async Task<bool> CanOverrideAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            return false;

        var snapshot = await authorization.GetAsync(userId, cancellationToken);

        if (snapshot is null || !snapshot.IsActive)
            return false;

        return snapshot.RoleNames.Any(
            role => OverrideRoles.Contains(role, StringComparer.OrdinalIgnoreCase));
    }
}
