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

        await UpsertAsync(duty, TravelReference, "Yol gideri",
            duty.TravelCost, cancellationToken);

        await UpsertAsync(duty, AccommodationReference, "Konaklama gideri",
            duty.AccommodationCost, cancellationToken);

        await UpsertAsync(duty, AllowanceReference, "Harcırah",
            duty.TotalAllowance, cancellationToken);
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

        var description =
            $"{label} — {DutyLabel(duty.DutyType)} ({duty.StartDate:dd.MM.yyyy}" +
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
