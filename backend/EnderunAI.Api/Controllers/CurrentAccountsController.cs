using EnderunAI.Api.Contracts.Core;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/current-accounts")]
public sealed class CurrentAccountsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.CurrentAccountsView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] CurrentAccountStatus? status,
        CancellationToken cancellationToken)
    {
        var query = db.CurrentAccounts.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        var items = await query
            .OrderBy(x => x.Title)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.Code,
                x.Title,
                x.ShortName,
                x.Roles,
                x.Status,
                x.TaxOffice,
                x.TaxNumber,
                x.AuthorizedPerson,
                x.Phone,
                x.Email,
                x.Address,
                x.PaymentTerm,
                x.CreditLimit,
                x.IsActive,
                // Muhasebe hesap eşlemeleri: cariler ekranı bu alanları
                // gösteriyordu ama projeksiyonda yoktu, bu yüzden her kart
                // "Bağlı Değil" görünüyordu.
                x.PayableAccountingAccountId,
                PayableAccountCode = x.PayableAccountingAccount != null
                    ? x.PayableAccountingAccount.Code
                    : null,
                x.ReceivableAccountingAccountId,
                ReceivableAccountCode = x.ReceivableAccountingAccount != null
                    ? x.ReceivableAccountingAccount.Code
                    : null
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.CurrentAccountsView)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await db.CurrentAccounts
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.Code,
                x.Title,
                x.ShortName,
                x.Roles,
                x.Status,
                x.TaxOffice,
                x.TaxNumber,
                x.AuthorizedPerson,
                x.Phone,
                x.Email,
                x.Address,
                x.PaymentTerm,
                x.CreditLimit,
                x.IsActive
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (item is null)
            return NotFound(new { message = "Cari kart bulunamadı." });

        return Ok(item);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.CurrentAccountsEdit)]
    public async Task<IActionResult> Update(
        Guid id,
        CreateCurrentAccountRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.CurrentAccounts
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Cari kart bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "Cari ünvanı zorunludur." });

        if (request.Roles <= 0)
            return BadRequest(new { message = "En az bir cari rolü seçilmelidir." });

        /*
         * Cari kodu ve şirket alanı düzenleme sırasında bilinçli olarak
         * değiştirilmez. Muhasebe ve hareket bütünlüğü korunur.
         */
        entity.Title = request.Title.Trim();
        entity.ShortName = request.ShortName?.Trim();
        entity.Roles = (CurrentAccountRoles)request.Roles;
        entity.TaxOffice = request.TaxOffice?.Trim();
        entity.TaxNumber = request.TaxNumber?.Trim();
        entity.AuthorizedPerson = request.AuthorizedPerson?.Trim();
        entity.Phone = request.Phone?.Trim();
        entity.Email = request.Email?.Trim();
        entity.Address = request.Address?.Trim();
        entity.PaymentTerm = request.PaymentTerm?.Trim();
        entity.CreditLimit = request.CreditLimit;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Cari kart güncellendi.",
            entity.Id,
            entity.Status
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.CurrentAccountsCreate)]
    public async Task<IActionResult> Create(
        CreateCurrentAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (!await db.Companies.AnyAsync(
                x => x.Id == request.CompanyId && x.IsActive,
                cancellationToken))
        {
            return BadRequest(new { message = "Geçerli bir şirket seçilmelidir." });
        }

        var code = request.Code.Trim().ToUpperInvariant();

        if (await db.CurrentAccounts.AnyAsync(
                x => x.CompanyId == request.CompanyId && x.Code == code,
                cancellationToken))
        {
            return Conflict(new { message = "Bu cari kodu zaten kullanılıyor." });
        }

        var entity = new CurrentAccount
        {
            CompanyId = request.CompanyId,
            Code = code,
            Title = request.Title.Trim(),
            ShortName = request.ShortName?.Trim(),
            Roles = (CurrentAccountRoles)request.Roles,
            Status = CurrentAccountStatus.Draft,
            TaxOffice = request.TaxOffice?.Trim(),
            TaxNumber = request.TaxNumber?.Trim(),
            AuthorizedPerson = request.AuthorizedPerson?.Trim(),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            Address = request.Address?.Trim(),
            PaymentTerm = request.PaymentTerm?.Trim(),
            CreditLimit = request.CreditLimit
        };

        db.CurrentAccounts.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(entity);
    }

    [HttpPost("{id:guid}/submit")]
    [RequirePermission(PermissionCatalog.Keys.CurrentAccountsEdit)]
    public async Task<IActionResult> Submit(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await db.CurrentAccounts
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Cari kart bulunamadı." });

        if (entity.Status != CurrentAccountStatus.Draft)
            return BadRequest(new { message = "Sadece taslak cari kart onaya gönderilebilir." });

        entity.Status = CurrentAccountStatus.PendingApproval;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Cari kart onaya gönderildi.", entity.Id, entity.Status });
    }

    [HttpPost("{id:guid}/approve")]
    [RequirePermission(PermissionCatalog.Keys.CurrentAccountsApprove)]
    public async Task<IActionResult> Approve(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await db.CurrentAccounts
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Cari kart bulunamadı." });

        if (entity.Status != CurrentAccountStatus.PendingApproval)
            return BadRequest(new { message = "Cari kart onay bekleyen durumda değil." });

        entity.Status = CurrentAccountStatus.Approved;
        entity.ApprovedAtUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Cari kart onaylandı.", entity.Id, entity.Status });
    }

    /// <summary>
    /// Cari bakiyeleri — ayrı bir hareket defteri tutulmaz, tek gerçek
    /// kaynak muhasebe defteridir: kesinleşmiş (Posted) fiş satırlarının
    /// cari boyutu üzerinden hesaplanır. Bakiye = Borç − Alacak
    /// (pozitif: bizden alacaklı değil, bize borçlu → müşteri;
    /// negatif: biz borçluyuz → satıcı).
    /// </summary>
    [HttpGet("balances")]
    [RequirePermission(PermissionCatalog.Keys.CurrentAccountsView)]
    public async Task<IActionResult> GetBalances(
        [FromQuery] Guid? companyId,
        CancellationToken cancellationToken)
    {
        var query = PostedLines();

        if (companyId.HasValue)
            query = query.Where(x => x.AccountingVoucher.CompanyId == companyId.Value);

        var balances = await query
            .Where(x => x.CurrentAccountId != null)
            .GroupBy(x => x.CurrentAccountId!.Value)
            .Select(g => new
            {
                CurrentAccountId = g.Key,
                TotalDebit = g.Sum(x => x.DebitAmountLocal),
                TotalCredit = g.Sum(x => x.CreditAmountLocal),
                MovementCount = g.Count(),
                LastMovementDate = g.Max(x => x.AccountingVoucher.VoucherDate)
            })
            .ToListAsync(cancellationToken);

        return Ok(balances.Select(x => new
        {
            x.CurrentAccountId,
            x.TotalDebit,
            x.TotalCredit,
            Balance = decimal.Round(x.TotalDebit - x.TotalCredit, 2),
            x.MovementCount,
            x.LastMovementDate
        }));
    }

    /// <summary>
    /// Cari ekstresi: dönem başı bakiyesi + hareketler + her satırda
    /// yürüyen bakiye. Kaynak muhasebe defteri (yalnızca kesinleşmiş
    /// fişler).
    /// </summary>
    [HttpGet("{id:guid}/statement")]
    [RequirePermission(PermissionCatalog.Keys.CurrentAccountsView)]
    public async Task<IActionResult> GetStatement(
        Guid id,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var account = await db.CurrentAccounts
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.Id, x.Code, x.Title, x.CompanyId, x.CreditLimit })
            .SingleOrDefaultAsync(cancellationToken);

        if (account is null)
            return NotFound(new { message = "Cari kart bulunamadı." });

        var baseQuery = PostedLines().Where(x => x.CurrentAccountId == id);

        var openingBalance = 0m;
        if (startDate.HasValue)
        {
            var start = AsUtcDate(startDate.Value);
            openingBalance = await baseQuery
                .Where(x => x.AccountingVoucher.VoucherDate < start)
                .SumAsync(x => x.DebitAmountLocal - x.CreditAmountLocal, cancellationToken);
        }

        var periodQuery = baseQuery;
        if (startDate.HasValue)
        {
            var start = AsUtcDate(startDate.Value);
            periodQuery = periodQuery.Where(x => x.AccountingVoucher.VoucherDate >= start);
        }
        if (endDate.HasValue)
        {
            var exclusiveEnd = AsUtcDate(endDate.Value).AddDays(1);
            periodQuery = periodQuery.Where(x => x.AccountingVoucher.VoucherDate < exclusiveEnd);
        }

        var rows = await periodQuery
            .OrderBy(x => x.AccountingVoucher.VoucherDate)
            .ThenBy(x => x.AccountingVoucher.VoucherNumber)
            .ThenBy(x => x.LineNumber)
            .Select(x => new
            {
                x.Id,
                VoucherId = x.AccountingVoucherId,
                x.AccountingVoucher.VoucherNumber,
                x.AccountingVoucher.VoucherDate,
                x.AccountingVoucher.SourceModule,
                AccountCode = x.AccountingAccount.Code,
                AccountName = x.AccountingAccount.Name,
                x.Description,
                x.DocumentNumber,
                x.DueDate,
                ProjectCode = x.Project != null ? x.Project.Code : null,
                Debit = x.DebitAmountLocal,
                Credit = x.CreditAmountLocal
            })
            .ToListAsync(cancellationToken);

        var running = decimal.Round(openingBalance, 2);
        var lines = new List<object>(rows.Count);

        foreach (var row in rows)
        {
            running = decimal.Round(running + row.Debit - row.Credit, 2);
            lines.Add(new
            {
                row.Id,
                row.VoucherId,
                row.VoucherNumber,
                row.VoucherDate,
                row.SourceModule,
                row.AccountCode,
                row.AccountName,
                row.Description,
                row.DocumentNumber,
                row.DueDate,
                row.ProjectCode,
                row.Debit,
                row.Credit,
                RunningBalance = running
            });
        }

        var periodDebit = decimal.Round(rows.Sum(x => x.Debit), 2);
        var periodCredit = decimal.Round(rows.Sum(x => x.Credit), 2);

        return Ok(new
        {
            currentAccount = new
            {
                account.Id,
                account.Code,
                account.Title,
                account.CreditLimit
            },
            openingBalance = decimal.Round(openingBalance, 2),
            periodDebit,
            periodCredit,
            closingBalance = running,
            lineCount = lines.Count,
            lines
        });
    }

    /// <summary>
    /// Ekstre/bakiye hesaplamalarının ortak temeli: yalnızca
    /// kesinleşmiş (Posted) ve silinmemiş fiş satırları.
    /// </summary>
    private IQueryable<AccountingVoucherLine> PostedLines() =>
        db.AccountingVoucherLines
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                !x.AccountingVoucher.IsDeleted &&
                x.AccountingVoucher.Status == AccountingVoucherStatus.Posted);

    private static DateTime AsUtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}
