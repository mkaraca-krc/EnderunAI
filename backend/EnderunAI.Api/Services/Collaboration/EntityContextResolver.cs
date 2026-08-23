using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Collaboration;

/// <summary>
/// Bir kaydın KAPSAM BAĞLAMI: hangi şirket, hangi proje.
/// </summary>
public sealed record EntityContext(Guid CompanyId, Guid? ProjectId);

/// <summary>
/// YORUM VE EK DOSYA HANGİ KAYDA BAĞLI — VE KULLANICI O KAYDI
/// GÖREBİLİYOR MU.
///
/// NEDEN VAR: yorum bileşeni `(varlık tipi + kayıt no)` ile her ekrana
/// takılıyor. Bu genel bir kapı; kapsam kontrolü olmadan bırakılırsa
/// göremediği bir çeke yorum yazan, göremediği bir hakedişin
/// tartışmasını okuyan kullanıcı olur. G3 paketinin tamamı bu tür
/// açıkları kapatmakla geçti — yeni kapı KAPSAMLI doğuyor.
///
/// DESTEKLENMEYEN TİP SESSİZCE GEÇMEZ: `null` dönüyor ve çağıran
/// reddediyor. Sessizce izin verilseydi, yeni bir modül yorum
/// bileşenini takar ve kapsamsız çalışırdı — üstelik çalışıyor
/// göründüğü için kimse fark etmezdi.
/// </summary>
public interface IEntityContextResolver
{
    /// <summary>
    /// Kaydın kapsam bağlamı. Kayıt yoksa ya da tip desteklenmiyorsa
    /// `null`.
    /// </summary>
    Task<EntityContext?> ResolveAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Kullanıcı bu kaydı görebiliyor mu — KAPSAM KONTROLÜ.
    ///
    /// LİSTE BAŞINA BİR KEZ ÇAĞRILIR: yorumlar zaten tek kaydın
    /// altında, her satır için ayrı çözümleme N+1 olurdu.
    /// </summary>
    Task<bool> CanAccessAsync(
        string entityType,
        Guid entityId,
        CurrentDataScopeSnapshot scope,
        CancellationToken cancellationToken);
}

public sealed class EntityContextResolver(AppDbContext db) : IEntityContextResolver
{
    /*
     * DESTEKLENEN VARLIK TİPLERİ VE GEREKÇELERİ.
     *
     * Liste, yorum bileşeninin takılabileceği ekranları belirliyor.
     * Yeni bir tip eklenmeden yorum takılırsa uç reddediyor ve bekçi
     * testi (CommentEntityTypeGuardTests) düşüyor — sessiz bir
     * kapsamsızlık değil, açık bir hata.
     */
    public static IReadOnlySet<string> SupportedTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WorkTask",       // Görevin kendi tartışması.
            "Project",        // Proje geneli.
            "Cheque",         // Çek: vade, ciro, karşılıksız takibi.
            "ProgressPayment",// Hakediş: kesinti ve metraj tartışması.
            "Offer",          // Teklif: revizyon gerekçeleri.
            "PurchaseRequest",// Satın alma talebi.
            "GoodsReceipt"    // Mal kabul: eksik/hasarlı teslim notu.
        };

    public async Task<EntityContext?> ResolveAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            return null;

        /*
         * TİP BAZLI ÇÖZÜMLEME — HER TİP AYRI TABLODA.
         *
         * Tek bir genel sorgu yazmanın yolu yok: `EntityType` +
         * `EntityId` mantıksal bir bağ, veritabanı düzeyinde yabancı
         * anahtar değil. Bu switch, o bağın TEK tanımı.
         */
        return entityType.Trim() switch
        {
            var t when t.Equals("WorkTask", StringComparison.OrdinalIgnoreCase) =>
                await db.WorkTasks.AsNoTracking()
                    .Where(x => x.Id == entityId)
                    .Select(x => new EntityContext(x.CompanyId, x.ProjectId))
                    .SingleOrDefaultAsync(cancellationToken),

            var t when t.Equals("Project", StringComparison.OrdinalIgnoreCase) =>
                await db.Projects.AsNoTracking()
                    .Where(x => x.Id == entityId)
                    .Select(x => new EntityContext(x.CompanyId, x.Id))
                    .SingleOrDefaultAsync(cancellationToken),

            var t when t.Equals("Cheque", StringComparison.OrdinalIgnoreCase) =>
                await db.Cheques.AsNoTracking()
                    .Where(x => x.Id == entityId)
                    .Select(x => new EntityContext(x.CompanyId, (Guid?)null))
                    .SingleOrDefaultAsync(cancellationToken),

            var t when t.Equals("ProgressPayment", StringComparison.OrdinalIgnoreCase) =>
                await db.ProgressPayments.AsNoTracking()
                    .Where(x => x.Id == entityId)
                    .Select(x => new EntityContext(x.CompanyId, x.ProjectId))
                    .SingleOrDefaultAsync(cancellationToken),

            var t when t.Equals("Offer", StringComparison.OrdinalIgnoreCase) =>
                await db.Offers.AsNoTracking()
                    .Where(x => x.Id == entityId)
                    .Select(x => new EntityContext(x.CompanyId, (Guid?)null))
                    .SingleOrDefaultAsync(cancellationToken),

            var t when t.Equals("PurchaseRequest", StringComparison.OrdinalIgnoreCase) =>
                await db.PurchaseRequests.AsNoTracking()
                    .Where(x => x.Id == entityId)
                    .Select(x => new EntityContext(x.CompanyId, x.ProjectId))
                    .SingleOrDefaultAsync(cancellationToken),

            var t when t.Equals("GoodsReceipt", StringComparison.OrdinalIgnoreCase) =>
                await db.GoodsReceipts.AsNoTracking()
                    .Where(x => x.Id == entityId)
                    .Select(x => new EntityContext(x.CompanyId, (Guid?)null))
                    .SingleOrDefaultAsync(cancellationToken),

            // DESTEKLENMEYEN TİP: sessizce geçmiyor.
            _ => null
        };
    }

    public async Task<bool> CanAccessAsync(
        string entityType,
        Guid entityId,
        CurrentDataScopeSnapshot scope,
        CancellationToken cancellationToken)
    {
        var baglam = await ResolveAsync(entityType, entityId, cancellationToken);

        if (baglam is null)
            return false;

        if (scope.HasGlobalAccess)
            return true;

        return scope.CompanyIds.Contains(baglam.CompanyId) ||
               (baglam.ProjectId is Guid proje && scope.ProjectIds.Contains(proje));
    }
}
