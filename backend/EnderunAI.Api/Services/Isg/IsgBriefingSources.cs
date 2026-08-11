using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hizir;
using EnderunAI.Api.Services.Hizir.Briefing;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Isg;

// İSG GEÇERLİLİK KAYNAĞI KALDIRILDI (bildirim motoru devraldı).
//
// Aynı tabloları aynı eşikle ikinci kez saymak, brifingde çift
// gösterim demekti: bir kez toplu ("3 personelin raporu doldu"),
// bir kez de motordan kişi kişi. Hesap artık
// DocumentExpiryNotificationSource'ta ve eşik yine
// IsgValidityCalculator.WarningDays.
//
// DAVRANIŞ DEĞİŞİKLİĞİ: brifingde toplu sayım yerine en acil
// kalemler kişi bazında görünüyor (en fazla beş satır). Ayrıntının
// tamamı bildirim çanında.

/// <summary>
/// Açık kaza kayıtları ve SGK'ya bildirilmemiş iş kazaları.
///
/// Kaza defteri dar izinle korunduğu için brifing kaynağı da aynı izni
/// ister; yetkisi olmayan kullanıcıda kaynak hiç çalıştırılmaz.
/// </summary>
public sealed class IsgIncidentBriefingSource(AppDbContext db) : IHizirBriefingSource
{
    public string Key => "isg_kaza";
    public string? RequiredPermission => PermissionCatalog.Keys.IsgIncidentView;

    public async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken)
    {
        var items = new List<BriefingItem>();

        var openIncidents = await db.IsgIncidents.AsNoTracking()
            .Where(x => x.Status != IsgIncidentStatus.Closed)
            .Select(x => new { x.IncidentType, x.IncidentDateTime, x.SgkNotified })
            .ToListAsync(cancellationToken);

        if (openIncidents.Count > 0)
        {
            items.Add(new BriefingItem(
                $"{openIncidents.Count} kaza/ramak kala kaydı açık",
                "Kök neden ve alınan önlem tamamlanmadan kayıt kapatılmıyor.",
                BriefingSeverity.Warning, "/isg/kazalar"));
        }

        // SGK bildirim gecikmesi yalnızca gerçek kazalarda anlamlı;
        // kural servisin kendisinde tek yerde duruyor.
        var overdue = await db.IsgIncidents.AsNoTracking()
            .Where(x => x.IncidentType == IsgIncidentType.Accident && !x.SgkNotified)
            .ToListAsync(cancellationToken);

        var overdueCount = overdue.Count(IsgIncidentService.IsNotificationOverdue);

        if (overdueCount > 0)
        {
            items.Add(new BriefingItem(
                $"{overdueCount} iş kazası SGK'ya bildirilmemiş ve yasal süre geçti",
                "İş kazası üç iş günü içinde bildirilmek zorunda.",
                BriefingSeverity.Critical, "/isg/kazalar"));
        }

        return items;
    }
}
