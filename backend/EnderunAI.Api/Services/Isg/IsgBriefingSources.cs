using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hizir;
using EnderunAI.Api.Services.Hizir.Briefing;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Isg;

/// <summary>
/// Süresi dolan/dolacak İSG kayıtları: sağlık raporu, eğitim,
/// sertifika ve saha belgeleri.
///
/// Eşik <see cref="IsgValidityCalculator.WarningDays"/>'ten gelir —
/// panelle aynı gün sayısı. Veri yoksa madde üretilmez.
/// </summary>
public sealed class IsgExpiryBriefingSource(AppDbContext db) : IHizirBriefingSource
{
    public string Key => "isg_gecerlilik";
    public string? RequiredPermission => PermissionCatalog.Keys.IsgView;

    public async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var horizon = today.AddDays(IsgValidityCalculator.WarningDays);

        var items = new List<BriefingItem>();

        // --- Sağlık raporu ---
        var healthExpired = await db.IsgHealthReports.AsNoTracking()
            .CountAsync(x => x.ValidUntil != null && x.ValidUntil < today,
                cancellationToken);

        var healthSoon = await db.IsgHealthReports.AsNoTracking()
            .CountAsync(x => x.ValidUntil != null &&
                             x.ValidUntil >= today && x.ValidUntil <= horizon,
                cancellationToken);

        if (healthExpired > 0)
        {
            items.Add(new BriefingItem(
                $"{healthExpired} personelin sağlık raporunun süresi doldu",
                "Süresi dolmuş raporla çalıştırma yasal sorumluluk doğurur; " +
                "OSGB'den yenileme isteyin.",
                BriefingSeverity.Critical, "/isg/personel"));
        }

        if (healthSoon > 0)
        {
            items.Add(new BriefingItem(
                $"{healthSoon} personelin sağlık raporu {IsgValidityCalculator.WarningDays} " +
                "gün içinde doluyor",
                "OSGB'den yenileme randevusu isteyin.",
                BriefingSeverity.Warning, "/isg/personel"));
        }

        // --- Eğitim ---
        var trainingExpired = await db.IsgTrainings.AsNoTracking()
            .CountAsync(x => x.ValidUntil != null && x.ValidUntil < today,
                cancellationToken);

        if (trainingExpired > 0)
        {
            items.Add(new BriefingItem(
                $"{trainingExpired} personelin İSG eğitimi güncelliğini yitirdi",
                "Yenileme eğitimi planlayın.",
                BriefingSeverity.Critical, "/isg/personel"));
        }

        // --- Sertifika ---
        var certificateExpired = await db.IsgCertificates.AsNoTracking()
            .CountAsync(x => x.ExpiryDate != null && x.ExpiryDate < today,
                cancellationToken);

        var certificateSoon = await db.IsgCertificates.AsNoTracking()
            .CountAsync(x => x.ExpiryDate != null &&
                             x.ExpiryDate >= today && x.ExpiryDate <= horizon,
                cancellationToken);

        if (certificateExpired > 0)
        {
            items.Add(new BriefingItem(
                $"{certificateExpired} yetki belgesinin süresi doldu",
                "Süresi dolmuş belgeyle iş yaptırılmamalı.",
                BriefingSeverity.Critical, "/isg/personel"));
        }

        if (certificateSoon > 0)
        {
            items.Add(new BriefingItem(
                $"{certificateSoon} yetki belgesi {IsgValidityCalculator.WarningDays} " +
                "gün içinde doluyor",
                null, BriefingSeverity.Warning, "/isg/personel"));
        }

        // --- Saha belgeleri ---
        var documentExpired = await db.IsgSiteDocuments.AsNoTracking()
            .CountAsync(x => x.ValidUntil != null && x.ValidUntil < today,
                cancellationToken);

        if (documentExpired > 0)
        {
            items.Add(new BriefingItem(
                $"{documentExpired} saha İSG belgesinin süresi doldu",
                "Süresi dolmuş risk değerlendirmesi denetimde belge yokluğu sayılır.",
                BriefingSeverity.Critical, "/isg/belgeler"));
        }

        // --- OSGB sözleşmesi ---
        var contractExpiring = await db.IsgOsgbContracts.AsNoTracking()
            .CountAsync(x => x.EndDate != null &&
                             x.EndDate >= today && x.EndDate <= horizon,
                cancellationToken);

        if (contractExpiring > 0)
        {
            items.Add(new BriefingItem(
                $"{contractExpiring} OSGB sözleşmesi {IsgValidityCalculator.WarningDays} " +
                "gün içinde bitiyor",
                "Yenilenmezse İSG hizmeti kesintiye uğrar.",
                BriefingSeverity.Warning, "/isg/osgb"));
        }

        return items;
    }
}

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
