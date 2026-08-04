using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record DeductionAccountMappingRequest(
    int DeductionType,
    Guid? AccountingAccountId,
    string? Notes);

public sealed record ReplaceDeductionAccountMappingsRequest(
    IReadOnlyCollection<DeductionAccountMappingRequest> Mappings);

/// <summary>
/// Hakediş kesinti türlerinin muhasebe hesapları. Kesin teminat,
/// barter, yemek, konaklama ve İSG kesintileri farklı hesaplara borç
/// yazılır; eşleme buradan yapılır.
///
/// Eşleme zorunlu değil: boş bırakılan tür, finans ayarındaki genel
/// kesinti hesabına düşer.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hakedis-deduction-accounts")]
public sealed class HakedisDeductionAccountsController(AppDbContext db) : ControllerBase
{
    /// <summary>Kesinti türleri ve varsa eşlendikleri hesaplar.</summary>
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.AccountingView)]
    public async Task<IActionResult> Get(
        [FromQuery] Guid companyId, CancellationToken cancellationToken)
    {
        var mappings = await db.HakedisDeductionAccountMappings
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.DeductionType,
                x.AccountingAccountId,
                AccountCode = x.AccountingAccount.Code,
                AccountName = x.AccountingAccount.Name,
                x.Notes
            })
            .ToListAsync(cancellationToken);

        var result = Enum.GetValues<HakedisDeductionType>()
            .Select(type =>
            {
                var mapping = mappings.SingleOrDefault(
                    x => x.DeductionType == (int)type);

                return new
                {
                    deductionType = (int)type,
                    name = DeductionTypeName(type),
                    accountingAccountId = mapping?.AccountingAccountId,
                    accountCode = mapping?.AccountCode,
                    accountName = mapping?.AccountName,
                    notes = mapping?.Notes
                };
            })
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Eşlemeleri topluca kaydeder. Hesabı boşaltılan tür eşlemesi
    /// silinir ve genel kesinti hesabına düşer.
    /// </summary>
    [HttpPut]
    [RequirePermission(PermissionCatalog.Keys.AccountingManage)]
    public async Task<IActionResult> Replace(
        [FromQuery] Guid companyId,
        ReplaceDeductionAccountMappingsRequest request,
        CancellationToken cancellationToken)
    {
        var companyExists = await db.Companies
            .AnyAsync(x => x.Id == companyId, cancellationToken);

        if (!companyExists)
            return NotFound(new { message = "Şirket bulunamadı." });

        var accountIds = request.Mappings
            .Where(x => x.AccountingAccountId is not null)
            .Select(x => x.AccountingAccountId!.Value)
            .Distinct()
            .ToList();

        if (accountIds.Count > 0)
        {
            var validCount = await db.AccountingAccounts
                .CountAsync(x => accountIds.Contains(x.Id) &&
                                 x.CompanyId == companyId,
                    cancellationToken);

            if (validCount != accountIds.Count)
            {
                return BadRequest(new
                {
                    message = "Seçilen hesaplardan biri bu şirketin hesap planında yok."
                });
            }
        }

        var existing = await db.HakedisDeductionAccountMappings
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        foreach (var item in request.Mappings)
        {
            var current = existing.SingleOrDefault(
                x => x.DeductionType == item.DeductionType);

            if (item.AccountingAccountId is not Guid accountId)
            {
                if (current is not null)
                    db.HakedisDeductionAccountMappings.Remove(current);

                continue;
            }

            if (current is null)
            {
                db.HakedisDeductionAccountMappings.Add(
                    new HakedisDeductionAccountMapping
                    {
                        CompanyId = companyId,
                        DeductionType = item.DeductionType,
                        AccountingAccountId = accountId,
                        Notes = item.Notes?.Trim()
                    });
                continue;
            }

            current.AccountingAccountId = accountId;
            current.Notes = item.Notes?.Trim();
            current.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Kesinti hesapları kaydedildi." });
    }

    private static string DeductionTypeName(HakedisDeductionType type) => type switch
    {
        HakedisDeductionType.PerformanceBond => "Kesin teminat",
        HakedisDeductionType.AllRiskInsurance => "All-risk sigorta",
        HakedisDeductionType.MaterialDeduction => "Malzeme kesintisi",
        HakedisDeductionType.Barter => "Barter",
        HakedisDeductionType.Meal => "Yemek",
        HakedisDeductionType.Accommodation => "Konaklama / kamp",
        HakedisDeductionType.OhsPenalty => "İSG ceza",
        HakedisDeductionType.OhsContribution => "İSG katılımı",
        HakedisDeductionType.Other => "Diğer kesintiler",
        _ => type.ToString()
    };
}
