using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Kasa ve banka hesapları ile hareketleri. Her hareket, kaydedildiği
/// anda dengeli ve doğrudan Posted bir muhasebe fişi üretir; fiş
/// üretilemezse hareket de kaydedilmez (tek işlem).
/// </summary>
[ApiController]
[Authorize]
[Route("api/cash-accounts")]
public sealed class CashAccountsController(
    AppDbContext db,
    IAccountingIntegrationService accountingIntegration) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] int? type,
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = db.CashAccounts
            .Include(x => x.AccountingAccount)
            .AsNoTracking()
            .AsQueryable();

        if (companyId is not null)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (type is not null)
            query = query.Where(x => (int)x.Type == type.Value);

        if (!includeInactive)
            query = query.Where(x => x.IsActive);

        var accounts = await query
            .OrderBy(x => x.Type).ThenBy(x => x.Code)
            .ToListAsync(cancellationToken);

        var accountIds = accounts.Select(x => x.Id).ToList();

        var movements = await db.CashTransactions
            .AsNoTracking()
            .Where(x => accountIds.Contains(x.CashAccountId))
            .GroupBy(x => new { x.CashAccountId, x.Direction })
            .Select(g => new
            {
                g.Key.CashAccountId,
                g.Key.Direction,
                Total = g.Sum(x => x.Amount),
                Count = g.Count()
            })
            .ToListAsync(cancellationToken);

        var response = accounts.Select(account =>
        {
            var totalIn = movements
                .Where(m => m.CashAccountId == account.Id
                    && m.Direction == CashTransactionDirection.In)
                .Sum(m => m.Total);
            var totalOut = movements
                .Where(m => m.CashAccountId == account.Id
                    && m.Direction == CashTransactionDirection.Out)
                .Sum(m => m.Total);
            var count = movements
                .Where(m => m.CashAccountId == account.Id)
                .Sum(m => m.Count);

            return new CashAccountResponse(
                account.Id,
                account.CompanyId,
                (int)account.Type,
                CashAccountTypeName(account.Type),
                account.Code,
                account.Name,
                account.BankName,
                account.Iban,
                account.CurrencyCode,
                account.OpeningBalance,
                account.AccountingAccountId,
                account.AccountingAccount.Code,
                account.AccountingAccount.Name,
                totalIn,
                totalOut,
                account.OpeningBalance + totalIn - totalOut,
                count,
                account.IsActive);
        }).ToList();

        return Ok(response);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.FinanceCreate)]
    public async Task<IActionResult> Create(
        CreateCashAccountRequest request,
        CancellationToken cancellationToken)
    {
        var code = Normalize(request.Code);
        var name = Normalize(request.Name);

        if (string.IsNullOrWhiteSpace(code))
            return BadRequest(new { message = "Hesap kodu zorunludur." });
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Hesap adı zorunludur." });

        if (!await db.Companies.AnyAsync(x => x.Id == request.CompanyId, cancellationToken))
            return BadRequest(new { message = "Şirket bulunamadı." });

        if (await db.CashAccounts.AnyAsync(
                x => x.CompanyId == request.CompanyId && x.Code == code, cancellationToken))
        {
            return Conflict(new { message = $"'{code}' kodlu kasa/banka hesabı zaten var." });
        }

        var accountingAccount = await db.AccountingAccounts
            .SingleOrDefaultAsync(
                x => x.Id == request.AccountingAccountId
                    && x.CompanyId == request.CompanyId,
                cancellationToken);

        if (accountingAccount is null)
            return BadRequest(new { message = "Muhasebe hesabı bulunamadı." });
        if (!accountingAccount.IsActive)
            return BadRequest(new { message = "Seçilen muhasebe hesabı pasif." });
        if (!accountingAccount.IsPostingAllowed)
            return BadRequest(new { message = "Seçilen muhasebe hesabına fiş kesilemez (grup hesabı)." });

        var account = new CashAccount
        {
            CompanyId = request.CompanyId,
            Type = (CashAccountType)request.Type,
            Code = code,
            Name = name,
            BankName = Normalize(request.BankName),
            Iban = Normalize(request.Iban),
            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
                ? "TRY"
                : request.CurrencyCode.Trim().ToUpperInvariant(),
            OpeningBalance = request.OpeningBalance,
            AccountingAccountId = request.AccountingAccountId
        };

        db.CashAccounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { account.Id, account.Code, account.Name });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.FinanceEdit)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateCashAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await db.CashAccounts
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (account is null)
            return NotFound(new { message = "Kasa/banka hesabı bulunamadı." });

        var name = Normalize(request.Name);
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Hesap adı zorunludur." });

        var accountingAccount = await db.AccountingAccounts
            .SingleOrDefaultAsync(
                x => x.Id == request.AccountingAccountId
                    && x.CompanyId == account.CompanyId,
                cancellationToken);

        if (accountingAccount is null)
            return BadRequest(new { message = "Muhasebe hesabı bulunamadı." });
        if (!accountingAccount.IsPostingAllowed)
            return BadRequest(new { message = "Seçilen muhasebe hesabına fiş kesilemez (grup hesabı)." });

        account.Name = name;
        account.BankName = Normalize(request.BankName);
        account.Iban = Normalize(request.Iban);
        account.OpeningBalance = request.OpeningBalance;
        account.AccountingAccountId = request.AccountingAccountId;
        account.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { account.Id });
    }

    [HttpGet("{id:guid}/transactions")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> GetTransactions(
        Guid id,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var account = await db.CashAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (account is null)
            return NotFound(new { message = "Kasa/banka hesabı bulunamadı." });

        var start = AsUtcDate(startDate);
        var end = AsUtcDate(endDate);

        // Dönem başı devir: filtre başlangıcından önceki tüm hareketler.
        var periodOpening = account.OpeningBalance;
        if (start is not null)
        {
            var earlier = await db.CashTransactions
                .AsNoTracking()
                .Where(x => x.CashAccountId == id && x.TransactionDate < start.Value)
                .GroupBy(x => x.Direction)
                .Select(g => new { Direction = g.Key, Total = g.Sum(x => x.Amount) })
                .ToListAsync(cancellationToken);

            periodOpening += earlier
                .Where(x => x.Direction == CashTransactionDirection.In).Sum(x => x.Total);
            periodOpening -= earlier
                .Where(x => x.Direction == CashTransactionDirection.Out).Sum(x => x.Total);
        }

        var query = db.CashTransactions
            .AsNoTracking()
            .Where(x => x.CashAccountId == id);

        if (start is not null)
            query = query.Where(x => x.TransactionDate >= start.Value);
        if (end is not null)
            query = query.Where(x => x.TransactionDate <= end.Value.AddDays(1).AddTicks(-1));

        var rows = await query
            .OrderBy(x => x.TransactionDate).ThenBy(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.CashAccountId,
                x.TransactionDate,
                x.TransactionType,
                x.Direction,
                x.Amount,
                x.CurrencyCode,
                x.Description,
                x.DocumentNumber,
                x.CurrentAccountId,
                CurrentAccountTitle = x.CurrentAccount != null ? x.CurrentAccount.Title : null,
                x.ProjectId,
                ProjectCode = x.Project != null ? x.Project.Code : null,
                x.SourceModule,
                x.SourceEntityId,
                x.AccountingVoucherId,
                AccountingVoucherNumber = x.AccountingVoucher != null
                    ? x.AccountingVoucher.VoucherNumber
                    : null
            })
            .ToListAsync(cancellationToken);

        var running = periodOpening;
        var transactions = new List<CashTransactionResponse>(rows.Count);

        foreach (var row in rows)
        {
            running += row.Direction == CashTransactionDirection.In
                ? row.Amount
                : -row.Amount;

            transactions.Add(new CashTransactionResponse(
                row.Id,
                row.CashAccountId,
                row.TransactionDate,
                (int)row.TransactionType,
                CashTransactionTypeName(row.TransactionType),
                (int)row.Direction,
                row.Amount,
                row.CurrencyCode,
                row.Description,
                row.DocumentNumber,
                row.CurrentAccountId,
                row.CurrentAccountTitle,
                row.ProjectId,
                row.ProjectCode,
                row.SourceModule,
                row.SourceEntityId,
                row.AccountingVoucherId,
                row.AccountingVoucherNumber,
                running));
        }

        var totalIn = rows
            .Where(x => x.Direction == CashTransactionDirection.In).Sum(x => x.Amount);
        var totalOut = rows
            .Where(x => x.Direction == CashTransactionDirection.Out).Sum(x => x.Amount);

        return Ok(new CashAccountStatementResponse(
            account.Id,
            account.Code,
            account.Name,
            account.CurrencyCode,
            account.OpeningBalance,
            periodOpening,
            totalIn,
            totalOut,
            periodOpening + totalIn - totalOut,
            transactions));
    }

    [HttpPost("{id:guid}/transactions")]
    [RequirePermission(PermissionCatalog.Keys.FinanceCreate)]
    public async Task<IActionResult> CreateTransaction(
        Guid id,
        CreateCashTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var account = await db.CashAccounts
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (account is null)
            return NotFound(new { message = "Kasa/banka hesabı bulunamadı." });

        if (request.Amount <= 0m)
            return BadRequest(new { message = "Tutar sıfırdan büyük olmalıdır." });

        var transactionType = (CashTransactionType)request.TransactionType;
        if (transactionType is not (CashTransactionType.Collection or CashTransactionType.Payment))
        {
            return BadRequest(new
            {
                message = "Bu ekrandan yalnızca tahsilat ve ödeme girilebilir. " +
                    "Çek ve faktoring hareketleri kendi modüllerinden oluşturulur."
            });
        }

        if (request.CurrentAccountId is null)
            return BadRequest(new { message = "Tahsilat/ödeme için cari seçimi zorunludur." });

        if (!await db.CurrentAccounts.AnyAsync(
                x => x.Id == request.CurrentAccountId.Value, cancellationToken))
        {
            return BadRequest(new { message = "Cari bulunamadı." });
        }

        if (request.ProjectId is not null
            && !await db.Projects.AnyAsync(x => x.Id == request.ProjectId.Value, cancellationToken))
        {
            return BadRequest(new { message = "Proje bulunamadı." });
        }

        var description = Normalize(request.Description);
        if (string.IsNullOrWhiteSpace(description))
            return BadRequest(new { message = "Açıklama zorunludur." });

        var transaction = new CashTransaction
        {
            CashAccountId = account.Id,
            TransactionDate = DateTime.SpecifyKind(
                request.TransactionDate.Date, DateTimeKind.Utc),
            TransactionType = transactionType,
            Direction = transactionType == CashTransactionType.Collection
                ? CashTransactionDirection.In
                : CashTransactionDirection.Out,
            Amount = decimal.Round(request.Amount, 2),
            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
                ? account.CurrencyCode
                : request.CurrencyCode.Trim().ToUpperInvariant(),
            Description = description,
            DocumentNumber = Normalize(request.DocumentNumber),
            CurrentAccountId = request.CurrentAccountId,
            ProjectId = request.ProjectId,
            SourceModule = "CashTransaction"
        };

        await using var dbTransaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            db.CashTransactions.Add(transaction);
            await db.SaveChangesAsync(cancellationToken);

            var voucherId = await accountingIntegration
                .CreateCashTransactionVoucherAsync(transaction, cancellationToken);

            transaction.AccountingVoucherId = voucherId;
            await db.SaveChangesAsync(cancellationToken);

            await dbTransaction.CommitAsync(cancellationToken);

            return Ok(new { transaction.Id, AccountingVoucherId = voucherId });
        }
        catch (InvalidOperationException exception)
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            return Conflict(new { message = exception.Message });
        }
    }

    internal static string CashAccountTypeName(CashAccountType type) => type switch
    {
        CashAccountType.Cash => "Kasa",
        CashAccountType.Bank => "Banka",
        _ => type.ToString()
    };

    internal static string CashTransactionTypeName(CashTransactionType type) => type switch
    {
        CashTransactionType.Collection => "Tahsilat",
        CashTransactionType.Payment => "Ödeme",
        CashTransactionType.ChequeCollection => "Çek tahsili",
        CashTransactionType.ChequePayment => "Çek ödemesi",
        CashTransactionType.Factoring => "Faktoring",
        _ => type.ToString()
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime? AsUtcDate(DateTime? value) => value is null
        ? null
        : DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Utc);
}
