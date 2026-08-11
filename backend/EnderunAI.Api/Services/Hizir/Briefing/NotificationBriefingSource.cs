using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Hizir.Briefing;

/// <summary>
/// Bildirim motorunu brifinge bağlayan KÖPRÜ.
///
/// NEDEN VAR: brifing ile bildirim motoru aynı olayları anlatıyor.
/// İkisi kendi sorgusunu yazmaya devam etseydi çek vadesi iki ayrı
/// yerde iki ayrı eşikle hesaplanır ve zamanla ayrışırdı — brifing
/// "7 gün", bildirim "3 gün" der, kullanıcı hangisine inanacağını
/// bilemezdi. Artık hesap TEK YERDE (INotificationSource), brifing
/// sonucu OKUYOR.
///
/// REGRESYON-GÜVENLİ DEVİR: eski brifing kaynakları YERİNDE DURUYOR.
/// Bu köprü onların yanına ekleniyor; motorun kapsamadığı kalemler
/// (kritik stok, teklif geçerliliği, proje maliyet aşımı) eski
/// yollarından gelmeye devam ediyor. Eskiler bir çırpıda silinseydi
/// brifing, motorun henüz kapsamadığı her şeyi bir gecede kaybederdi.
///
/// ÇİFT GÖSTERİM ÖNLEMİ: motorun devraldığı türlerin eski kaynakları
/// devre dışı bırakıldı (bkz. Program.cs) — aynı çek hem eski
/// sorgudan hem motordan gelip iki kez görünmemeli.
///
/// YETKİ: brifing deseni korunuyor. Kaynağın kendi izni yok; her
/// bildirim kendi RequiredPermission'ını taşıyor ve burada
/// kullanıcının izinlerine göre süzülüyor.
/// </summary>
public sealed class NotificationBriefingSource(
    AppDbContext db,
    NotificationStore store) : IHizirBriefingSource
{
    public string Key => "bildirimler";

    /// <summary>
    /// Kaynak düzeyinde izin YOK: süzme bildirim bazında yapılıyor.
    /// Burada bir izin verilseydi, o izni olmayan kullanıcı kendi
    /// modülünün bildirimini de göremezdi.
    /// </summary>
    public string? RequiredPermission => null;

    public async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken)
    {
        var companyIds = context.Scope.VisibleCompanyIds.Count > 0
            ? context.Scope.VisibleCompanyIds.ToList()
            : await db.Companies
                .AsNoTracking()
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

        var items = new List<BriefingItem>();

        foreach (var companyId in companyIds)
        {
            var rows = await store.ListVisibleAsync(
                companyId,
                context.Permissions.ToList(),
                includeHandled: false,
                DateTime.UtcNow,
                cancellationToken);

            // BRİFİNG ÖZETTİR, LİSTE DEĞİL: kullanıcının önüne otuz
            // satır dökmek brifingi okunmaz hale getirir. Ayrıntı çanda
            // duruyor; burada en acil olanlar.
            items.AddRange(rows
                .Where(x => x.Severity != NotificationSeverity.Info)
                .Take(5)
                .Select(x => new BriefingItem(
                    x.Title,
                    x.Detail,
                    x.Severity switch
                    {
                        NotificationSeverity.Critical => BriefingSeverity.Critical,
                        NotificationSeverity.Warning => BriefingSeverity.Warning,
                        _ => BriefingSeverity.Info
                    },
                    x.TargetPath)));
        }

        return items;
    }
}
