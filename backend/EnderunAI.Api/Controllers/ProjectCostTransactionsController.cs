using EnderunAI.Api.Contracts.ProjectSites;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}")]
public sealed class ProjectCostTransactionsController(AppDbContext db) : ControllerBase
{
    [HttpGet("cost-transactions")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsView)]
    public async Task<IActionResult> GetAll(
        Guid projectId,
        [FromQuery] Guid? siteId,
        CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects.AsNoTracking()
            .AnyAsync(x => x.Id == projectId, cancellationToken);

        if (!projectExists)
            return NotFound(new { message = "Proje bulunamadı." });

        var query = db.ProjectCostTransactions
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId);

        if (siteId.HasValue)
            query = query.Where(x => x.ProjectSiteId == siteId.Value);

        var items = await query
            .OrderByDescending(x => x.CostDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.ProjectId,
                x.ProjectSiteId,
                SiteCode = x.ProjectSite != null ? x.ProjectSite.Code : null,
                SiteName = x.ProjectSite != null ? x.ProjectSite.Name : null,
                x.CostType,
                x.CostDate,
                x.Amount,
                x.Description
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost("cost-transactions")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsCreate)]
    public async Task<IActionResult> Create(
        Guid projectId,
        CreateProjectCostTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects.AsNoTracking()
            .AnyAsync(x => x.Id == projectId, cancellationToken);

        if (!projectExists)
            return NotFound(new { message = "Proje bulunamadı." });

        if (request.Amount <= 0)
            return BadRequest(new { message = "Tutar sıfırdan büyük olmalıdır." });

        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new { message = "Açıklama zorunludur." });

        if (request.ProjectSiteId.HasValue)
        {
            var siteBelongsToProject = await db.ProjectSites.AsNoTracking().AnyAsync(
                x => x.Id == request.ProjectSiteId.Value && x.ProjectId == projectId,
                cancellationToken);

            if (!siteBelongsToProject)
            {
                return BadRequest(new
                {
                    message = "Seçilen şantiye bu projeye ait değil."
                });
            }
        }

        var item = new ProjectCostTransaction
        {
            ProjectId = projectId,
            ProjectSiteId = request.ProjectSiteId,
            CostType = request.CostType,
            CostDate = DateTime.SpecifyKind(request.CostDate.Date, DateTimeKind.Utc),
            Amount = request.Amount,
            Description = request.Description.Trim()
        };

        db.ProjectCostTransactions.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Maliyet kaydı oluşturuldu.",
            item.Id
        });
    }

    [HttpGet("cost-breakdown")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsView)]
    public async Task<IActionResult> GetBreakdown(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects.AsNoTracking()
            .AnyAsync(x => x.Id == projectId, cancellationToken);

        if (!projectExists)
            return NotFound(new { message = "Proje bulunamadı." });

        var transactions = await db.ProjectCostTransactions
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => new { x.ProjectSiteId, x.Amount })
            .ToListAsync(cancellationToken);

        var sites = await db.ProjectSites
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.Code)
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToListAsync(cancellationToken);

        var siteBreakdown = sites.Select(site => new
        {
            site.Id,
            site.Code,
            site.Name,
            Amount = transactions
                .Where(x => x.ProjectSiteId == site.Id)
                .Sum(x => x.Amount)
        }).ToList();

        var sharedCost = transactions
            .Where(x => x.ProjectSiteId == null)
            .Sum(x => x.Amount);

        var projectTotal = transactions.Sum(x => x.Amount);

        return Ok(new
        {
            projectId,
            sites = siteBreakdown,
            sharedCost,
            projectTotal
        });
    }

    /// <summary>
    /// Proje maliyeti ↔ muhasebe mutabakatı. Proje maliyet defterindeki
    /// tutarlar ile bu projeye yazılmış muhasebe maliyet/gider hesabı
    /// (7'li grup) tutarlarını karşılaştırır; iki tarafın da aynı rakamı
    /// göstermesi gerekir. Muhasebeye bağlanmamış maliyet kayıtları ve
    /// proje maliyetine yansımamış muhasebe satırları ayrı ayrı listelenir.
    /// </summary>
    [HttpGet("cost-reconciliation")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsView)]
    public async Task<IActionResult> GetCostReconciliation(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects.AsNoTracking()
            .AnyAsync(x => x.Id == projectId, cancellationToken);

        if (!projectExists)
            return NotFound(new { message = "Proje bulunamadı." });

        var costRows = await db.ProjectCostTransactions
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => new
            {
                x.Id,
                x.CostDate,
                x.Amount,
                x.Description,
                x.ReferenceType,
                x.AccountingVoucherLineId
            })
            .ToListAsync(cancellationToken);

        // Muhasebe tarafı: bu projeye yazılmış, kesinleşmiş fişlerin
        // 7'li (maliyet/gider) hesap satırları — net borç.
        var accountingRows = await db.AccountingVoucherLines
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                !x.AccountingVoucher.IsDeleted &&
                x.AccountingVoucher.Status == AccountingVoucherStatus.Posted &&
                x.ProjectId == projectId &&
                x.AccountingAccount.Code.StartsWith("7"))
            .Select(x => new
            {
                x.Id,
                x.AccountingVoucher.VoucherNumber,
                x.AccountingVoucher.VoucherDate,
                AccountCode = x.AccountingAccount.Code,
                AccountName = x.AccountingAccount.Name,
                x.Description,
                Amount = x.DebitAmountLocal - x.CreditAmountLocal
            })
            .ToListAsync(cancellationToken);

        var linkedLineIds = costRows
            .Where(x => x.AccountingVoucherLineId.HasValue)
            .Select(x => x.AccountingVoucherLineId!.Value)
            .ToHashSet();

        var projectCostTotal = decimal.Round(costRows.Sum(x => x.Amount), 2);
        var accountingTotal = decimal.Round(accountingRows.Sum(x => x.Amount), 2);

        var unlinkedCosts = costRows
            .Where(x => x.AccountingVoucherLineId is null)
            .OrderByDescending(x => x.CostDate)
            .Select(x => new
            {
                x.Id,
                x.CostDate,
                x.Amount,
                x.Description,
                x.ReferenceType
            })
            .ToList();

        var unlinkedAccountingLines = accountingRows
            .Where(x => !linkedLineIds.Contains(x.Id))
            .OrderByDescending(x => x.VoucherDate)
            .ToList();

        return Ok(new
        {
            projectId,
            projectCostTotal,
            accountingTotal,
            difference = decimal.Round(projectCostTotal - accountingTotal, 2),
            isReconciled = decimal.Round(projectCostTotal - accountingTotal, 2) == 0m,
            linkedCostCount = costRows.Count - unlinkedCosts.Count,
            unlinkedCostTotal = decimal.Round(unlinkedCosts.Sum(x => x.Amount), 2),
            unlinkedAccountingTotal = decimal.Round(unlinkedAccountingLines.Sum(x => x.Amount), 2),
            unlinkedCosts,
            unlinkedAccountingLines
        });
    }
}
