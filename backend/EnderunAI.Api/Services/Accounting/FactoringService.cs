using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.EntityFrameworkCore;
using EnderunAI.Api.Formatting;

namespace EnderunAI.Api.Services.Accounting;

public interface IFactoringService
{
    Task<IReadOnlyCollection<FactoringTransactionResponse>> GetAllAsync(
        Guid? companyId,
        Guid? projectId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken);

    Task<FactoringTransactionResponse> GetByIdAsync(
        Guid id, CancellationToken cancellationToken);

    FactoringCalculationResponse Preview(FactoringPreviewRequest request);

    Task<FactoringTransactionResponse> CreateAsync(
        CreateFactoringTransactionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Çek kırdırma (faktoring). Kesintiler — komisyon, BSMV, masraf —
/// ayrı ayrı hesaplanır ve fişte ayrı satır olarak 780 Finansman
/// Giderleri'ne yazılır; net tutar seçilen banka hesabına girer ve
/// çek portföyden çıkar.
/// </summary>
public sealed class FactoringService(
    AppDbContext db,
    IAccountingIntegrationService accountingIntegration,
    IDocumentNumberService documentNumberService) : IFactoringService
{
    /// <summary>Yasal BSMV oranı (komisyon üzerinden %5).</summary>
    public const decimal DefaultBsmvRate = 5m;

    /// <summary>
    /// Kesinti matematiği. Komisyon oran ya da tutar olarak verilebilir;
    /// ikisi de verilirse tutar esas alınır (banka dekontundaki gerçek
    /// tutar oranın yuvarlamasından daha güvenilir).
    /// </summary>
    public static FactoringCalculationResponse Calculate(
        decimal chequeAmount,
        decimal? commissionRate,
        decimal? commissionAmount,
        decimal? bsmvRate,
        decimal expenseAmount)
    {
        var nominal = decimal.Round(chequeAmount, 2);
        if (nominal <= 0m)
            throw new ArgumentException("Çek tutarı sıfırdan büyük olmalıdır.");

        if (expenseAmount < 0m)
            throw new ArgumentException("Masraf tutarı negatif olamaz.");

        var rate = bsmvRate ?? DefaultBsmvRate;
        if (rate < 0m || rate > 100m)
            throw new ArgumentException("BSMV oranı 0 ile 100 arasında olmalıdır.");

        decimal commission;
        decimal effectiveCommissionRate;

        if (commissionAmount is not null && commissionAmount.Value > 0m)
        {
            commission = decimal.Round(commissionAmount.Value, 2);
            effectiveCommissionRate = decimal.Round(commission / nominal * 100m, 4);
        }
        else if (commissionRate is not null && commissionRate.Value > 0m)
        {
            if (commissionRate.Value > 100m)
                throw new ArgumentException("Komisyon oranı %100'den büyük olamaz.");

            effectiveCommissionRate = decimal.Round(commissionRate.Value, 4);
            commission = decimal.Round(nominal * effectiveCommissionRate / 100m, 2);
        }
        else
        {
            commission = 0m;
            effectiveCommissionRate = 0m;
        }

        if (commission < 0m)
            throw new ArgumentException("Komisyon tutarı negatif olamaz.");

        var bsmv = decimal.Round(commission * rate / 100m, 2);
        var expense = decimal.Round(expenseAmount, 2);
        var totalDeduction = commission + bsmv + expense;
        var net = nominal - totalDeduction;

        if (net <= 0m)
        {
            throw new ArgumentException(
                $"Kesintiler ({TurkishFormat.Amount(totalDeduction)}) çek tutarını ({TurkishFormat.Amount(nominal)}) " +
                "aştığı için net tahsilat kalmıyor.");
        }

        return new FactoringCalculationResponse(
            nominal,
            effectiveCommissionRate,
            commission,
            rate,
            bsmv,
            expense,
            totalDeduction,
            net);
    }

    public FactoringCalculationResponse Preview(FactoringPreviewRequest request) =>
        Calculate(
            request.ChequeAmount,
            request.CommissionRate,
            request.CommissionAmount,
            request.BsmvRate,
            request.ExpenseAmount);

    public async Task<IReadOnlyCollection<FactoringTransactionResponse>> GetAllAsync(
        Guid? companyId,
        Guid? projectId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var query = db.FactoringTransactions.AsNoTracking().AsQueryable();

        if (companyId is not null)
            query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId is not null)
            query = query.Where(x => x.ProjectId == projectId.Value);
        if (startDate is not null)
            query = query.Where(x => x.TransactionDate >= AsUtc(startDate.Value));
        if (endDate is not null)
            query = query.Where(x => x.TransactionDate <= AsUtc(endDate.Value));

        return await query
            .OrderByDescending(x => x.TransactionDate)
            .Select(ProjectExpression)
            .ToListAsync(cancellationToken);
    }

    public async Task<FactoringTransactionResponse> GetByIdAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var response = await db.FactoringTransactions
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ProjectExpression)
            .SingleOrDefaultAsync(cancellationToken);

