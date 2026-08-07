using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Subcontractors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>Faturalı ödeme veya resmî avans.</summary>
public sealed record SaveSubcontractorLedgerEntryRequest(
    Guid SubcontractorContractId,
    Guid? SubcontractorProgressPaymentId,
    int Kind,
    DateTime EntryDate,
    decimal Amount,
    decimal VatRate,
    Guid? ProjectHakedisSectionId,
    Guid? SupplierInvoiceId,
    string? Description);

/// <summary>Elden ödeme veya elden avans.</summary>
public sealed record SaveSubcontractorCashEntryRequest(
    Guid SubcontractorContractId,
    Guid? SubcontractorProgressPaymentId,
    int Kind,
    DateTime EntryDate,
    decimal Amount,
    string? Description);

/// <summary>
/// Taşeron ödemeleri ve avansları.
///
/// İKİ AYRI TABLO, İKİ AYRI İZİN:
/// - Faturalı ödeme/avans: <c>subcontractor.*</c>. Muhasebeye ve proje
///   maliyetine girer.
/// - Elden ödeme/avans: AYRICA <c>extra_payment.*</c>. Resmî muhasebeye
///   hiçbir fiş yazmaz, proje maliyeti defterine satır açmaz.
///
/// Yetkisiz kullanıcının sorgusu elden tablosuna HİÇ uğramaz; gizleme
/// arayüzde değil, sorgu seviyesinde.
/// </summary>
[ApiController]
[Authorize]
[Route("api/subcontractor-ledger")]
public sealed class SubcontractorLedgerController(
    AppDbContext db,
    SubcontractorLedgerService ledger,
    IExtraPaymentVisibilityService extraPaymentVisibility) : ControllerBase
{
    /// <summary>
    /// Sözleşmenin ödeme/avans özeti ve hareketleri. Elden tutarlar
    /// yalnızca yetkiliye döner; yetkisiz kullanıcı <c>cashHidden</c>
    /// ile eksik gördüğünü bilir.
    /// </summary>
    [HttpGet("{contractId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorView)]
    public async Task<IActionResult> Get(
        Guid contractId, CancellationToken cancellationToken)
    {
        var contract = await db.SubcontractorContracts
            .AsNoTracking()
            .Where(x => x.Id == contractId)
            .Select(x => new { x.Id, x.ContractAmount, x.CurrencyCode })
            .SingleOrDefaultAsync(cancellationToken);

        if (contract is null)
            return NotFound(new { message = "Taşeron sözleşmesi bulunamadı." });

        var canViewCash = await extraPaymentVisibility
            .CanViewExtraPaymentAsync(cancellationToken);

        var summary = await ledger.GetSummaryAsync(
            contractId, canViewCash, cancellationToken);

        var entries = await db.SubcontractorLedgerEntries
            .AsNoTracking()
            .Where(x => x.SubcontractorContractId == contractId)
            .OrderByDescending(x => x.EntryDate)
            .Select(x => new
            {
                x.Id,
                Kind = (int)x.Kind,
                KindName = KindName(x.Kind),
                IsCash = false,
                x.EntryDate,
                x.Amount,
                x.VatRate,
                x.VatAmount,
                x.WithholdingAmount,
                x.PayableAmount,
                x.CurrencyCode,
                x.SupplierInvoiceId,
                x.SubcontractorProgressPaymentId,
                x.Description
            })
            .ToListAsync(cancellationToken);

        // Elden hareketler yalnızca yetkiliye; yetkisizde sorgu hiç
        // çalışmıyor.
        var cashEntries = canViewCash
            ? await db.SubcontractorCashLedgerEntries
                .AsNoTracking()
                .Where(x => x.SubcontractorContractId == contractId)
                .OrderByDescending(x => x.EntryDate)
                .Select(x => new
                {
                    x.Id,
                    Kind = (int)x.Kind,
                    KindName = KindName(x.Kind),
                    IsCash = true,
                    x.EntryDate,
                    x.Amount,
                    x.CurrencyCode,
                    x.SubcontractorProgressPaymentId,
                    x.Description
                })
                .ToListAsync(cancellationToken)
            : null;

        var cumulativeWork = await db.SubcontractorProgressPayments
            .AsNoTracking()
            .Where(x => x.SubcontractorContractId == contractId &&
                        x.Status != SubcontractorProgressPaymentStatus.Cancelled)
            .OrderByDescending(x => x.PeriodNumber)
            .Select(x => (decimal?)x.CumulativeAmount)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;

        return Ok(new
        {
            summary.InvoicedPaymentTotal,
            summary.InvoicedAdvanceTotal,
            summary.CashPaymentTotal,
            summary.CashAdvanceTotal,
            summary.OffsetTotal,
            summary.OpenAdvance,
            summary.CashHidden,
            contract.CurrencyCode,
            CumulativeWorkAmount = cumulativeWork,
            OverAdvanceWarning = SubcontractorLedgerService.BuildOverAdvanceWarning(
                summary.OpenAdvance, contract.ContractAmount, cumulativeWork),
            Entries = entries,
            CashEntries = cashEntries
        });
    }

    /// <summary>Faturalı ödeme ya da resmî avans kaydeder.</summary>
    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorManage)]
    public async Task<IActionResult> Create(
        SaveSubcontractorLedgerEntryRequest request,
        CancellationToken cancellationToken)
    {
        var contract = await db.SubcontractorContracts
            .Include(x => x.Sections)
            .SingleOrDefaultAsync(
                x => x.Id == request.SubcontractorContractId, cancellationToken);

        if (contract is null)
            return BadRequest(new { message = "Taşeron sözleşmesi bulunamadı." });

        var failure = await ValidateAsync(
            contract, request.Kind, request.Amount,
            request.SubcontractorProgressPaymentId,
            request.ProjectHakedisSectionId, cancellationToken);

        if (failure is not null)
            return BadRequest(new { message = failure });

        if (request.VatRate is < 0m or > 100m)
            return BadRequest(new { message = "KDV oranı 0 ile 100 arasında olmalıdır." });

        var amount = decimal.Round(request.Amount, 2);
        var vat = decimal.Round(amount * request.VatRate / 100m, 2);

        // Tevkifat oranı SÖZLEŞMEDEN gelir; her kayıtta elle girilseydi
        // aynı taşeronun iki ödemesi farklı oranla muhasebeleşir ve KDV
        // beyanı tutmazdı.
        var withholding = contract.WithholdingDenominator > 0
            ? decimal.Round(
                vat * contract.WithholdingNumerator /
                contract.WithholdingDenominator, 2)
            : 0m;

        var entry = new SubcontractorLedgerEntry
        {
            CompanyId = contract.CompanyId,
            SubcontractorContractId = contract.Id,
            SubcontractorProgressPaymentId = request.SubcontractorProgressPaymentId,
            Kind = (SubcontractorLedgerKind)request.Kind,
            EntryDate = UtcDate(request.EntryDate),
            Amount = amount,
            VatRate = request.VatRate,
            VatAmount = vat,
            WithholdingAmount = withholding,
            PayableAmount = decimal.Round(amount + vat - withholding, 2),
            CurrencyCode = contract.CurrencyCode,
            SupplierInvoiceId = request.SupplierInvoiceId,
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim()
        };

        db.SubcontractorLedgerEntries.Add(entry);

        var title = await db.CurrentAccounts
            .AsNoTracking()
            .Where(x => x.Id == contract.CurrentAccountId)
            .Select(x => x.Title)
            .SingleAsync(cancellationToken);

        // Kısım verilmemişse sözleşmenin tek kısmı varsa ona yazılır;
        // birden fazlaysa proje geneline kalır — rastgele bir kısım
        // seçmek maliyet analizini yanıltırdı.
        var sectionId = request.ProjectHakedisSectionId
            ?? (contract.Sections.Count == 1
                ? contract.Sections.Single().ProjectHakedisSectionId
                : null);

        ledger.WriteProjectCost(entry, contract, sectionId, title);

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            entry.Id,
            entry.PayableAmount,
            entry.WithholdingAmount,
            message = entry.Kind == SubcontractorLedgerKind.Advance
                ? "Taşeron avansı kaydedildi."
                : "Taşeron ödemesi kaydedildi."
        });
    }

    /// <summary>
    /// Elden ödeme ya da elden avans kaydeder. Resmî muhasebeye hiçbir
    /// fiş yazmaz, proje maliyeti defterine satır açmaz.
    /// </summary>
    [HttpPost("cash")]
    [RequirePermission(PermissionCatalog.Keys.ExtraPaymentManage)]
    public async Task<IActionResult> CreateCash(
        SaveSubcontractorCashEntryRequest request,
        CancellationToken cancellationToken)
    {
        var contract = await db.SubcontractorContracts
            .SingleOrDefaultAsync(
                x => x.Id == request.SubcontractorContractId, cancellationToken);

        if (contract is null)
            return BadRequest(new { message = "Taşeron sözleşmesi bulunamadı." });

        var failure = await ValidateAsync(
            contract, request.Kind, request.Amount,
            request.SubcontractorProgressPaymentId, null, cancellationToken);

        if (failure is not null)
            return BadRequest(new { message = failure });

        db.SubcontractorCashLedgerEntries.Add(new SubcontractorCashLedgerEntry
        {
            CompanyId = contract.CompanyId,
            SubcontractorContractId = contract.Id,
            SubcontractorProgressPaymentId = request.SubcontractorProgressPaymentId,
            Kind = (SubcontractorLedgerKind)request.Kind,
            EntryDate = UtcDate(request.EntryDate),
            Amount = decimal.Round(request.Amount, 2),
            CurrencyCode = contract.CurrencyCode,
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim()
        });

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Elden kayıt eklendi." });
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorManage)]
    public async Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken)
    {
        var entry = await db.SubcontractorLedgerEntries
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entry is null)
            return NotFound(new { message = "Kayıt bulunamadı." });

        entry.IsDeleted = true;
        entry.DeletedAtUtc = DateTime.UtcNow;

        // Maliyet kaydı da düşmeli; kalırsa proje maliyeti silinen bir
        // ödemeyi taşımaya devam eder.
        var costs = await db.ProjectCostTransactions
            .Where(x => x.ReferenceType == "SubcontractorLedgerEntry" &&
                        x.ReferenceId == entry.Id)
            .ToListAsync(cancellationToken);

        foreach (var cost in costs)
        {
            cost.IsDeleted = true;
            cost.DeletedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Kayıt silindi." });
    }

    [HttpDelete("cash/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ExtraPaymentManage)]
    public async Task<IActionResult> DeleteCash(
        Guid id, CancellationToken cancellationToken)
    {
        var entry = await db.SubcontractorCashLedgerEntries
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entry is null)
            return NotFound(new { message = "Kayıt bulunamadı." });

        entry.IsDeleted = true;
        entry.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Elden kayıt silindi." });
    }

    // ---------- Yardımcılar ----------

    private async Task<string?> ValidateAsync(
        SubcontractorContract contract,
        int kind,
        decimal amount,
        Guid? progressPaymentId,
        Guid? sectionId,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(SubcontractorLedgerKind), kind))
            return "Geçersiz hareket türü.";

        if (amount <= 0m)
            return "Tutar sıfırdan büyük olmalıdır.";

        if (progressPaymentId is Guid paymentId)
        {
            var belongs = await db.SubcontractorProgressPayments.AnyAsync(
                x => x.Id == paymentId &&
                     x.SubcontractorContractId == contract.Id,
                cancellationToken);

            if (!belongs)
                return "Seçilen hakediş bu sözleşmeye ait değil.";
        }

        if (sectionId is Guid section)
        {
            var belongs = await db.ProjectHakedisSections.AnyAsync(
                x => x.Id == section && x.ProjectId == contract.ProjectId,
                cancellationToken);

            if (!belongs)
                return "Seçilen icmal kısmı bu projeye ait değil.";
        }

        return null;
    }

    private static DateTime UtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private static string KindName(SubcontractorLedgerKind kind) => kind switch
    {
        SubcontractorLedgerKind.Payment => "Ödeme",
        SubcontractorLedgerKind.Advance => "Avans",
        _ => "Bilinmiyor"
    };
}
