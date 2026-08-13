using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

// Bu controller önceden tüm alanlarda sabit 0 / boş dizi döndürüyordu.
// Aşağıdaki her endpoint artık sistemde GERÇEKTEN var olan verilerle
// (Project, ProgressPayment, ProjectCostTransaction, CurrentAccount)
// besleniyor. Kasa/banka hareketi (CashAccount/CashTransaction) ve
// tedarikçi fatura/tahsilat defteri henüz uygulamaya bağlı değil —
// bu alanlar sıfır göstermek yerine "available:false" + açıklayıcı
// mesajla işaretleniyor; frontend bu bayrağa göre dürüst bir etiket
// gösteriyor.
[ApiController]
[Authorize]
[Route("api/finance")]
public sealed class FinanceDashboardController(
    AppDbContext db,
    Services.Projects.IProjectRealizedCostReader realizedCosts,
    IExtraPaymentVisibilityService extraPaymentVisibility) : ControllerBase
{
    private static readonly ProgressPaymentStatus[] RealizedProgressPaymentStatuses =
    [
        ProgressPaymentStatus.Approved,
        ProgressPaymentStatus.Posted
    ];

    private const string CashLedgerNotice =
        "Kasa/banka hareket modülü henüz uygulamaya bağlı değil, bu tutar hesaplanamıyor.";

    private const string CurrentAccountLedgerNotice =
        "Cari hareket/bakiye defteri (fatura ve tahsilat modülü) henüz devrede değil, bu tutar hesaplanamıyor.";

    private const string SupplierLedgerNotice =
        "Tedarikçi bakiyesi için fatura ve ödeme kayıtları henüz uygulamaya bağlı değil.";

    [HttpGet("dashboard")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    {
        var activeProjectCount = await db.Projects
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status == ProjectStatus.Active)
            .CountAsync(cancellationToken);

        var totalContractAmount = await db.Projects
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ContractAmount != null)
            .SumAsync(x => x.ContractAmount!.Value, cancellationToken);

        var realizedPayments = db.ProgressPayments
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                RealizedProgressPaymentStatuses.Contains(x.Status));

        var progressPaymentCount = await realizedPayments.CountAsync(cancellationToken);
        var totalProgressPaymentAmount = await realizedPayments
            .SumAsync(x => x.CurrentAmount, cancellationToken);
        var totalPriceDifferenceAmount = await realizedPayments
            .SumAsync(x => x.PriceDifferenceAmount, cancellationToken);
        var totalDeductionAmount = await realizedPayments
            .SumAsync(x => x.TotalDeductionAmount, cancellationToken);
        var totalNetPayableAmount = await realizedPayments
            .SumAsync(x => x.NetPayableAmount, cancellationToken);

        return Ok(new
        {
            totalContractAmount,
            totalProgressPaymentAmount,
            totalPriceDifferenceAmount,
            totalDeductionAmount,
            totalNetPayableAmount,
            activeProjectCount,
            progressPaymentCount
        });
    }

    [HttpGet("financial-dashboard")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> FinancialDashboard(
        [FromQuery] Guid? companyId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var rangeStart = UtcDate(startDate ?? new DateTime(now.Year, 1, 1));
        var rangeEndExclusive = UtcDate(endDate ?? now).AddDays(1);

        var paymentQuery = db.ProgressPayments
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                RealizedProgressPaymentStatuses.Contains(x.Status) &&
                x.ProgressPaymentDate >= rangeStart &&
                x.ProgressPaymentDate < rangeEndExclusive);

        if (companyId.HasValue)
            paymentQuery = paymentQuery.Where(x => x.CompanyId == companyId.Value);

        var periodRevenue = await paymentQuery.SumAsync(x => x.CurrentAmount, cancellationToken);

        // DÖNEM GİDERİ ORTAK OKUYUCUDAN: maliyet defteri + projelere
        // yazılmış elle gider kayıtları. Pano kendi sorgusunu tutsaydı,
        // proje maliyet analizi kirayı sayarken pano saymaz ve iki ekran
        // aynı dönem için farklı gider gösterirdi.
        //
        // Elden kalemler yalnız yetkili kullanıcının rakamına girer.
        var periodExpense = await realizedCosts.ReadProjectCostTotalAsync(
            companyId,
            rangeStart,
            rangeEndExclusive,
            await extraPaymentVisibility.CanViewExtraPaymentAsync(cancellationToken),
            cancellationToken);
        var netResult = decimal.Round(periodRevenue - periodExpense, 2);

        // Kasa/banka hareketi ve cari tahsilat/ödeme defteri koda hiç
        // bağlı değil (CashAccount/CashTransaction tabloları migration'da
        // var ama karşılığında EF model'i yok, CurrentAccount'ta hareket
        // tablosu hiç yok) — bu alanlar 0 göstermek yerine açıkça
        // "hesaplanamıyor" olarak işaretleniyor.
        var unavailableFields = new[]
        {
            "cashBalance",
            "bankBalance",
            "totalLiquidAssets",
            "receivables",
            "payables",
            "todayCollections",
            "todayPayments",
            "cashInflow",
            "cashOutflow",
            "netCashChange"
        };

        return Ok(new
        {
            companyId = companyId ?? Guid.Empty,
            startDate = rangeStart,
            endDate = endDate ?? now,
            generatedAtUtc = now,
            summary = new
            {
                cashBalance = 0m,
                bankBalance = 0m,
                totalLiquidAssets = 0m,
                receivables = 0m,
                payables = 0m,
                todayCollections = 0m,
                todayPayments = 0m,
                periodRevenue,
                periodExpense,
                netProfit = netResult > 0 ? netResult : 0m,
                netLoss = netResult < 0 ? -netResult : 0m,
                cashInflow = 0m,
                cashOutflow = 0m,
                netCashChange = 0m
            },
            unavailableFields,
            unavailableFieldsMessage = CashLedgerNotice
        });
    }

    [HttpGet("cari-summary")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> CurrentAccountSummary(CancellationToken cancellationToken)
    {
        var accountCount = await db.CurrentAccounts
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .CountAsync(cancellationToken);

        return Ok(new
        {
            accountCount,
            totalReceivable = 0m,
            totalPayable = 0m,
            netBalance = 0m,
            balancesAvailable = false,
            message = CurrentAccountLedgerNotice
        });
    }

    [HttpGet("projects-summary")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> ProjectsSummary(CancellationToken cancellationToken)
    {
        var projects = await db.Projects
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status == ProjectStatus.Active)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                ContractAmount = x.ContractAmount ?? 0m
            })
            .ToListAsync(cancellationToken);

        var projectIds = projects.Select(x => x.Id).ToArray();

        var paymentTotals = await db.ProgressPayments
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                projectIds.Contains(x.ProjectId) &&
                RealizedProgressPaymentStatuses.Contains(x.Status))
            .GroupBy(x => x.ProjectId)
            .Select(g => new
            {
                ProjectId = g.Key,
                ProgressPaymentAmount = g.Sum(x => x.CurrentAmount),
                NetPayableAmount = g.Sum(x => x.NetPayableAmount)
            })
            .ToDictionaryAsync(x => x.ProjectId, cancellationToken);

        var result = projects.Select(project =>
        {
            paymentTotals.TryGetValue(project.Id, out var totals);

            var progressPaymentAmount = totals?.ProgressPaymentAmount ?? 0m;
            var netPayableAmount = totals?.NetPayableAmount ?? 0m;

            return new
            {
                projectId = project.Id,
                projectCode = project.Code,
                projectName = project.Name,
                contractAmount = project.ContractAmount,
                progressPaymentAmount,
                netPayableAmount,
                remainingAmount = decimal.Round(
                    project.ContractAmount - progressPaymentAmount,
                    2)
            };
        });

        return Ok(result);
    }

    [HttpGet("cash-flow")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public IActionResult CashFlow()
    {
        return Ok(new
        {
            totalIncome = 0m,
            totalExpense = 0m,
            netCash = 0m,
            available = false,
            message = CashLedgerNotice
        });
    }

    [HttpGet("suppliers-summary")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public IActionResult SuppliersSummary()
    {
        return Ok(new
        {
            items = Array.Empty<object>(),
            available = false,
            message = SupplierLedgerNotice
        });
    }

    private static DateTime UtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}
