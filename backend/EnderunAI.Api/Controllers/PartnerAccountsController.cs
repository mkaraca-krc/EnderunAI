using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Expenses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record SavePartnerAccountRequest(
    Guid CompanyId,
    string FullName,
    string? Title,
    string? Notes);

public sealed record SavePartnerEntryRequest(
    PartnerAccountEntryKind Kind,
    DateTime EntryDate,
    decimal Amount,
    string Description);

/// <summary>
/// Şahıs / ortak carisi — şirketten çıkan para ve faturasız gider
/// mahsubu.
///
/// TAMAMI ELDEN MASKESİNDE: her uç <c>expense.view</c> YANINDA
/// <c>extra_payment.view</c> istiyor. Bu defter faturasız kalemler
/// taşıyor; gider merkezini görebilen herkese açık olsaydı elden
/// tutarlar oradan okunurdu.
///
/// Muhasebe fişi ve kasa hareketi ÜRETMEZ: paranın şirketten çıkışı
/// kasa/banka modülünde zaten kayıtlı, ikisi birden yazsaydı aynı
/// para iki kez çıkardı.
/// </summary>
[ApiController]
[Authorize]
[Route("api/expenses/sahis-cari")]
public sealed class PartnerAccountsController(
    AppDbContext db,
    PartnerAccountService partners,
    IExtraPaymentVisibilityService extraPaymentVisibility) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.ExpenseView)]
    public async Task<IActionResult> List(
        [FromQuery] Guid companyId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        if (await MaskedAsync(cancellationToken))
            return Forbid();

        var balances = await partners.GetBalancesAsync(companyId, cancellationToken);

        return Ok(balances.Select(x => new
        {
            id = x.PartnerAccountId,
            fullName = x.FullName,
            title = x.Title,
            advanceTotal = x.AdvanceTotal,
            settlementTotal = x.SettlementTotal,
            repaymentTotal = x.RepaymentTotal,
            balance = x.Balance
        }));
    }

    [HttpGet("{id:guid}/hareketler")]
    [RequirePermission(PermissionCatalog.Keys.ExpenseView)]
    public async Task<IActionResult> Entries(
        Guid id,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        if (await MaskedAsync(cancellationToken))
            return Forbid();

        var query = db.PartnerAccountEntries
            .AsNoTracking()
            .Where(x => x.PartnerAccountId == id);

        // ARALIK: tarih Kind=Unspecified geldiğinde Npgsql
        // timestamptz karşılaştırmasını reddediyor; dönüşüm ortak
        // yardımcıdan.
        if (from is DateTime start)
        {
            var fromUtc = ExpenseEntryService.AsUtcDate(start);
            query = query.Where(x => x.EntryDate >= fromUtc);
        }

        if (to is DateTime end)
        {
            var toUtc = ExpenseEntryService.AsUtcDate(end);
            query = query.Where(x => x.EntryDate <= toUtc);
        }

        var rows = await query
            .OrderByDescending(x => x.EntryDate).ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                id = x.Id,
                kind = x.Kind.ToString(),
                entryDate = x.EntryDate,
                amount = x.Amount,
                description = x.Description,
                expenseEntryId = x.ExpenseEntryId,
                categoryName = x.ExpenseEntry != null
                    ? x.ExpenseEntry.ExpenseCategory.Name
                    : null
            })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.ExpenseManage)]
    public async Task<IActionResult> Create(
        [FromBody] SavePartnerAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (await MaskedAsync(cancellationToken))
            return Forbid();

        if (request.CompanyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        var fullName = (request.FullName ?? string.Empty).Trim();

        if (fullName.Length == 0)
            return BadRequest(new { message = "Ad soyad zorunludur." });

        var partner = new PartnerAccount
        {
            CompanyId = request.CompanyId,
            FullName = fullName,
            Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };

        db.PartnerAccounts.Add(partner);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id = partner.Id });
    }

    /// <summary>
    /// Elle hareket: avans (şirketten çıkan para) ya da geri ödeme.
    ///
    /// MAHSUP BURADAN GİRİLMEZ: mahsup her zaman bir gider kaydından
    /// doğar, yoksa gider merkezinde görünmeyen bir kalem bakiyeyi
    /// düşürür ve "para nereye gitti" sorusu cevapsız kalırdı.
    /// </summary>
    [HttpPost("{id:guid}/hareketler")]
    [RequirePermission(PermissionCatalog.Keys.ExpenseManage)]
    public async Task<IActionResult> AddEntry(
        Guid id,
        [FromBody] SavePartnerEntryRequest request,
        CancellationToken cancellationToken)
    {
        if (await MaskedAsync(cancellationToken))
            return Forbid();

        var exists = await db.PartnerAccounts
            .AnyAsync(x => x.Id == id, cancellationToken);

        if (!exists)
            return NotFound(new { message = "Şahıs carisi bulunamadı." });

        if (request.Kind == PartnerAccountEntryKind.ExpenseSettlement)
            return BadRequest(new
            {
                message = "Mahsup elle girilmez; faturasız gideri Gider Merkezi'nden " +
                          "kaydedin, mahsup kendiliğinden düşer."
            });

        if (request.Amount <= 0m)
            return BadRequest(new { message = "Tutar sıfırdan büyük olmalıdır." });

        // AÇIKLAMA ZORUNLU: bu defter resmî belgeye dayanmıyor.
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new { message = "Açıklama zorunludur." });

        var entry = new PartnerAccountEntry
        {
            PartnerAccountId = id,
            Kind = request.Kind,
            EntryDate = ExpenseEntryService.AsUtcDate(request.EntryDate),
            Amount = decimal.Round(request.Amount, 2),
            Description = request.Description.Trim()
        };

        db.PartnerAccountEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id = entry.Id });
    }

    [HttpDelete("hareketler/{entryId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ExpenseManage)]
    public async Task<IActionResult> DeleteEntry(
        Guid entryId, CancellationToken cancellationToken)
    {
        if (await MaskedAsync(cancellationToken))
            return Forbid();

        var entry = await db.PartnerAccountEntries
            .SingleOrDefaultAsync(x => x.Id == entryId, cancellationToken);

        if (entry is null)
            return NotFound(new { message = "Hareket bulunamadı." });

        // Mahsup kendi gider kaydına bağlı: buradan silinseydi gider
        // defterinde duran bir kalem bakiyede karşılıksız kalırdı.
        if (entry.Kind == PartnerAccountEntryKind.ExpenseSettlement)
            return BadRequest(new
            {
                message = "Mahsup buradan silinmez; gider kaydını silin ya da " +
                          "ödeme şeklini değiştirin."
            });

        db.PartnerAccountEntries.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id = entryId });
    }

    private async Task<bool> MaskedAsync(CancellationToken cancellationToken) =>
        !await extraPaymentVisibility.CanViewExtraPaymentAsync(cancellationToken);
}
