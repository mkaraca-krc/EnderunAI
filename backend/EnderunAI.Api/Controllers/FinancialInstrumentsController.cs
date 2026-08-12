using EnderunAI.Api.Data;
using EnderunAI.Api.Models.FinancialInstruments;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.FinancialInstruments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record SaveBankLoanRequest(
    Guid CompanyId,
    string Name,
    string? ContractNumber,
    Guid? BankCurrentAccountId,
    Guid? CashAccountId,
    Guid? ProjectId,
    decimal PrincipalAmount,
    decimal MonthlyInterestRate,
    int InstallmentCount,
    DateTime DrawdownDate,
    DateTime FirstInstallmentDate,
    string? Notes);

public sealed record UpdateInstallmentRequest(
    decimal PrincipalAmount,
    decimal InterestAmount,
    DateTime DueDate,
    bool IsPaid,
    DateTime? PaidDate);

public sealed record SaveCreditCardRequest(
    Guid CompanyId,
    string Name,
    string? BankName,
    string? LastFourDigits,
    CreditCardOwnership Ownership,
    Guid? PartnerAccountId,
    Guid? CashAccountId,
    int StatementDay,
    int DueDay,
    bool IsActive);

/// <summary>
/// Finansal araçlar: banka kredisi ve kredi kartı.
///
/// YETKİ: mevcut <c>finance.view</c> / <c>finance.edit</c>. Yeni bir
/// anahtar açılmadı — bugün kimsenin sormadığı bir kapıyı ikiye
/// bölmek olurdu. Barter kendi yerinde (<c>hakedis.*</c>) kalıyor.
/// </summary>
[ApiController]
[Authorize]
[Route("api/finansal-araclar")]
public sealed class FinancialInstrumentsController(
    AppDbContext db,
    BankLoanService loans,
    CreditCardService cards,
    FinancialInstrumentSummaryService summary) : ControllerBase
{
    /// <summary>
    /// Kredi, kart ve barter özeti.
    ///
    /// Rakamlar araçların nakit akışa verdiği satırlardan OKUNUYOR;
    /// taksit planı, ekstre dönemi ve barter mahsubu kuralları burada
    /// tekrarlanmıyor. Aksi hâlde aynı taksit nakit akış takviminde
    /// bir tutarla, özette başkasıyla görünebilirdi.
    /// </summary>
    [HttpGet("ozet")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid companyId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        // Varsayılan pencere: bugünden altı ay. Nakit akış
        // projeksiyonunun varsayılanıyla aynı ki iki ekran aynı
        // dönemi anlatsın.
        var start = from ?? DateTime.UtcNow.Date;
        var end = to ?? start.AddMonths(6);

        if (end < start)
            return BadRequest(new { message = "Bitiş tarihi başlangıçtan önce olamaz." });

        return Ok(await summary.GetAsync(
            companyId,
            DateTime.SpecifyKind(start, DateTimeKind.Utc),
            DateTime.SpecifyKind(end, DateTimeKind.Utc),
            cancellationToken));
    }

    // ---------------- Banka kredisi ----------------

    [HttpGet("krediler")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> ListLoans(
        [FromQuery] Guid companyId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        var rows = await db.BankLoans
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.DrawdownDate)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                contractNumber = x.ContractNumber,
                status = x.Status.ToString(),
                principalAmount = x.PrincipalAmount,
                monthlyInterestRate = x.MonthlyInterestRate,
                installmentCount = x.InstallmentCount,
                drawdownDate = x.DrawdownDate,
                firstInstallmentDate = x.FirstInstallmentDate,
                isDrawn = x.IsDrawn,
                projectId = x.ProjectId,
                projectName = x.Project != null ? x.Project.Name : null,
                // Kalan borç = ödenmemiş taksitlerin anaparası. Faiz
                // borç değil, gelecekteki gider.
                remainingPrincipal = x.Installments
                    .Where(i => !i.IsPaid)
                    .Sum(i => (decimal?)i.PrincipalAmount) ?? 0m,
                paidCount = x.Installments.Count(i => i.IsPaid)
            })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpGet("krediler/{id:guid}/taksitler")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> ListInstallments(
        Guid id, CancellationToken cancellationToken)
    {
        var rows = await db.BankLoanInstallments
            .AsNoTracking()
            .Where(x => x.BankLoanId == id)
            .OrderBy(x => x.Number)
            .Select(x => new
            {
                id = x.Id,
                number = x.Number,
                dueDate = x.DueDate,
                principalAmount = x.PrincipalAmount,
                interestAmount = x.InterestAmount,
                totalAmount = x.PrincipalAmount + x.InterestAmount,
                isPaid = x.IsPaid,
                paidDate = x.PaidDate
            })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpPost("krediler")]
    [RequirePermission(PermissionCatalog.Keys.FinanceEdit)]
    public async Task<IActionResult> CreateLoan(
        [FromBody] SaveBankLoanRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        var name = (request.Name ?? string.Empty).Trim();

        if (name.Length == 0)
            return BadRequest(new { message = "Kredi adı zorunludur." });

        if (request.PrincipalAmount <= 0m)
            return BadRequest(new { message = "Anapara sıfırdan büyük olmalıdır." });

        // ÜST SINIR: 600 taksit (50 yıl) üzeri bir plan, gözden
        // geçirilmeyen bir varsayıma dönüşür ve tabloyu şişirir.
        if (request.InstallmentCount is < 1 or > 600)
            return BadRequest(new { message = "Taksit sayısı 1-600 arasında olmalıdır." });

        if (request.MonthlyInterestRate < 0m)
            return BadRequest(new { message = "Faiz oranı negatif olamaz." });

        var loan = new BankLoan
        {
            CompanyId = request.CompanyId,
            Name = name,
            ContractNumber = string.IsNullOrWhiteSpace(request.ContractNumber)
                ? null
                : request.ContractNumber.Trim(),
            BankCurrentAccountId = request.BankCurrentAccountId,
            CashAccountId = request.CashAccountId,
            ProjectId = request.ProjectId,
            PrincipalAmount = decimal.Round(request.PrincipalAmount, 2),
            MonthlyInterestRate = request.MonthlyInterestRate,
            InstallmentCount = request.InstallmentCount,
            DrawdownDate = Services.Expenses.ExpenseEntryService.AsUtcDate(
                request.DrawdownDate),
            FirstInstallmentDate = Services.Expenses.ExpenseEntryService.AsUtcDate(
                request.FirstInstallmentDate),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };

        db.BankLoans.Add(loan);
        await db.SaveChangesAsync(cancellationToken);

        // Plan otomatik üretiliyor; kullanıcı sonra satır satır
        // düzeltebilir.
        var error = await loans.RebuildScheduleAsync(loan.Id, cancellationToken);

        if (error is not null)
            return BadRequest(new { message = error });

        return Ok(new { id = loan.Id });
    }

    /// <summary>
    /// Planı yeniden üretir. Ödenmiş taksit varsa üretmez — ödenmiş
    /// bir taksitin tutarını değiştirmek geçmişi değiştirmek olurdu.
    /// </summary>
    [HttpPost("krediler/{id:guid}/plan-yenile")]
    [RequirePermission(PermissionCatalog.Keys.FinanceEdit)]
    public async Task<IActionResult> RebuildSchedule(
        Guid id, CancellationToken cancellationToken)
    {
        var error = await loans.RebuildScheduleAsync(id, cancellationToken);

        return error is null
            ? Ok(new { id })
            : BadRequest(new { message = error });
    }

    /// <summary>
    /// Tek taksiti düzeltir. Bankanın uyguladığı yuvarlama ya da
    /// komisyon hesabımıza birebir uymayabilir; plan dokunulmaz
    /// olsaydı kullanıcı gerçeği yazamazdı.
    /// </summary>
    [HttpPut("taksitler/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.FinanceEdit)]
    public async Task<IActionResult> UpdateInstallment(
        Guid id,
        [FromBody] UpdateInstallmentRequest request,
        CancellationToken cancellationToken)
    {
        var installment = await db.BankLoanInstallments
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (installment is null)
            return NotFound(new { message = "Taksit bulunamadı." });

        if (request.PrincipalAmount < 0m || request.InterestAmount < 0m)
            return BadRequest(new { message = "Tutarlar negatif olamaz." });

        installment.PrincipalAmount = decimal.Round(request.PrincipalAmount, 2);
        installment.InterestAmount = decimal.Round(request.InterestAmount, 2);
        installment.DueDate = Services.Expenses.ExpenseEntryService.AsUtcDate(
            request.DueDate);
        installment.IsPaid = request.IsPaid;
        installment.PaidDate = request.PaidDate is DateTime paid
            ? Services.Expenses.ExpenseEntryService.AsUtcDate(paid)
            : null;
        installment.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id });
    }

    /// <summary>
    /// Kredinin durumunu değiştirir. İPTAL edilen kredi nakit akışta
    /// ne çekiliş ne taksit üretir — kapatılan bir kaydın mali etkisi
    /// de kalkmalı.
    /// </summary>
    [HttpPost("krediler/{id:guid}/durum")]
    [RequirePermission(PermissionCatalog.Keys.FinanceEdit)]
    public async Task<IActionResult> UpdateLoanStatus(
        Guid id,
        [FromQuery] BankLoanStatus status,
        [FromQuery] bool? isDrawn,
        CancellationToken cancellationToken)
    {
        var loan = await db.BankLoans
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (loan is null)
            return NotFound(new { message = "Kredi bulunamadı." });

        loan.Status = status;

        if (isDrawn is bool drawn)
            loan.IsDrawn = drawn;

        loan.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id, status = loan.Status.ToString(), isDrawn = loan.IsDrawn });
    }

    // ---------------- Kredi kartı ----------------

    [HttpGet("kartlar")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> ListCards(
        [FromQuery] Guid companyId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        var rows = await db.CreditCards
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                bankName = x.BankName,
                lastFourDigits = x.LastFourDigits,
                ownership = x.Ownership.ToString(),
                partnerAccountId = x.PartnerAccountId,
                partnerName = x.PartnerAccount != null ? x.PartnerAccount.FullName : null,
                statementDay = x.StatementDay,
                dueDay = x.DueDay,
                isActive = x.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    /// <summary>
    /// Ekstre dönemleri. Şirket kartlarının ekstresi nakit çıkışıdır;
    /// şahıs kartınınki değildir — ikisi de listeleniyor ama
    /// <c>producesCashOutflow</c> ayrımı taşınıyor.
    /// </summary>
    [HttpGet("kartlar/ekstreler")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> ListStatements(
        [FromQuery] Guid companyId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        var today = DateTime.UtcNow.Date;

        var start = Services.Expenses.ExpenseEntryService.AsUtcDate(
            from ?? today.AddMonths(-3));

        var end = Services.Expenses.ExpenseEntryService.AsUtcDate(
            to ?? today.AddMonths(1));

        if (end < start)
            return BadRequest(new { message = "Bitiş tarihi başlangıçtan önce olamaz." });

        var statements = await cards.GetStatementsAsync(
            companyId, start, end, includePersonal: true, cancellationToken);

        var ownership = await db.CreditCards
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToDictionaryAsync(x => x.Id, x => x.Ownership, cancellationToken);

        return Ok(statements.Select(x => new
        {
            creditCardId = x.CreditCardId,
            cardName = x.CardName,
            periodStart = x.PeriodStart,
            periodEnd = x.PeriodEnd,
            dueDate = x.DueDate,
            amount = x.Amount,
            itemCount = x.ItemCount,
            producesCashOutflow =
                ownership.GetValueOrDefault(x.CreditCardId) ==
                CreditCardOwnership.Company
        }));
    }

    [HttpPost("kartlar")]
    [RequirePermission(PermissionCatalog.Keys.FinanceEdit)]
    public async Task<IActionResult> CreateCard(
        [FromBody] SaveCreditCardRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        var name = (request.Name ?? string.Empty).Trim();

        if (name.Length == 0)
            return BadRequest(new { message = "Kart adı zorunludur." });

        if (request.StatementDay is < 1 or > 31 || request.DueDay is < 1 or > 31)
            return BadRequest(new
            {
                message = "Kesim ve son ödeme günü 1-31 arasında olmalıdır."
            });

        // Şahıs kartında sahibi ZORUNLU: harcama onun carisine
        // yazılacak, sahibi yoksa hiçbir bakiyeye düşmez.
        if (request.Ownership == CreditCardOwnership.Personal)
        {
            if (request.PartnerAccountId is not Guid partnerId)
                return BadRequest(new
                {
                    message = "Şahıs kartında kart sahibi seçilmelidir."
                });

            var partnerExists = await db.PartnerAccounts
                .AnyAsync(x => x.Id == partnerId && x.CompanyId == request.CompanyId,
                    cancellationToken);

            if (!partnerExists)
                return BadRequest(new { message = "Şahıs carisi bulunamadı." });
        }

        var card = new CreditCard
        {
            CompanyId = request.CompanyId,
            Name = name,
            BankName = string.IsNullOrWhiteSpace(request.BankName)
                ? null
                : request.BankName.Trim(),
            LastFourDigits = string.IsNullOrWhiteSpace(request.LastFourDigits)
                ? null
                : request.LastFourDigits.Trim(),
            Ownership = request.Ownership,
            PartnerAccountId = request.Ownership == CreditCardOwnership.Personal
                ? request.PartnerAccountId
                : null,
            CashAccountId = request.CashAccountId,
            StatementDay = request.StatementDay,
            DueDay = request.DueDay,
            IsActive = request.IsActive
        };

        db.CreditCards.Add(card);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id = card.Id });
    }
}
