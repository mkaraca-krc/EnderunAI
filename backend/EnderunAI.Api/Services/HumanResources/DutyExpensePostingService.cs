using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.HumanResources;

/// <summary>
/// Görev masrafını hedef projenin maliyet defterine yansıtır.
///
/// ÜÇ KATEGORİ AYRI SATIR: yol, konaklama ve harcırah tek toplama
/// çökertilmiyor. Gider merkezi tarafı "şantiyeye ne kadar yol / ne
/// kadar konaklama / ne kadar harcırah" diye soracak; kırılım
/// sonradan ayrıştırılamaz.
///
/// MÜKERRER YANSIMA YOK: her satır ReferenceType + ReferenceId ile
/// kendi kaynağına bağlı. Yeniden yansıtma aynı satırı GÜNCELLER,
/// ikincisini açmaz. Göreve ayrı bir "yansıtıldı" bayrağı eklenmedi —
/// defterin kendisi kaynağı taşıyor, bayrak ile defter arasında
/// tutarsızlık doğamaz.
///
/// SORGULANABİLİR KANCA: ileride kurulacak gider merkezi bu
/// satırları ReferenceType öneki ve tarih aralığıyla OKUYACAK,
/// yeniden defterlemeyecek. Aynı masrafın iki kez sayılmaması bu
/// ayrımla korunuyor.
/// </summary>
public sealed class DutyExpensePostingService(AppDbContext db)
{
    /// <summary>Kancanın öneki: tüm görev masrafı satırları bununla başlar.</summary>
    public const string ReferencePrefix = "PersonnelDuty";

    public const string TravelReference = ReferencePrefix + "Travel";
    public const string AccommodationReference = ReferencePrefix + "Accommodation";
    public const string AllowanceReference = ReferencePrefix + "Allowance";

    /// <summary>
    /// Onaylı görevin masrafını yansıtır. Onaysız görev maliyet
    /// üretmez; talep aşamasındaki bir görev projenin kârını
    /// değiştirmemeli.
    /// </summary>
    public async Task PostAsync(PersonnelDuty duty, CancellationToken cancellationToken)
    {
        if (duty.Status != PersonnelDutyStatus.Approved)
            return;

        var target = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == duty.TargetProjectId)
            .Select(x => new { x.Name, x.SurveyOutcome })
            .SingleOrDefaultAsync(cancellationToken);

        var lostBidLabel = target is { SurveyOutcome: ProjectSurveyOutcome.Lost }
            ? $"{target.Name} — Proje Keşfi"
            : null;

        await UpsertAsync(duty, TravelReference, "Yol gideri",
            duty.TravelCost, lostBidLabel, cancellationToken);

        await UpsertAsync(duty, AccommodationReference, "Konaklama gideri",
            duty.AccommodationCost, lostBidLabel, cancellationToken);

        await UpsertAsync(duty, AllowanceReference, "Harcırah",
            duty.TotalAllowance, lostBidLabel, cancellationToken);
    }

    /// <summary>
    /// Bir projenin keşif sonucu değişince o projeye bağlı onaylı
    /// görevlerin satırlarını yeniden yazar.
    ///
    /// Açıklamayı burada değil TEK YAZICIDA ürettiği için defterle
    /// görev arasında kayma olmaz: masraf sonradan düzeltilse bile
    /// satır yine sonucun anlattığı adla yazılır. Tutarlara
    /// DOKUNMAZ — kaybetmek harcanan parayı değiştirmez.
    /// </summary>
    public async Task RepostForProjectAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var duties = await db.PersonnelDuties
            .Where(x => x.TargetProjectId == projectId &&
                        x.Status == PersonnelDutyStatus.Approved)
            .ToListAsync(cancellationToken);

        foreach (var duty in duties)
            await PostAsync(duty, cancellationToken);
    }

    /// <summary>
    /// Tek kategorinin satırını yazar ya da günceller. Tutar sıfıra
    /// düştüyse satır SİLİNİR: sıfır tutarlı bir maliyet satırı
    /// defterde gürültüdür ve "yansıtıldı ama sıfır" ile "hiç
    /// yansıtılmadı" ayrımını bulanıklaştırır.
    /// </summary>
    private async Task UpsertAsync(
        PersonnelDuty duty,
        string referenceType,
        string label,
        decimal amount,
        string? lostBidLabel,
        CancellationToken cancellationToken)
    {
        var existing = await db.ProjectCostTransactions
            .SingleOrDefaultAsync(
                x => x.ReferenceType == referenceType && x.ReferenceId == duty.Id,
                cancellationToken);

        if (amount <= 0m)
        {
            if (existing is not null)
                db.ProjectCostTransactions.Remove(existing);

            return;
        }

        // Kaybedilen teklifte satır, kazanılmış bir işin maliyeti gibi
        // değil, "proje adı — Proje Keşfi" gideri olarak okunur.
        // Tutar aynı kalır: kaybetmek harcanan parayı geri getirmez.
        var description = lostBidLabel is null
            ? $"{label} — {DutyLabel(duty.DutyType)} ({duty.StartDate:dd.MM.yyyy}" +
              $"–{duty.EndDate:dd.MM.yyyy})"
            : $"{label} — {lostBidLabel} ({duty.StartDate:dd.MM.yyyy}" +
              $"–{duty.EndDate:dd.MM.yyyy})";

        if (existing is null)
        {
            db.ProjectCostTransactions.Add(new ProjectCostTransaction
            {
                ProjectId = duty.TargetProjectId,
                ProjectSiteId = duty.TargetProjectSiteId,
                // Görev masrafı imalata değil işin yürütülmesine ait:
                // genel gider sınıfında durur ve hedef projede olduğu
                // için kârlılığa düşer.
                CostType = ProjectCostType.Overhead,
                CostClass = ProjectCostClass.Overhead,
                CostDate = duty.StartDate,
                Amount = amount,
                Description = description,
                ReferenceType = referenceType,
                ReferenceId = duty.Id
            });

            return;
        }

        // Görev hedefi ya da tarihi düzeltilmiş olabilir; satır
        // yeniden yazılır, ikincisi açılmaz.
        existing.ProjectId = duty.TargetProjectId;
        existing.ProjectSiteId = duty.TargetProjectSiteId;
        existing.CostDate = duty.StartDate;
        existing.Amount = amount;
        existing.Description = description;
    }

    private static string DutyLabel(PersonnelDutyType type) => type switch
    {
        PersonnelDutyType.Work => "çalışma görevlendirmesi",
        PersonnelDutyType.Survey => "keşif görevi",
        PersonnelDutyType.Visit => "ziyaret/denetim",
        _ => "görevlendirme"
    };
}
