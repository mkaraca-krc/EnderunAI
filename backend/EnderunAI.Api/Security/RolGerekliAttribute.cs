namespace EnderunAI.Api.Security;

/// <summary>
/// ROL/1 — ROL KAPISI, JETONDAN DEĞİL TAZE ANLIK GÖRÜNTÜDEN (2026-09-11).
///
/// ═══ NEDEN `[Authorize(Roles = …)]` DEĞİL ═══
///
/// ASP.NET'in rol kapısı rolü JETONDAN okur. Jeton 12 saat yaşar ve arada
/// tazelenmez: ölçüldü — Admin rolü alınan kullanıcı aynı jetonla
/// kullanıcı yönetimi ucundan 200 aldı; yeni Admin yapılan kullanıcı
/// yeniden girişe kadar reddedildi. Ayrıca bu kapının reddi GÜNLÜĞE
/// DÜŞMÜYORDU (ASP.NET kendi yolundan 403 dönüyor).
///
/// Bu nitelik yalnız bir İŞARETTİR; kararı `PermissionAuthorizationMiddleware`
/// verir — izin kararıyla AYNI geçiş noktası, AYNI taze anlık görüntü
/// (`UserAuthorizationService`, istek başına), ret `ErisimRetSebebi.RolYok`
/// ile günlüğe. Anlık görüntü yoksa KAPALI düşer.
///
/// `[Authorize(Roles = …)]` yazmak yasak — `RolTekKaynakTests.Muhafaza_
/// JetondanRolOkunmaz` kaynakları tarar.
///
/// Rolleri ADIYLA anmak ayrı bir borç (YT2 ailesi: sabit rol adı). Bu
/// nitelik o borcu değiştirmiyor; yalnız rolün NEREDEN okunduğunu düzeltiyor.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RolGerekliAttribute(params string[] roller) : Attribute
{
    /// <summary>Herhangi biri yeter.</summary>
    public IReadOnlyList<string> Roller { get; } = roller;
}
