using EnderunAI.Api.Contracts.Core;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Accounting;
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
    /// Cari kartları muhasebe alt hesaplarıyla eşleştirir: tedarikçiler
    /// 320.x, müşteriler 120.x altında UNVAN birebir aynı olan hesaba
    /// bağlanır. Kod eşleştirmesi bilinçli olarak kullanılmıyor — canlıda
    /// cari kodu ile hesap kodu çakışıyor ama farklı firmaları gösteriyor
    /// (cari 120.001 ≠ hesap 120.001), kod bazlı eşleme yanlış firmanın
    /// hesabına yazardı. Aynı unvanda birden fazla hesap varsa o cari
    /// atlanır (belirsiz eşleşme yapılmaz). Mevcut eşleşmeler asla
    /// ezilmez; eşleşmeyenler 320/120 ana hesabına cari boyutuyla
    /// yazılmaya devam eder.
    /// </summary>
    [HttpPost("synchronize-accounting")]
    [RequirePermission(PermissionCatalog.Keys.CurrentAccountsEdit)]
    public async Task<IActionResult> SynchronizeAccounting(
        [FromQuery] Guid companyId,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçilmelidir." });

        var accounts = await db.CurrentAccounts
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        if (accounts.Count == 0)
            return NotFound(new { message = "Bu şirkette cari kart bulunamadı." });

        var candidates = await db.AccountingAccounts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.IsActive &&
                x.IsPostingAllowed &&
                (x.Code.StartsWith("320.") || x.Code.StartsWith("120.")))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToListAsync(cancellationToken);

        // Aynı unvandan birden fazla hesap varsa hangisine yazılacağı
        // belirsiz — o unvan tamamen dışarıda bırakılır.
        static Dictionary<string, Guid> UniqueByName(
            IEnumerable<(string Name, Guid Id)> source) =>
            source
                .GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Select(x => x.Id).Distinct().Count() == 1)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var payableByName = UniqueByName(candidates
            .Where(x => x.Code.StartsWith("320.", StringComparison.Ordinal))
            .Select(x => (x.Name, x.Id)));

        var receivableByName = UniqueByName(candidates
            .Where(x => x.Code.StartsWith("120.", StringComparison.Ordinal))
            .Select(x => (x.Name, x.Id)));

        var matchedPayable = 0;
        var matchedReceivable = 0;
        var alreadyMapped = 0;
        var unmatched = 0;

        foreach (var account in accounts)
        {
            var title = account.Title.Trim();
            var isSupplier = account.Roles.HasFlag(CurrentAccountRoles.Supplier) ||
                             account.Roles.HasFlag(CurrentAccountRoles.Subcontractor);
            var isCustomer = account.Roles.HasFlag(CurrentAccountRoles.Customer);
            var touched = false;

            if (isSupplier)
            {
                if (account.PayableAccountingAccountId is not null)
                {
                    alreadyMapped++;
                    touched = true;
                }
                else if (payableByName.TryGetValue(title, out var payableId))
                {
                    account.PayableAccountingAccountId = payableId;
                    matchedPayable++;
                    touched = true;
                }
            }

            if (isCustomer)
            {
                if (account.ReceivableAccountingAccountId is not null)
                {
                    alreadyMapped++;
                    touched = true;
                }
                else if (receivableByName.TryGetValue(title, out var receivableId))
                {
                    account.ReceivableAccountingAccountId = receivableId;
                    matchedReceivable++;
                    touched = true;
                }
            }

            if (!touched)
                unmatched++;
        }

        var newlyMatched = matchedPayable + matchedReceivable;
        if (newlyMatched > 0)
            await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message =
                $"{newlyMatched} cari muhasebe hesabıyla eşleştirildi " +
                $"({matchedPayable} satıcı, {matchedReceivable} alıcı). " +
                $"{alreadyMapped} kart zaten eşliydi, {unmatched} kart için birebir unvan eşleşmesi bulunamadı " +
                "— bunlar 320/120 ana hesabına cari boyutuyla yazılmaya devam eder, cari kartından elle de eşlenebilir.",
            matchedPayable,
            matchedReceivable,
            alreadyMapped,
            unmatched
        });
    }

    /// <summary>
    /// Cari bakiyeleri — ayrı bir hareket defteri tutulmaz, tek gerçek
    /// kaynak muhasebe defteridir: kesinleşmiş (Posted) fiş satırlarının
    /// cari boyutu üzerinden hesaplanır. Bakiye = Borç − Alacak
    /// (pozitif: bizden alacaklı değil, bize borçlu → müşteri;
    /// negatif: biz borçluyuz → satıcı).
    ///
    /// <c>balance</c> alanı bugüne kadarki TL bakiyedir ve anlamı
    /// değişmedi. Yanına <c>currencyBalances</c> eklendi: dövizli
    /// carinin "kaç USD borcumuz var" sorusunun cevabı TL toplamdan
    /// okunamıyordu.
    /// </summary>
    [HttpGet("balances")]
    [RequirePermission(PermissionCatalog.Keys.CurrentAccountsView)]
    public async Task<IActionResult> GetBalances(
        [FromQuery] Guid? companyId,
        [FromServices] CurrentAccountCurrencyService currencyService,
        CancellationToken cancellationToken)
    {
        var balances = await currencyService.GetBalancesAsync(
            companyId, currentAccountId: null, cancellationToken);

        return Ok(balances.Select(x => new
        {
            x.CurrentAccountId,
            x.TotalDebit,
            x.TotalCredit,
            x.Balance,
            x.MovementCount,
            x.LastMovementDate,
            x.HasForeignCurrency,
            CurrencyBalances = x.CurrencyBalances.Select(c => new
            {
                c.CurrencyCode,
                c.TotalDebit,
                c.TotalCredit,
                c.Balance,
                c.BalanceLocal,
                c.MovementCount,
                c.LastMovementDate
            })
        }));
    }

    /// <summary>
    /// Carinin döviz bakiyesinin verilen tarihteki kurla değerlemesi.
    ///
    /// Defter değeri (hareketlerin kendi günündeki kurla TL karşılığı)
    /// ile değerleme değeri arasındaki fark, gerçekleşmemiş kur
    /// farkıdır. Burada yalnızca RAPORLANIR — fiş kesilmez.
    /// </summary>
    [HttpGet("{id:guid}/currency-valuation")]
    [RequirePermission(PermissionCatalog.Keys.CurrentAccountsView)]
    public async Task<IActionResult> GetCurrencyValuation(
        Guid id,
        [FromQuery] DateTime? valuationDate,
        [FromServices] CurrentAccountCurrencyService currencyService,
        CancellationToken cancellationToken)
    {
        var date = valuationDate.HasValue
            ? AsUtcDate(valuationDate.Value)
            : DateTime.UtcNow.Date;

        var result = await currencyService.ValuateAsync(id, date, cancellationToken);

        return result is null
            ? NotFound(new { message = "Cari kart bulunamadı." })
            : Ok(result);
    }

    /// <summary>
    /// Cari ekstresi: dönem başı bakiyesi + hareketler + her satırda
    /// yürüyen bakiye. Kaynak muhasebe defteri (yalnızca kesinleşmiş
    /// fişler).
    ///
    /// Ekstre iki bakiyeyi birlikte yürütür: TL (defter) bakiyesi ve
    /// satırın kendi para birimindeki bakiye. Dövizli bir carinin
    /// ekstresinde yalnızca TL yürüyen bakiye vardı; "bu tarihte kaç
    /// USD borçluyduk" sorusu cevapsız kalıyordu.
    /// </summary>
    /// <param name="currency">Tek para birimine indirger (örn. USD).
    /// Boşsa tüm hareketler.</param>
    [HttpGet("{id:guid}/statement")]
    [RequirePermission(PermissionCatalog.Keys.CurrentAccountsView)]
    public async Task<IActionResult> GetStatement(
        Guid id,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? currency,
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

        var currencyFilter = string.IsNullOrWhiteSpace(currency)
            ? null
            : currency.Trim().ToUpperInvariant();

        if (currencyFilter is not null)
            baseQuery = baseQuery.Where(x => x.CurrencyCode.ToUpper() == currencyFilter);

        var openingBalance = 0m;

        // Dönem başı bakiyesi para birimi bazında da tutulur: yürüyen
        // döviz bakiyesi sıfırdan değil, devirden başlamalı.
        var openingByCurrency = new Dictionary<string, (decimal Original, decimal Local)>(
            StringComparer.OrdinalIgnoreCase);

        if (startDate.HasValue)
        {
            var start = AsUtcDate(startDate.Value);

            var openingRows = await baseQuery
                .Where(x => x.AccountingVoucher.VoucherDate < start)
                .GroupBy(x => x.CurrencyCode)
                .Select(g => new
                {
                    CurrencyCode = g.Key,
                    Original = g.Sum(x => x.DebitAmount - x.CreditAmount),
                    Local = g.Sum(x => x.DebitAmountLocal - x.CreditAmountLocal)
                })
                .ToListAsync(cancellationToken);

            foreach (var row in openingRows)
            {
                var code = NormalizeCurrency(row.CurrencyCode);
                openingByCurrency.TryGetValue(code, out var existing);
                openingByCurrency[code] =
                    (existing.Original + row.Original, existing.Local + row.Local);
            }

            openingBalance = openingRows.Sum(x => x.Local);
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
                Credit = x.CreditAmountLocal,
                x.CurrencyCode,
                x.ExchangeRate,
                DebitOriginal = x.DebitAmount,
                CreditOriginal = x.CreditAmount
            })
            .ToListAsync(cancellationToken);

        var running = decimal.Round(openingBalance, 2);
        var lines = new List<object>(rows.Count);

        // Her para birimi kendi yürüyen bakiyesini taşır; TL bakiye
        // dövizli satırlarda da işlemeye devam eder (defter değeri).
        var runningByCurrency = openingByCurrency.ToDictionary(
            x => x.Key, x => decimal.Round(x.Value.Original, 2),
            StringComparer.OrdinalIgnoreCase);

        var periodByCurrency = new Dictionary<string, (
            decimal Debit, decimal Credit, decimal DebitLocal, decimal CreditLocal)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var code = NormalizeCurrency(row.CurrencyCode);

            running = decimal.Round(running + row.Debit - row.Credit, 2);

            runningByCurrency.TryGetValue(code, out var currencyRunning);
            currencyRunning = decimal.Round(
                currencyRunning + row.DebitOriginal - row.CreditOriginal, 2);
            runningByCurrency[code] = currencyRunning;

            periodByCurrency.TryGetValue(code, out var period);
            periodByCurrency[code] = (
                period.Debit + row.DebitOriginal,
                period.Credit + row.CreditOriginal,
                period.DebitLocal + row.Debit,
                period.CreditLocal + row.Credit);

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
                RunningBalance = running,
                CurrencyCode = code,
                row.ExchangeRate,
                row.DebitOriginal,
                row.CreditOriginal,
                RunningBalanceOriginal = currencyRunning
            });
        }

        var periodDebit = decimal.Round(rows.Sum(x => x.Debit), 2);
        var periodCredit = decimal.Round(rows.Sum(x => x.Credit), 2);

        var currencyCodes = openingByCurrency.Keys
            .Concat(periodByCurrency.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x == "TRY" ? 0 : 1)
            .ThenBy(x => x, StringComparer.Ordinal)
            .ToList();

        var currencySummary = currencyCodes.Select(code =>
        {
            openingByCurrency.TryGetValue(code, out var opening);
            periodByCurrency.TryGetValue(code, out var period);
            runningByCurrency.TryGetValue(code, out var closing);

            return new
            {
                currencyCode = code,
                openingBalance = decimal.Round(opening.Original, 2),
                openingBalanceLocal = decimal.Round(opening.Local, 2),
                periodDebit = decimal.Round(period.Debit, 2),
                periodCredit = decimal.Round(period.Credit, 2),
                periodDebitLocal = decimal.Round(period.DebitLocal, 2),
                periodCreditLocal = decimal.Round(period.CreditLocal, 2),
                closingBalance = decimal.Round(closing, 2),
                closingBalanceLocal = decimal.Round(
                    opening.Local + period.DebitLocal - period.CreditLocal, 2)
            };
        }).ToList();

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
            currency = currencyFilter,
            hasForeignCurrency = currencyCodes.Any(x => x != "TRY"),
            currencySummary,
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

    /// <summary>
    /// Para birimi kodunu tek yazıma indirger; boşsa yerel para birimi.
    /// </summary>
    private static string NormalizeCurrency(string? code) =>
        string.IsNullOrWhiteSpace(code) ? "TRY" : code.Trim().ToUpperInvariant();

    private static DateTime AsUtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}
