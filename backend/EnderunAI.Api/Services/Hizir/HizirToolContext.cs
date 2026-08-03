using EnderunAI.Api.Security;

namespace EnderunAI.Api.Services.Hizir;

/// <summary>
/// Bir aracın çalıştırılacağı kullanıcı bağlamı. Her araç sorgusunu
/// bu bağlamla sınırlar; modele "şunu gösterme" denmez, veri zaten
/// dönmez.
/// </summary>
public sealed record HizirToolContext(
    Guid UserId,
    string FullName,
    IReadOnlyCollection<string> RoleNames,
    IReadOnlyCollection<string> Permissions,
    CurrentDataScopeSnapshot Scope)
{
    public bool Has(string permission) =>
        Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Aracın çalışması sonucu. Reddedilen çağrılar da sonuç üretir —
/// model neden veri alamadığını bilmeli ki kullanıcıya doğru sebebi
/// söylesin ve veri uydurmasın.
/// </summary>
public sealed record HizirToolOutcome(
    string Content,
    bool Denied = false,
    bool IsError = false);

/// <summary>
/// Salt-okunur bir Hızır aracı. RequiredPermission null ise araç
/// herkese açıktır (ör. kullanım kılavuzu araması).
/// </summary>
public sealed record HizirTool(
    string Name,
    string Description,
    object InputSchema,
    string? RequiredPermission,
    Func<HizirToolContext, IReadOnlyDictionary<string, object?>, CancellationToken,
        Task<HizirToolOutcome>> ExecuteAsync);
