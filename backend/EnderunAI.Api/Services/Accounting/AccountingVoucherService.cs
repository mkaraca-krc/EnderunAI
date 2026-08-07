using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.EntityFrameworkCore;
using EnderunAI.Api.Formatting;

namespace EnderunAI.Api.Services.Accounting;

public sealed class AccountingVoucherService(
    AppDbContext dbContext,
    IDocumentNumberService documentNumberService)
    : IAccountingVoucherService
{
    public async Task<IReadOnlyCollection<AccountingVoucherListItemResponse>>
        GetAllAsync(
            Guid? companyId,
            int? status,
            int? voucherType,
            DateTime? startDate,
            DateTime? endDate,
            string? search,
            CancellationToken cancellationToken)
    {
        var query = dbContext.AccountingVouchers
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (companyId.HasValue)
        {
            query = query.Where(x => x.CompanyId == companyId.Value);
        }

        if (status.HasValue)
        {
            ValidateStatus(status.Value);
            query = query.Where(
                x => x.Status == (AccountingVoucherStatus)status.Value);
        }

        if (voucherType.HasValue)
        {
            ValidateVoucherType(voucherType.Value);
            query = query.Where(
                x => x.VoucherType == (AccountingVoucherType)voucherType.Value);
        }

        if (startDate.HasValue)
        {
            query = query.Where(
                x => x.VoucherDate >= UtcDate(startDate.Value));
        }

        if (endDate.HasValue)
        {
            var exclusiveEnd = UtcDate(endDate.Value).AddDays(1);
            query = query.Where(x => x.VoucherDate < exclusiveEnd);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim().ToLower();

            query = query.Where(x =>
                x.VoucherNumber.ToLower().Contains(normalized) ||
                (x.Description != null &&
                 x.Description.ToLower().Contains(normalized)) ||
                (x.ReferenceNumber != null &&
                 x.ReferenceNumber.ToLower().Contains(normalized)));
        }

        return await query
            .OrderByDescending(x => x.VoucherDate)
            .ThenByDescending(x => x.VoucherNumber)
            .Select(x => new AccountingVoucherListItemResponse(
                x.Id,
                x.CompanyId,
                x.VoucherNumber,
                (int)x.VoucherType,
                (int)x.Status,
                x.VoucherDate,
                x.FiscalYear,
                x.FiscalPeriod,
                x.CurrencyCode,
                x.ExchangeRate,
                x.Description,
                x.ReferenceNumber,
                x.SourceModule,
                x.TotalDebit,
                x.TotalCredit,
                x.Lines.Count(y => !y.IsDeleted)))
            .ToListAsync(cancellationToken);
    }

    public async Task<AccountingVoucherDetailResponse> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var voucher = await LoadVoucherAsync(id, false, cancellationToken);

        if (voucher is null)
        {
            throw new KeyNotFoundException(
                "Muhasebe fişi bulunamadı.");
        }

        return MapDetail(voucher);
    }

    public async Task<AccountingVoucherDetailResponse> CreateAsync(
        CreateAccountingVoucherRequest request,
        CancellationToken cancellationToken)
    {
        ValidateVoucherType(request.VoucherType);
        ValidateHeader(
            request.CompanyId,
            request.VoucherDate,
            request.CurrencyCode,
            request.ExchangeRate);

        await ValidateCompanyAsync(
            request.CompanyId,
            cancellationToken);


        var validatedLines = await ValidateAndPrepareLinesAsync(
            request.CompanyId,
            request.CurrencyCode,
            request.ExchangeRate,
            request.Lines,
            cancellationToken);

        var voucherType =
            (AccountingVoucherType)request.VoucherType;

        var voucherNumber =
            await documentNumberService.GenerateAsync(
                request.CompanyId,
                "ACCOUNTING_VOUCHER",
                GetPrefix(voucherType),
                cancellationToken);

        var voucher = new AccountingVoucher
        {
            CompanyId = request.CompanyId,
            VoucherNumber = voucherNumber,
            VoucherType = voucherType,
            Status = AccountingVoucherStatus.Draft,
            VoucherDate = UtcDate(request.VoucherDate),
            FiscalYear = request.VoucherDate.Year,
            FiscalPeriod = request.VoucherDate.Month,
            CurrencyCode = NormalizeCurrency(
                request.CurrencyCode),
            ExchangeRate = request.ExchangeRate,
            Description = NormalizeNullable(
                request.Description),
            ReferenceNumber = NormalizeNullable(
                request.ReferenceNumber),
            SourceModule = NormalizeNullable(
                request.SourceModule),
            SourceEntityId = request.SourceEntityId
        };

        ApplyLinesAndTotals(voucher, validatedLines);

        dbContext.AccountingVouchers.Add(voucher);
        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(
            voucher.Id,
            cancellationToken);
    }

    public async Task<AccountingVoucherDetailResponse> UpdateAsync(
        Guid id,
        UpdateAccountingVoucherRequest request,
        CancellationToken cancellationToken)
    {
        ValidateVoucherType(request.VoucherType);

        var voucher = await LoadVoucherAsync(
            id,
            true,
            cancellationToken);

        if (voucher is null)
        {
            throw new KeyNotFoundException(
                "Muhasebe fişi bulunamadı.");
        }

        if (voucher.Status != AccountingVoucherStatus.Draft)
        {
            throw new InvalidOperationException(
                "Yalnızca taslak muhasebe fişleri güncellenebilir.");
        }

        ValidateHeader(
            voucher.CompanyId,
            request.VoucherDate,
            request.CurrencyCode,
            request.ExchangeRate);


        var validatedLines = await ValidateAndPrepareLinesAsync(
            voucher.CompanyId,
            request.CurrencyCode,
            request.ExchangeRate,
            request.Lines,
            cancellationToken);

        dbContext.AccountingVoucherLines.RemoveRange(
            voucher.Lines);

        voucher.Lines.Clear();
        voucher.VoucherType =
            (AccountingVoucherType)request.VoucherType;
        voucher.VoucherDate = UtcDate(request.VoucherDate);
        voucher.FiscalYear = request.VoucherDate.Year;
        voucher.FiscalPeriod = request.VoucherDate.Month;
        voucher.CurrencyCode = NormalizeCurrency(
            request.CurrencyCode);
        voucher.ExchangeRate = request.ExchangeRate;
        voucher.Description = NormalizeNullable(
            request.Description);
        voucher.ReferenceNumber = NormalizeNullable(
            request.ReferenceNumber);
        voucher.UpdatedAtUtc = DateTime.UtcNow;

        ApplyLinesAndTotals(voucher, validatedLines);

        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(
            voucher.Id,
            cancellationToken);
    }

    public async Task<AccountingVoucherActionResponse> PostAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var voucher = await LoadVoucherAsync(
            id,
            true,
            cancellationToken);

        if (voucher is null)
        {
            throw new KeyNotFoundException(
                "Muhasebe fişi bulunamadı.");
        }

        if (voucher.Status != AccountingVoucherStatus.Draft)
        {
            throw new InvalidOperationException(
                "Yalnızca taslak fişler kesinleştirilebilir.");
        }


        if (voucher.Lines.Count < 2)
        {
            throw new InvalidOperationException(
                "Muhasebe fişinde en az iki satır bulunmalıdır.");
        }

        RecalculateTotals(voucher);

        if (voucher.TotalDebit <= 0 ||
            voucher.TotalCredit <= 0)
        {
            throw new InvalidOperationException(
                "Fişin borç ve alacak toplamları sıfırdan büyük olmalıdır.");
        }

        if (decimal.Round(voucher.TotalDebit, 2) !=
            decimal.Round(voucher.TotalCredit, 2))
        {
            throw new InvalidOperationException(
                $"Fiş dengeli değil. Borç: {TurkishFormat.Amount(voucher.TotalDebit)}, Alacak: {TurkishFormat.Amount(voucher.TotalCredit)}");
        }

        voucher.Status = AccountingVoucherStatus.Posted;
        voucher.PostedAtUtc = DateTime.UtcNow;
        voucher.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AccountingVoucherActionResponse(
            voucher.Id,
            voucher.VoucherNumber,
            (int)voucher.Status,
            "Muhasebe fişi kesinleştirildi.");
    }

    public async Task<AccountingVoucherActionResponse> CancelAsync(
        Guid id,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "İptal gerekçesi zorunludur.");
        }

        var voucher = await dbContext.AccountingVouchers
            .FirstOrDefaultAsync(
                x => x.Id == id && !x.IsDeleted,
                cancellationToken);

        if (voucher is null)
        {
            throw new KeyNotFoundException(
                "Muhasebe fişi bulunamadı.");
        }

        if (voucher.Status == AccountingVoucherStatus.Cancelled)
        {
            throw new InvalidOperationException(
                "Muhasebe fişi zaten iptal edilmiş.");
        }

        voucher.Status = AccountingVoucherStatus.Cancelled;
        voucher.CancellationReason = reason.Trim();
        voucher.CancelledAtUtc = DateTime.UtcNow;
        voucher.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AccountingVoucherActionResponse(
            voucher.Id,
            voucher.VoucherNumber,
            (int)voucher.Status,
            "Muhasebe fişi iptal edildi.");
    }

    private async Task<List<AccountingVoucherLine>>
        ValidateAndPrepareLinesAsync(
            Guid companyId,
            string voucherCurrency,
            decimal voucherExchangeRate,
            IReadOnlyCollection<AccountingVoucherLineRequest> requests,
            CancellationToken cancellationToken)
    {
        if (requests is null || requests.Count == 0)
        {
            throw new ArgumentException(
                "Muhasebe fişinde en az bir satır bulunmalıdır.");
        }

        var accountIds = requests
            .Select(x => x.AccountingAccountId)
            .Distinct()
            .ToArray();

        var accounts = await dbContext.AccountingAccounts
            .Where(x =>
                accountIds.Contains(x.Id) &&
                x.CompanyId == companyId &&
                x.IsActive &&
                !x.IsDeleted)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (accounts.Count != accountIds.Length)
        {
            throw new ArgumentException(
                "Fiş satırlarından bir veya daha fazlasındaki hesap bulunamadı.");
        }

        var projectIds = requests
            .Where(x => x.ProjectId.HasValue)
            .Select(x => x.ProjectId!.Value)
            .Distinct()
            .ToArray();

        if (projectIds.Length > 0)
        {
            var projectCount = await dbContext.Projects
                .CountAsync(
                    x =>
                        projectIds.Contains(x.Id) &&
                        x.CompanyId == companyId &&
                        !x.IsDeleted,
                    cancellationToken);

            if (projectCount != projectIds.Length)
            {
                throw new ArgumentException(
                    "Fiş satırlarındaki projelerden biri bulunamadı veya farklı şirkete ait.");
            }
        }

        var currentAccountIds = requests
            .Where(x => x.CurrentAccountId.HasValue)
            .Select(x => x.CurrentAccountId!.Value)
            .Distinct()
            .ToArray();

        if (currentAccountIds.Length > 0)
        {
            var currentAccountCount =
                await dbContext.CurrentAccounts.CountAsync(
                    x =>
                        currentAccountIds.Contains(x.Id) &&
                        x.CompanyId == companyId &&
                        !x.IsDeleted,
                    cancellationToken);

            if (currentAccountCount != currentAccountIds.Length)
            {
                throw new ArgumentException(
                    "Fiş satırlarındaki cari hesaplardan biri bulunamadı veya farklı şirkete ait.");
            }
        }

        var result = new List<AccountingVoucherLine>();
        var lineNumber = 1;

        foreach (var request in requests)
        {
            var account = accounts[request.AccountingAccountId];

            if (!account.IsPostingAllowed)
            {
                throw new InvalidOperationException(
                    $"{account.Code} - {account.Name} grup hesabıdır; fiş kaydı yapılamaz.");
            }

            ValidateAmounts(
                request.DebitAmount,
                request.CreditAmount,
                lineNumber);

            if (account.RequiresProject &&
                !request.ProjectId.HasValue)
            {
                throw new ArgumentException(
                    $"{account.Code} hesabında proje seçimi zorunludur.");
            }

            if (account.RequiresCostCenter &&
                string.IsNullOrWhiteSpace(
                    request.CostCenterCode))
            {
                throw new ArgumentException(
                    $"{account.Code} hesabında masraf merkezi zorunludur.");
            }

            var currency = string.IsNullOrWhiteSpace(
                request.CurrencyCode)
                ? NormalizeCurrency(voucherCurrency)
                : NormalizeCurrency(request.CurrencyCode);

            var exchangeRate = request.ExchangeRate > 0
                ? request.ExchangeRate
                : voucherExchangeRate;

            if (exchangeRate <= 0)
            {
                throw new ArgumentException(
                    $"{lineNumber}. satırda döviz kuru sıfırdan büyük olmalıdır.");
            }

            result.Add(new AccountingVoucherLine
            {
                AccountingAccountId =
                    request.AccountingAccountId,
                LineNumber = lineNumber,
                Description = NormalizeNullable(
                    request.Description),
                DebitAmount = request.DebitAmount,
                CreditAmount = request.CreditAmount,
                CurrencyCode = currency,
                ExchangeRate = exchangeRate,
                DebitAmountLocal = decimal.Round(
                    request.DebitAmount * exchangeRate,
                    2),
                CreditAmountLocal = decimal.Round(
                    request.CreditAmount * exchangeRate,
                    2),
                CurrentAccountId =
                    request.CurrentAccountId,
                ProjectId = request.ProjectId,
                CostCenterCode = NormalizeNullable(
                    request.CostCenterCode),
                DocumentNumber = NormalizeNullable(
                    request.DocumentNumber),
                DocumentDate = UtcDate(request.DocumentDate),
                DueDate = UtcDate(request.DueDate)
            });

            lineNumber++;
        }

        return result;
    }

    private static void ApplyLinesAndTotals(
        AccountingVoucher voucher,
        IEnumerable<AccountingVoucherLine> lines)
    {
        foreach (var line in lines)
        {
            voucher.Lines.Add(line);
        }

        RecalculateTotals(voucher);

    }

    private static void RecalculateTotals(AccountingVoucher voucher)
    {
        voucher.TotalDebit = decimal.Round(
            voucher.Lines
                .Where(x => !x.IsDeleted)
                .Sum(x => x.DebitAmountLocal),
            2);

        voucher.TotalCredit = decimal.Round(
            voucher.Lines
                .Where(x => !x.IsDeleted)
                .Sum(x => x.CreditAmountLocal),
            2);
    }

    private async Task<AccountingVoucher?> LoadVoucherAsync(
        Guid id,
        bool tracking,
        CancellationToken cancellationToken)
    {
        IQueryable<AccountingVoucher> query =
            dbContext.AccountingVouchers
                .Include(x => x.Lines.Where(line => !line.IsDeleted))
                    .ThenInclude(line => line.AccountingAccount)
                .Include(x => x.Lines.Where(line => !line.IsDeleted))
                    .ThenInclude(line => line.CurrentAccount)
                .Include(x => x.Lines.Where(line => !line.IsDeleted))
                    .ThenInclude(line => line.Project)
                .Where(x => x.Id == id && !x.IsDeleted);

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(cancellationToken);
    }

    private static AccountingVoucherDetailResponse MapDetail(
        AccountingVoucher voucher)
    {
        return new AccountingVoucherDetailResponse(
            voucher.Id,
            voucher.CompanyId,
            voucher.VoucherNumber,
            (int)voucher.VoucherType,
            (int)voucher.Status,
            voucher.VoucherDate,
            voucher.FiscalYear,
            voucher.FiscalPeriod,
            voucher.CurrencyCode,
            voucher.ExchangeRate,
            voucher.Description,
            voucher.ReferenceNumber,
            voucher.SourceModule,
            voucher.SourceEntityId,
            voucher.TotalDebit,
            voucher.TotalCredit,
            voucher.PostedAtUtc,
            voucher.CancelledAtUtc,
            voucher.CancellationReason,
            voucher.Lines
                .Where(x => !x.IsDeleted)
                .OrderBy(x => x.LineNumber)
                .Select(x => new AccountingVoucherLineResponse(
                    x.Id,
                    x.LineNumber,
                    x.AccountingAccountId,
                    x.AccountingAccount.Code,
                    x.AccountingAccount.Name,
                    x.Description,
                    x.DebitAmount,
                    x.CreditAmount,
                    x.CurrencyCode,
                    x.ExchangeRate,
                    x.DebitAmountLocal,
                    x.CreditAmountLocal,
                    x.CurrentAccountId,
                    x.CurrentAccount?.Title,
                    x.ProjectId,
                    x.Project?.Code,
                    x.Project?.Name,
                    x.CostCenterCode,
                    x.DocumentNumber,
                    x.DocumentDate,
                    x.DueDate))
                .ToList());
    }

    private async Task ValidateCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.Companies.AnyAsync(
            x => x.Id == companyId && x.IsActive && !x.IsDeleted,
            cancellationToken);

        if (!exists)
        {
            throw new ArgumentException("Geçerli bir şirket seçilmelidir.");
        }
    }

    private static void ValidateHeader(
        Guid companyId,
        DateTime voucherDate,
        string currencyCode,
        decimal exchangeRate)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Şirket seçimi zorunludur.");
        }

        if (voucherDate == default)
        {
            throw new ArgumentException("Fiş tarihi zorunludur.");
        }

        _ = NormalizeCurrency(currencyCode);

        if (exchangeRate <= 0)
        {
            throw new ArgumentException(
                "Döviz kuru sıfırdan büyük olmalıdır.");
        }
    }

    private static void ValidateAmounts(
        decimal debitAmount,
        decimal creditAmount,
        int lineNumber)
    {
        if (debitAmount < 0 || creditAmount < 0)
        {
            throw new ArgumentException(
                $"{lineNumber}. satırda tutarlar negatif olamaz.");
        }

        if (debitAmount > 0 && creditAmount > 0)
        {
            throw new ArgumentException(
                $"{lineNumber}. satırda aynı anda borç ve alacak yazılamaz.");
        }

        if (debitAmount == 0 && creditAmount == 0)
        {
            throw new ArgumentException(
                $"{lineNumber}. satırda borç veya alacak tutarı zorunludur.");
        }
    }

    private static void ValidateVoucherType(int value)
    {
        if (!Enum.IsDefined(typeof(AccountingVoucherType), value))
        {
            throw new ArgumentException("Geçersiz fiş türü.");
        }
    }

    private static void ValidateStatus(int value)
    {
        if (!Enum.IsDefined(typeof(AccountingVoucherStatus), value))
        {
            throw new ArgumentException("Geçersiz fiş durumu.");
        }
    }

    private static string GetPrefix(AccountingVoucherType type)
    {
        return type switch
        {
            AccountingVoucherType.Collection => "THS",
            AccountingVoucherType.Payment => "TDI",
            AccountingVoucherType.Opening => "ACL",
            AccountingVoucherType.Closing => "KPN",
            _ => "MHS"
        };
    }

    private static string NormalizeCurrency(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "TRY"
            : value.Trim().ToUpperInvariant();

        if (normalized.Length != 3)
        {
            throw new ArgumentException(
                "Para birimi kodu 3 karakter olmalıdır.");
        }

        return normalized;
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static DateTime UtcDate(DateTime value)
    {
        return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    }

    private static DateTime? UtcDate(DateTime? value)
    {
        return value.HasValue
            ? UtcDate(value.Value)
            : null;
    }
}