        if (response is null)
            throw new KeyNotFoundException("Faktoring işlemi bulunamadı.");

        return response;
    }

    public async Task<FactoringTransactionResponse> CreateAsync(
        CreateFactoringTransactionRequest request, CancellationToken cancellationToken)
    {
        var cheque = await db.Cheques
            .SingleOrDefaultAsync(x => x.Id == request.ChequeId, cancellationToken);

        if (cheque is null)
            throw new ArgumentException("Çek bulunamadı.");

        // KIRDIRMA DA ÇEKİN DURUMUNU DEĞİŞTİRİYOR: damga burada da
        // zorunlu. Kural ChequeService'ten geliyor — iki ayrı kontrol
        // zamanla ayrışır ve aynı istek bir uçta geçip diğerinde
        // reddedilirdi.
        ChequeService.EnsureRowVersionMatches(cheque, request.RowVersion);

        if (cheque.Direction != ChequeDirection.Received)
            throw new InvalidOperationException("Yalnızca alınan çekler kırdırılabilir.");

        if (cheque.Status != ChequeStatus.Portfolio)
        {
            throw new InvalidOperationException(
                $"Yalnızca portföydeki çekler kırdırılabilir. " +
                $"Çekin durumu: {ChequeService.StatusName(cheque.Status)}.");
        }

        var cashAccount = await db.CashAccounts
            .SingleOrDefaultAsync(
                x => x.Id == request.CashAccountId && x.CompanyId == cheque.CompanyId,
                cancellationToken);

        if (cashAccount is null)
            throw new ArgumentException("Kasa/banka hesabı bulunamadı.");

        if (request.FactoringCurrentAccountId is not null
            && !await db.CurrentAccounts.AnyAsync(
                x => x.Id == request.FactoringCurrentAccountId.Value, cancellationToken))
        {
            throw new ArgumentException("Faktoring şirketi carisi bulunamadı.");
        }

        var projectId = request.ProjectId ?? cheque.ProjectId;
        if (projectId is not null && !await db.Projects.AnyAsync(
                x => x.Id == projectId.Value, cancellationToken))
        {
            throw new ArgumentException("Proje bulunamadı.");
        }

        var calculation = Calculate(
            cheque.Amount,
            request.CommissionRate,
            request.CommissionAmount,
            request.BsmvRate,
            request.ExpenseAmount);

        var internalNumber = await documentNumberService.GenerateAsync(
            cheque.CompanyId, "FACTORING", "FAK", cancellationToken);

        var transactionDate = AsUtc(request.TransactionDate);

        var factoring = new FactoringTransaction
        {
            CompanyId = cheque.CompanyId,
            InternalNumber = internalNumber,
            ChequeId = cheque.Id,
            FactoringCurrentAccountId = request.FactoringCurrentAccountId,
            CashAccountId = cashAccount.Id,
            ProjectId = projectId,
            TransactionDate = transactionDate,
            CurrencyCode = cheque.CurrencyCode,
            ChequeAmount = calculation.ChequeAmount,
            CommissionRate = calculation.CommissionRate,
            CommissionAmount = calculation.CommissionAmount,
            BsmvRate = calculation.BsmvRate,
            BsmvAmount = calculation.BsmvAmount,
            ExpenseAmount = calculation.ExpenseAmount,
            TotalDeductionAmount = calculation.TotalDeductionAmount,
            NetAmount = calculation.NetAmount,
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description!.Trim()
        };

        var ownsTransaction = db.Database.CurrentTransaction is null;
        var dbTransaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            db.FactoringTransactions.Add(factoring);
            await db.SaveChangesAsync(cancellationToken);

            var voucherId = await accountingIntegration
                .CreateFactoringVoucherAsync(factoring, cancellationToken);

            var cashTransaction = new CashTransaction
            {
                CashAccountId = cashAccount.Id,
                TransactionDate = transactionDate,
                TransactionType = CashTransactionType.Factoring,
                Direction = CashTransactionDirection.In,
                Amount = calculation.NetAmount,
                CurrencyCode = cheque.CurrencyCode,
                Description = $"Çek kırdırma {internalNumber} — çek no {cheque.ChequeNumber} " +
                    $"(net, kesinti {TurkishFormat.Amount(calculation.TotalDeductionAmount)})",
                DocumentNumber = internalNumber,
                CurrentAccountId = request.FactoringCurrentAccountId,
                ProjectId = projectId,
                SourceModule = "Factoring",
                SourceEntityId = factoring.Id,
                // Fiş faktoring modülünde üretildi; hareket aynı fişe bağlanır.
                AccountingVoucherId = voucherId
            };

            db.CashTransactions.Add(cashTransaction);

            factoring.AccountingVoucherId = voucherId;
            factoring.CashTransactionId = cashTransaction.Id;

            cheque.Status = ChequeStatus.AtFactoring;
            cheque.CashAccountId = cashAccount.Id;

            // Damga ilerliyor: ilerlemeseydi aynı damgayla gelen ikinci
            // istek de geçer, koruma fiilen çalışmazdı.
            cheque.UpdatedAtUtc = DateTime.UtcNow;

            db.ChequeMovements.Add(new ChequeMovement
            {
                ChequeId = cheque.Id,
                MovementDate = transactionDate,
                FromStatus = ChequeStatus.Portfolio,
                ToStatus = ChequeStatus.AtFactoring,
                Description = $"Faktoringe verildi ({internalNumber}) — " +
                    $"net {TurkishFormat.Amount(calculation.NetAmount)}, kesinti {TurkishFormat.Amount(calculation.TotalDeductionAmount)}",
                CashAccountId = cashAccount.Id,
                AccountingVoucherId = voucherId
            });

            await db.SaveChangesAsync(cancellationToken);

            if (dbTransaction is not null)
                await dbTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (dbTransaction is not null)
                await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            dbTransaction?.Dispose();
        }

        return await GetByIdAsync(factoring.Id, cancellationToken);
    }

    private static System.Linq.Expressions.Expression<
        Func<FactoringTransaction, FactoringTransactionResponse>> ProjectExpression =>
        x => new FactoringTransactionResponse(
            x.Id,
            x.CompanyId,
            x.InternalNumber,
            x.ChequeId,
            x.Cheque.ChequeNumber,
            x.Cheque.BankName,
            x.Cheque.DueDate,
            x.FactoringCurrentAccountId,
            x.FactoringCurrentAccount != null ? x.FactoringCurrentAccount.Title : null,
            x.CashAccountId,
            x.CashAccount.Name,
            x.ProjectId,
            x.Project != null ? x.Project.Code : null,
            x.TransactionDate,
            x.CurrencyCode,
            x.ChequeAmount,
            x.CommissionRate,
            x.CommissionAmount,
            x.BsmvRate,
            x.BsmvAmount,
            x.ExpenseAmount,
            x.TotalDeductionAmount,
            x.NetAmount,
            x.Description,
            x.AccountingVoucherId,
            x.AccountingVoucher != null ? x.AccountingVoucher.VoucherNumber : null);

    private static DateTime AsUtc(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}
