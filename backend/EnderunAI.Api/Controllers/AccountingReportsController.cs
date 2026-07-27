using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/accounting-reports")]
public sealed class AccountingReportsController(AppDbContext db)
    : ControllerBase
{
    private sealed record ReportRow(
        Guid VoucherId,
        DateTime VoucherDate,
        string VoucherNumber,
        int VoucherType,
        string? VoucherDescription,
        string? ReferenceNumber,
        string? SourceModule,
        int LineNumber,
        Guid AccountingAccountId,
        string AccountCode,
        string AccountName,
        string? LineDescription,
        Guid? CurrentAccountId,
        string? CurrentAccountCode,
        string? CurrentAccountTitle,
        Guid? ProjectId,
        string? ProjectCode,
        string? ProjectName,
        string? CostCenterCode,
        string? DocumentNumber,
        DateTime? DocumentDate,
        DateTime? DueDate,
        string CurrencyCode,
        decimal ExchangeRate,
        decimal DebitAmount,
        decimal CreditAmount,
        decimal DebitAmountLocal,
        decimal CreditAmountLocal);

    [HttpGet("journal")]
    public async Task<IActionResult> Journal(
        [FromQuery] Guid companyId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] Guid? accountingAccountId,
        [FromQuery] Guid? currentAccountId,
        [FromQuery] Guid? projectId,
        [FromQuery] string? accountCode,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(new { message = "Şirket seçimi zorunludur." });
        }

        if (startDate.HasValue &&
            endDate.HasValue &&
            UtcDate(endDate.Value) < UtcDate(startDate.Value))
        {
            return BadRequest(new
            {
                message = "Bitiş tarihi başlangıç tarihinden önce olamaz."
            });
        }

        var query = BaseQuery(companyId);
        query = ApplyDimensions(
            query,
            accountingAccountId,
            currentAccountId,
            projectId,
            accountCode,
            search);

        if (startDate.HasValue)
        {
            var start = UtcDate(startDate.Value);
            query = query.Where(
                x => x.AccountingVoucher.VoucherDate >= start);
        }

        if (endDate.HasValue)
        {
            var exclusiveEnd = UtcDate(endDate.Value).AddDays(1);
            query = query.Where(
                x => x.AccountingVoucher.VoucherDate < exclusiveEnd);
        }

        var lines = await Project(query)
            .OrderBy(x => x.VoucherDate)
            .ThenBy(x => x.VoucherNumber)
            .ThenBy(x => x.LineNumber)
            .ToListAsync(cancellationToken);

        var totalDebit = decimal.Round(
            lines.Sum(x => x.DebitAmountLocal),
            2);
        var totalCredit = decimal.Round(
            lines.Sum(x => x.CreditAmountLocal),
            2);

        return Ok(new
        {
            StartDate = startDate.HasValue
                ? UtcDate(startDate.Value)
                : (DateTime?)null,
            EndDate = endDate.HasValue
                ? UtcDate(endDate.Value)
                : (DateTime?)null,
            Summary = new
            {
                VoucherCount = lines
                    .Select(x => x.VoucherId)
                    .Distinct()
                    .Count(),
                LineCount = lines.Count,
                TotalDebit = totalDebit,
                TotalCredit = totalCredit,
                Difference = decimal.Round(
                    totalDebit - totalCredit,
                    2)
            },
            Lines = lines
        });
    }

    [HttpGet("general-ledger")]
    public async Task<IActionResult> GeneralLedger(
        [FromQuery] Guid companyId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] Guid? accountingAccountId,
        [FromQuery] Guid? currentAccountId,
        [FromQuery] Guid? projectId,
        [FromQuery] string? accountCode,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(new { message = "Şirket seçimi zorunludur." });
        }

        if (startDate.HasValue &&
            endDate.HasValue &&
            UtcDate(endDate.Value) < UtcDate(startDate.Value))
        {
            return BadRequest(new
            {
                message = "Bitiş tarihi başlangıç tarihinden önce olamaz."
            });
        }

        var dimensionQuery = ApplyDimensions(
            BaseQuery(companyId),
            accountingAccountId,
            currentAccountId,
            projectId,
            accountCode,
            search);

        var openingLines = new List<ReportRow>();

        if (startDate.HasValue)
        {
            var start = UtcDate(startDate.Value);

            openingLines = await Project(
                    dimensionQuery.Where(
                        x => x.AccountingVoucher.VoucherDate < start))
                .ToListAsync(cancellationToken);
        }

        var periodQuery = dimensionQuery;

        if (startDate.HasValue)
        {
            var start = UtcDate(startDate.Value);
            periodQuery = periodQuery.Where(
                x => x.AccountingVoucher.VoucherDate >= start);
        }

        if (endDate.HasValue)
        {
            var exclusiveEnd = UtcDate(endDate.Value).AddDays(1);
            periodQuery = periodQuery.Where(
                x => x.AccountingVoucher.VoucherDate < exclusiveEnd);
        }

        var periodLines = await Project(periodQuery)
            .OrderBy(x => x.AccountCode)
            .ThenBy(x => x.VoucherDate)
            .ThenBy(x => x.VoucherNumber)
            .ThenBy(x => x.LineNumber)
            .ToListAsync(cancellationToken);

        var openingByAccount = openingLines
            .GroupBy(x => new
            {
                x.AccountingAccountId,
                x.AccountCode,
                x.AccountName
            })
            .ToDictionary(
                x => x.Key.AccountingAccountId,
                x => new
                {
                    x.Key.AccountCode,
                    x.Key.AccountName,
                    Balance = decimal.Round(
                        x.Sum(y =>
                            y.DebitAmountLocal -
                            y.CreditAmountLocal),
                        2)
                });

        var periodByAccount = periodLines
            .GroupBy(x => new
            {
                x.AccountingAccountId,
                x.AccountCode,
                x.AccountName
            })
            .ToDictionary(
                x => x.Key.AccountingAccountId,
                x => new
                {
                    x.Key.AccountCode,
                    x.Key.AccountName,
                    Lines = x.ToList()
                });

        var accountIds = openingByAccount.Keys
            .Union(periodByAccount.Keys)
            .ToList();

        var accounts = new List<object>();

        foreach (var accountId in accountIds
                     .OrderBy(id =>
                         periodByAccount.TryGetValue(id, out var period)
                             ? period.AccountCode
                             : openingByAccount[id].AccountCode))
        {
            openingByAccount.TryGetValue(accountId, out var opening);
            periodByAccount.TryGetValue(accountId, out var period);

            var openingBalance = opening?.Balance ?? 0m;
            var sourceLines = period?.Lines ?? new List<ReportRow>();
            var runningBalance = openingBalance;
            var ledgerLines = new List<object>();

            foreach (var line in sourceLines)
            {
                runningBalance = decimal.Round(
                    runningBalance +
                    line.DebitAmountLocal -
                    line.CreditAmountLocal,
                    2);

                ledgerLines.Add(new
                {
                    line.VoucherId,
                    line.VoucherDate,
                    line.VoucherNumber,
                    line.VoucherType,
                    line.LineNumber,
                    line.AccountingAccountId,
                    line.AccountCode,
                    line.AccountName,
                    Description = line.LineDescription ??
                        line.VoucherDescription,
                    line.ReferenceNumber,
                    line.SourceModule,
                    line.CurrentAccountId,
                    line.CurrentAccountCode,
                    line.CurrentAccountTitle,
                    line.ProjectId,
                    line.ProjectCode,
                    line.ProjectName,
                    line.CostCenterCode,
                    line.DocumentNumber,
                    line.DocumentDate,
                    line.DueDate,
                    DebitAmount = line.DebitAmountLocal,
                    CreditAmount = line.CreditAmountLocal,
                    Balance = runningBalance
                });
            }

            var periodDebit = decimal.Round(
                sourceLines.Sum(x => x.DebitAmountLocal),
                2);
            var periodCredit = decimal.Round(
                sourceLines.Sum(x => x.CreditAmountLocal),
                2);

            accounts.Add(new
            {
                AccountingAccountId = accountId,
                AccountCode = period?.AccountCode ??
                    opening!.AccountCode,
                AccountName = period?.AccountName ??
                    opening!.AccountName,
                OpeningBalance = openingBalance,
                PeriodDebit = periodDebit,
                PeriodCredit = periodCredit,
                ClosingBalance = decimal.Round(
                    openingBalance +
                    periodDebit -
                    periodCredit,
                    2),
                Lines = ledgerLines
            });
        }

        var totalDebit = decimal.Round(
            periodLines.Sum(x => x.DebitAmountLocal),
            2);
        var totalCredit = decimal.Round(
            periodLines.Sum(x => x.CreditAmountLocal),
            2);

        return Ok(new
        {
            StartDate = startDate.HasValue
                ? UtcDate(startDate.Value)
                : (DateTime?)null,
            EndDate = endDate.HasValue
                ? UtcDate(endDate.Value)
                : (DateTime?)null,
            Summary = new
            {
                AccountCount = accounts.Count,
                VoucherCount = periodLines
                    .Select(x => x.VoucherId)
                    .Distinct()
                    .Count(),
                LineCount = periodLines.Count,
                TotalDebit = totalDebit,
                TotalCredit = totalCredit,
                Difference = decimal.Round(
                    totalDebit - totalCredit,
                    2)
            },
            Accounts = accounts
        });
    }

    private IQueryable<AccountingVoucherLine> BaseQuery(Guid companyId)
    {
        return db.AccountingVoucherLines
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                !x.AccountingVoucher.IsDeleted &&
                x.AccountingVoucher.CompanyId == companyId &&
                x.AccountingVoucher.Status ==
                    AccountingVoucherStatus.Posted);
    }

    private static IQueryable<AccountingVoucherLine> ApplyDimensions(
        IQueryable<AccountingVoucherLine> query,
        Guid? accountingAccountId,
        Guid? currentAccountId,
        Guid? projectId,
        string? accountCode,
        string? search)
    {
        if (accountingAccountId.HasValue)
        {
            query = query.Where(
                x => x.AccountingAccountId ==
                    accountingAccountId.Value);
        }

        if (currentAccountId.HasValue)
        {
            query = query.Where(
                x => x.CurrentAccountId ==
                    currentAccountId.Value);
        }

        if (projectId.HasValue)
        {
            query = query.Where(
                x => x.ProjectId == projectId.Value);
        }

        if (!string.IsNullOrWhiteSpace(accountCode))
        {
            var code = accountCode.Trim().ToLower();

            query = query.Where(
                x => x.AccountingAccount.Code
                    .ToLower()
                    .StartsWith(code));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();

            query = query.Where(x =>
                x.AccountingVoucher.VoucherNumber
                    .ToLower()
                    .Contains(term) ||
                (x.AccountingVoucher.Description != null &&
                 x.AccountingVoucher.Description
                     .ToLower()
                     .Contains(term)) ||
                (x.AccountingVoucher.ReferenceNumber != null &&
                 x.AccountingVoucher.ReferenceNumber
                     .ToLower()
                     .Contains(term)) ||
                x.AccountingAccount.Code
                    .ToLower()
                    .Contains(term) ||
                x.AccountingAccount.Name
                    .ToLower()
                    .Contains(term) ||
                (x.Description != null &&
                 x.Description.ToLower().Contains(term)) ||
                (x.CurrentAccount != null &&
                 x.CurrentAccount.Title
                     .ToLower()
                     .Contains(term)) ||
                (x.Project != null &&
                 (x.Project.Code.ToLower().Contains(term) ||
                  x.Project.Name.ToLower().Contains(term))) ||
                (x.DocumentNumber != null &&
                 x.DocumentNumber.ToLower().Contains(term)));
        }

        return query;
    }

    private static IQueryable<ReportRow> Project(
        IQueryable<AccountingVoucherLine> query)
    {
        return query.Select(x => new ReportRow(
            x.AccountingVoucherId,
            x.AccountingVoucher.VoucherDate,
            x.AccountingVoucher.VoucherNumber,
            (int)x.AccountingVoucher.VoucherType,
            x.AccountingVoucher.Description,
            x.AccountingVoucher.ReferenceNumber,
            x.AccountingVoucher.SourceModule,
            x.LineNumber,
            x.AccountingAccountId,
            x.AccountingAccount.Code,
            x.AccountingAccount.Name,
            x.Description,
            x.CurrentAccountId,
            x.CurrentAccount != null
                ? x.CurrentAccount.Code
                : null,
            x.CurrentAccount != null
                ? x.CurrentAccount.Title
                : null,
            x.ProjectId,
            x.Project != null
                ? x.Project.Code
                : null,
            x.Project != null
                ? x.Project.Name
                : null,
            x.CostCenterCode,
            x.DocumentNumber,
            x.DocumentDate,
            x.DueDate,
            x.CurrencyCode,
            x.ExchangeRate,
            x.DebitAmount,
            x.CreditAmount,
            x.DebitAmountLocal,
            x.CreditAmountLocal));
    }

    private static DateTime UtcDate(DateTime value)
    {
        return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    }
}
