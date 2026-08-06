using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Projects;

/// <summary>
/// Bir icmal satırının gerçekleşen maliyeti.
///
/// <b>Ölçülmüş</b> ve <b>dağıtılmış</b> bilinçli olarak ayrı tutulur:
/// ölçülmüş, girişte bu poza açıkça etiketlenmiş maliyettir; dağıtılmış,
/// kısma yazılmış ama poza etiketlenmemiş maliyetin sözleşme tutarı
/// oranında bölünmüş payıdır. İkincisi bir TAHMİNDİR ve öyle
/// gösterilmelidir — toplayıp tek rakam sunmak, ölçümle tahmini
/// birbirine karıştırır ve sapan pozu görünmez kılar.
/// </summary>
public sealed record BoqItemCost(
    Guid BoqItemId,
    decimal MeasuredMaterial,
    decimal MeasuredLabor,
    decimal MeasuredOther,
    decimal AllocatedMaterial,
    decimal AllocatedLabor,
    decimal AllocatedOther)
{
    public decimal MeasuredTotal => MeasuredMaterial + MeasuredLabor + MeasuredOther;
    public decimal AllocatedTotal => AllocatedMaterial + AllocatedLabor + AllocatedOther;
    public decimal Total => MeasuredTotal + AllocatedTotal;

    /// <summary>Rakamın ne kadarı ölçüm — 1 ise tamamı etiketlenmiş.</summary>
    public decimal MeasuredRatio =>
        Total == 0 ? 0 : decimal.Round(MeasuredTotal / Total, 4);
}

public sealed record ProjectBoqCostSnapshot(
    Guid ProjectId,
    IReadOnlyDictionary<Guid, BoqItemCost> ByBoqItem,
    /// <summary>Hiçbir kısma bağlanmamış, dolayısıyla dağıtılamayan maliyet.</summary>
    decimal UnassignedCost,
    /// <summary>Ek ödeme dahil mi — yetkisiz kullanıcıda resmi bordro bazlı.</summary>
    bool IncludesExtraPayments,
    IReadOnlyList<string> Assumptions);

public interface IBoqItemCostService
{
    Task<ProjectBoqCostSnapshot> GetAsync(
        Guid projectId, CancellationToken cancellationToken = default);
}

/// <summary>
/// İcmal satırı bazında gerçekleşen maliyeti hesaplar.
///
/// Malzeme ve genel gider <see cref="ProjectCostTransaction"/>'dan,
/// işçilik <see cref="HrProjectLaborCost"/>'tan gelir — proje maliyet
/// analiziyle aynı kaynaklar, iki ayrı "doğru" rakam oluşmasın diye.
///
/// GİZLİLİK: ek ödeme dahil gerçek işçilik yalnızca yetkili kullanıcıda
/// hesaba girer; yetkisiz kullanıcı resmi bordro bazlı rakam görür ve
/// bu varsayımlarda yazar.
/// </summary>
public sealed class BoqItemCostService(
    AppDbContext db,
    IExtraPaymentVisibilityService extraPaymentVisibility) : IBoqItemCostService
{
    public async Task<ProjectBoqCostSnapshot> GetAsync(
        Guid projectId, CancellationToken cancellationToken = default)
    {
        var assumptions = new List<string>();

        var canSeeExtraPayments =
            await extraPaymentVisibility.CanViewExtraPaymentAsync(cancellationToken);

        if (!canSeeExtraPayments)
        {
            assumptions.Add(
                "İşçilik maliyeti resmi bordro üzerinden gösteriliyor; elden ödemeler " +
                "dahil değildir.");
        }

        // Yürürlükteki icmalin satırları: dağıtım tabanı bunlar.
        var boqItems = await db.ProjectBoqItems
            .AsNoTracking()
            .Where(x => x.ProjectBoq.ProjectId == projectId && x.ProjectBoq.IsCurrentRevision)
            .Select(x => new
            {
                x.Id,
                x.ProjectHakedisSectionId,
                x.TotalAmount
            })
            .ToListAsync(cancellationToken);

        var costs = boqItems.ToDictionary(
            x => x.Id,
            x => new MutableCost());

        // ---- Malzeme ve diğer: maliyet defteri ----
        var transactions = await db.ProjectCostTransactions
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => new
            {
                x.ProjectBoqItemId,
                x.ProjectHakedisSectionId,
                x.CostClass,
                x.Amount
            })
            .ToListAsync(cancellationToken);

        // ---- İşçilik: puantajdan gelen gerçekleşme ----
        var laborRows = await db.HrProjectLaborCosts
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => new
            {
                x.ProjectBoqItemId,
                x.ProjectHakedisSectionId,
                x.TotalLaborCost,
                x.CompensationCost
            })
            .ToListAsync(cancellationToken);

        var unassigned = 0m;

        // Kısım bazında dağıtılacak havuzlar.
        var sectionPools = new Dictionary<Guid, MutableCost>();

        void AddToPool(Guid? sectionId, ProjectCostClass costClass, decimal amount)
        {
            if (sectionId is not Guid section)
            {
                unassigned += amount;
                return;
            }

            if (!sectionPools.TryGetValue(section, out var pool))
            {
                pool = new MutableCost();
                sectionPools[section] = pool;
            }

            pool.Add(costClass, amount);
        }

        foreach (var row in transactions)
        {
            if (row.ProjectBoqItemId is Guid boqItemId
                && costs.TryGetValue(boqItemId, out var measured))
            {
                measured.Add(row.CostClass, row.Amount);
                continue;
            }

            AddToPool(row.ProjectHakedisSectionId, row.CostClass, row.Amount);
        }

        foreach (var row in laborRows)
        {
            // Ek ödeme (tazminat/elden) yalnızca yetkilide toplama girer.
            var amount = canSeeExtraPayments
                ? row.TotalLaborCost
                : row.TotalLaborCost - row.CompensationCost;

            if (amount == 0)
                continue;

            if (row.ProjectBoqItemId is Guid boqItemId
                && costs.TryGetValue(boqItemId, out var measured))
            {
                measured.Add(ProjectCostClass.Labor, amount);
                continue;
            }

            AddToPool(row.ProjectHakedisSectionId, ProjectCostClass.Labor, amount);
        }

        // ---- Kısım havuzlarını sözleşme tutarı oranında dağıt ----
        var allocatedSections = 0;

        foreach (var (sectionId, pool) in sectionPools)
        {
            var sectionItems = boqItems
                .Where(x => x.ProjectHakedisSectionId == sectionId)
                .ToList();

            var basis = sectionItems.Sum(x => x.TotalAmount);

            if (sectionItems.Count == 0 || basis <= 0)
            {
                // Dağıtılacak satır yoksa ya da sözleşme tutarı sıfırsa
                // pay verilemez; rakam uydurmak yerine dağıtılmamış
                // olarak bırakılır.
                unassigned += pool.Total;
                continue;
            }

            allocatedSections++;

            foreach (var item in sectionItems)
            {
                var share = item.TotalAmount / basis;
                var target = costs[item.Id];

                target.AllocatedMaterial += decimal.Round(pool.Material * share, 2);
                target.AllocatedLabor += decimal.Round(pool.Labor * share, 2);
                target.AllocatedOther += decimal.Round(pool.Other * share, 2);
            }
        }

        if (allocatedSections > 0)
        {
            assumptions.Add(
                $"{allocatedSections} kısımda poza etiketlenmemiş maliyet, sözleşme tutarı " +
                "oranında dağıtıldı. Dağıtılan tutar ölçüm değil tahmindir.");
        }

        if (unassigned != 0)
        {
            assumptions.Add(
                "Bir kısma veya icmal satırına bağlanmamış maliyet var; poz bazında " +
                "gösterilemiyor, yalnızca proje toplamında görünür.");
        }

        return new ProjectBoqCostSnapshot(
            projectId,
            costs.ToDictionary(
                x => x.Key,
                x => new BoqItemCost(
                    x.Key,
                    x.Value.Material,
                    x.Value.Labor,
                    x.Value.Other,
                    x.Value.AllocatedMaterial,
                    x.Value.AllocatedLabor,
                    x.Value.AllocatedOther)),
            decimal.Round(unassigned, 2),
            canSeeExtraPayments,
            assumptions);
    }

    /// <summary>Toplama sırasında kullanılan değiştirilebilir kova.</summary>
    private sealed class MutableCost
    {
        public decimal Material;
        public decimal Labor;
        public decimal Other;

        public decimal AllocatedMaterial;
        public decimal AllocatedLabor;
        public decimal AllocatedOther;

        public decimal Total => Material + Labor + Other;

        public void Add(ProjectCostClass costClass, decimal amount)
        {
            switch (costClass)
            {
                case ProjectCostClass.Material:
                    Material += amount;
                    break;

                case ProjectCostClass.Labor:
                case ProjectCostClass.SubcontractorLabor:
                    Labor += amount;
                    break;

                default:
                    Other += amount;
                    break;
            }
        }
    }
}
