using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Accounting;

public interface IChequeService
{
    Task<IReadOnlyCollection<ChequeListItemResponse>> GetAllAsync(
        Guid? companyId,
        int? direction,
        int? status,
        Guid? currentAccountId,
        Guid? projectId,
        string? search,
        CancellationToken cancellationToken);

    Task<ChequeDetailResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<ChequeSummaryResponse> GetSummaryAsync(
        Guid? companyId, CancellationToken cancellationToken);

    Task<ChequeDetailResponse> CreateAsync(
        CreateChequeRequest request, CancellationToken cancellationToken);

    Task<ChequeDetailResponse> UpdateAsync(
        Guid id, UpdateChequeRequest request, CancellationToken cancellationToken);

    Task<ChequeDetailResponse> ChangeStatusAsync(
        Guid id, ChequeStatusChangeRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Çekin proje/masraf merkezi dağılımını baştan yazar ve giriş fişini
    /// dağılıma göre yeniden üretir. Boş liste dağılımı kaldırır.
    /// </summary>
    Task<ChequeDetailResponse> ReplaceAllocationsAsync(
        Guid id, ChequeAllocationsRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Çift yönlü çek defteri. Durum geçişleri tek bir matristen yürür;
/// her geçiş bir hareket satırı, gerekiyorsa dengeli bir muhasebe fişi
/// ve para hareketi doğuran geçişlerde bir kasa/banka hareketi üretir.
/// Fiş üretilemezse durum da değişmez (tek işlem).
/// </summary>
public sealed class ChequeService(
    AppDbContext db,
    IAccountingIntegrationService accountingIntegration,
    IAccountingVoucherService voucherService,
    IDocumentNumberService documentNumberService) : IChequeService
{
    /// <summary>
    /// İzin verilen durum geçişleri. Portföy → Faktoringde geçişi
    /// bilinçli olarak yok: kırdırma yalnızca faktoring modülünden,
    /// kesinti matematiğiyle birlikte yapılır.
    /// </summary>
    public static readonly IReadOnlyDictionary<ChequeStatus, IReadOnlyCollection<ChequeStatus>>
        AllowedTransitions = new Dictionary<ChequeStatus, IReadOnlyCollection<ChequeStatus>>
        {
            [ChequeStatus.Portfolio] = new[]
            {
                ChequeStatus.AtBank, ChequeStatus.Collected, ChequeStatus.Bounced
            },
            [ChequeStatus.AtBank] = new[]
            {
                ChequeStatus.Portfolio, ChequeStatus.Collected, ChequeStatus.Bounced
            },
            [ChequeStatus.AtFactoring] = new[]
            {
                ChequeStatus.Collected, ChequeStatus.Bounced
            },
            [ChequeStatus.Collected] = Array.Empty<ChequeStatus>(),
            [ChequeStatus.Bounced] = Array.Empty<ChequeStatus>(),
            [ChequeStatus.Issued] = new[]
            {
                ChequeStatus.Paid, ChequeStatus.Returned
            },
            [ChequeStatus.Paid] = Array.Empty<ChequeStatus>(),
            [ChequeStatus.Returned] = Array.Empty<ChequeStatus>()
        };

    /// <summary>Kasa/banka hesabı seçimi zorunlu olan geçişler.</summary>
    public static bool RequiresCashAccount(ChequeStatus from, ChequeStatus to) =>
        (from, to) switch
        {
            (ChequeStatus.Portfolio, ChequeStatus.AtBank) => true,
            (ChequeStatus.Portfolio, ChequeStatus.Collected) => true,
            (ChequeStatus.AtBank, ChequeStatus.Collected) => true,
            (ChequeStatus.AtFactoring, ChequeStatus.Bounced) => true,
            (ChequeStatus.Issued, ChequeStatus.Paid) => true,
            _ => false
        };

    /// <summary>
    /// Geçişin kasa/banka bakiyesine etkisi. null ise para hareketi yok
    /// (ör. bankaya tahsile verme yalnızca çekin yerini değiştirir).
    /// </summary>
    public static (CashTransactionType Type, CashTransactionDirection Direction)?
        CashEffect(ChequeStatus from, ChequeStatus to) => (from, to) switch
        {
            (ChequeStatus.Portfolio, ChequeStatus.Collected) =>
                (CashTransactionType.ChequeCollection, CashTransactionDirection.In),
            (ChequeStatus.AtBank, ChequeStatus.Collected) =>
                (CashTransactionType.ChequeCollection, CashTransactionDirection.In),
            (ChequeStatus.Issued, ChequeStatus.Paid) =>
                (CashTransactionType.ChequePayment, CashTransactionDirection.Out),
            (ChequeStatus.AtFactoring, ChequeStatus.Bounced) =>
                (CashTransactionType.Factoring, CashTransactionDirection.Out),
            _ => null
        };

    public static IReadOnlyCollection<ChequeStatus> NextStatuses(ChequeStatus status) =>
        AllowedTransitions.TryGetValue(status, out var next)
            ? next
            : Array.Empty<ChequeStatus>();

    public static string StatusName(ChequeStatus status) => status switch
    {
        ChequeStatus.Portfolio => "Portföyde",
        ChequeStatus.AtBank => "Bankada (tahsilde)",
        ChequeStatus.AtFactoring => "Faktoringde",
        ChequeStatus.Collected => "Tahsil edildi",
        ChequeStatus.Bounced => "Karşılıksız",
        ChequeStatus.Issued => "Verildi",
        ChequeStatus.Paid => "Ödendi",
        ChequeStatus.Returned => "İade alındı",
        _ => status.ToString()
    };

    public static string DirectionName(ChequeDirection direction) =>
        direction == ChequeDirection.Received ? "Alınan çek" : "Verilen çek";

    public async Task<IReadOnlyCollection<ChequeListItemResponse>> GetAllAsync(
        Guid? companyId,
        int? direction,
        int? status,
        Guid? currentAccountId,
        Guid? projectId,
        string? search,
        CancellationToken cancellationToken)
    {
        var query = db.Cheques.AsNoTracking().AsQueryable();

        if (companyId is not null)
            query = query.Where(x => x.CompanyId == companyId.Value);
        if (direction is not null)
            query = query.Where(x => (int)x.Direction == direction.Value);
        if (status is not null)
            query = query.Where(x => (int)x.Status == status.Value);
        if (currentAccountId is not null)
            query = query.Where(x => x.CurrentAccountId == currentAccountId.Value);
        if (projectId is not null)
            query = query.Where(x => x.ProjectId == projectId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.ChequeNumber.ToLower().Contains(term) ||
                x.InternalNumber.ToLower().Contains(term) ||
                x.BankName.ToLower().Contains(term) ||
                (x.Drawer != null && x.Drawer.ToLower().Contains(term)));
        }

        var rows = await query
            .OrderBy(x => x.DueDate)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.Direction,
                x.Status,
                x.InternalNumber,
                x.ChequeNumber,
                x.BankName,
                x.Drawer,
                x.CurrentAccountId,
                CurrentAccountTitle = x.CurrentAccount != null ? x.CurrentAccount.Title : null,
                x.ProjectId,
                ProjectCode = x.Project != null ? x.Project.Code : null,
                x.CostCenterCode,
                x.Amount,
                x.CurrencyCode,
                x.IssueDate,
                x.DueDate
            })
            .ToListAsync(cancellationToken);

        var today = DateTime.UtcNow.Date;

        return rows.Select(x =>
        {
            var daysToDue = (int)(x.DueDate.Date - today).TotalDays;
            var isOpen = x.Status is ChequeStatus.Portfolio or ChequeStatus.AtBank
                or ChequeStatus.AtFactoring or ChequeStatus.Issued;

            return new ChequeListItemResponse(
                x.Id,
                x.CompanyId,
                (int)x.Direction,
                DirectionName(x.Direction),
                (int)x.Status,
                StatusName(x.Status),
                x.InternalNumber,
                x.ChequeNumber,
                x.BankName,
                x.Drawer,
                x.CurrentAccountId,
                x.CurrentAccountTitle,
                x.ProjectId,
                x.ProjectCode,
                x.CostCenterCode,
                x.Amount,
                x.CurrencyCode,
                x.IssueDate,
                x.DueDate,
                daysToDue,
                isOpen && daysToDue < 0);
        }).ToList();
    }

    public async Task<ChequeDetailResponse> GetByIdAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var cheque = await db.Cheques
            .AsNoTracking()
            .Include(x => x.CurrentAccount)
            .Include(x => x.Project)
            .Include(x => x.ProgressPayment)
            .Include(x => x.SupplierInvoice)
            .Include(x => x.CashAccount)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (cheque is null)
            throw new KeyNotFoundException("Çek bulunamadı.");

        var movements = await db.ChequeMovements
            .AsNoTracking()
            .Where(x => x.ChequeId == id)
            .OrderBy(x => x.MovementDate).ThenBy(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.MovementDate,
                x.FromStatus,
                x.ToStatus,
                x.Description,
                x.CashAccountId,
                CashAccountName = x.CashAccount != null ? x.CashAccount.Name : null,
                x.AccountingVoucherId,
                AccountingVoucherNumber = x.AccountingVoucher != null
                    ? x.AccountingVoucher.VoucherNumber
                    : null
            })
            .ToListAsync(cancellationToken);

        var allocations = await db.ChequeAllocations
            .AsNoTracking()
            .Where(x => x.ChequeId == id)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new ChequeAllocationResponse(
                x.Id,
                x.Amount,
                x.ProjectId,
                x.Project != null ? x.Project.Code : null,
                x.Project != null ? x.Project.Name : null,
                x.CostCenterCode,
                x.SupplierInvoiceId,
                x.SupplierInvoice != null ? x.SupplierInvoice.InvoiceNumber : null,
                x.SalesInvoiceId,
                x.SalesInvoice != null ? x.SalesInvoice.InternalNumber : null,
                x.Description))
            .ToListAsync(cancellationToken);

        return new ChequeDetailResponse(
            cheque.Id,
            cheque.CompanyId,
            (int)cheque.Direction,
            DirectionName(cheque.Direction),
            (int)cheque.Status,
            StatusName(cheque.Status),
            cheque.InternalNumber,
            cheque.ChequeNumber,
            cheque.BankName,
            cheque.BankBranch,
            cheque.Drawer,
            cheque.CurrentAccountId,
            cheque.CurrentAccount?.Title,
            cheque.ProjectId,
            cheque.Project?.Code,
            cheque.Project?.Name,
            cheque.CostCenterCode,
            cheque.Amount,
            cheque.CurrencyCode,
            cheque.IssueDate,
            cheque.DueDate,
            cheque.ProgressPaymentId,
            cheque.ProgressPayment?.ProgressPaymentNumber,
            cheque.SupplierInvoiceId,
            cheque.SupplierInvoice?.InvoiceNumber,
            cheque.CashAccountId,
            cheque.CashAccount?.Name,
            cheque.Description,
            NextStatuses(cheque.Status).Select(x => (int)x).ToList(),
            movements.Select(x => new ChequeMovementResponse(
                x.Id,
                x.MovementDate,
                x.FromStatus is null ? null : (int)x.FromStatus.Value,
                x.FromStatus is null ? null : StatusName(x.FromStatus.Value),
                (int)x.ToStatus,
                StatusName(x.ToStatus),
                x.Description,
                x.CashAccountId,
                x.CashAccountName,
                x.AccountingVoucherId,
                x.AccountingVoucherNumber)).ToList(),
            allocations);
    }

    public async Task<ChequeSummaryResponse> GetSummaryAsync(
        Guid? companyId, CancellationToken cancellationToken)
    {
        var query = db.Cheques.AsNoTracking().AsQueryable();

        if (companyId is not null)
            query = query.Where(x => x.CompanyId == companyId.Value);

        var groups = await query
            .GroupBy(x => x.Status)
            .Select(g => new
            {
                Status = g.Key,
                Total = g.Sum(x => x.Amount),
                Count = g.Count()
            })
            .ToListAsync(cancellationToken);

        decimal Total(ChequeStatus status) =>
            groups.Where(x => x.Status == status).Sum(x => x.Total);
        int Count(ChequeStatus status) =>
            groups.Where(x => x.Status == status).Sum(x => x.Count);

        return new ChequeSummaryResponse(
            Total(ChequeStatus.Portfolio),
            Total(ChequeStatus.AtBank),
            Total(ChequeStatus.AtFactoring),
            Total(ChequeStatus.Collected),
            Total(ChequeStatus.Bounced),
            Total(ChequeStatus.Issued),
            Total(ChequeStatus.Paid),
            Count(ChequeStatus.Portfolio) + Count(ChequeStatus.AtBank)
                + Count(ChequeStatus.AtFactoring),
            Count(ChequeStatus.Issued));
    }

    public async Task<ChequeDetailResponse> CreateAsync(
        CreateChequeRequest request, CancellationToken cancellationToken)
    {
        var direction = (ChequeDirection)request.Direction;
        if (direction is not (ChequeDirection.Received or ChequeDirection.Issued))
            throw new ArgumentException("Geçersiz çek yönü.");

        if (request.Amount <= 0m)
            throw new ArgumentException("Çek tutarı sıfırdan büyük olmalıdır.");

        if (string.IsNullOrWhiteSpace(request.ChequeNumber))
            throw new ArgumentException("Çek numarası zorunludur.");

        if (string.IsNullOrWhiteSpace(request.BankName))
            throw new ArgumentException("Banka adı zorunludur.");

        if (request.CurrentAccountId is null)
        {
            throw new ArgumentException(direction == ChequeDirection.Received
                ? "Çeki veren cari seçilmelidir."
                : "Çekin verildiği cari seçilmelidir.");
        }

        if (!await db.Companies.AnyAsync(x => x.Id == request.CompanyId, cancellationToken))
            throw new ArgumentException("Şirket bulunamadı.");

        if (!await db.CurrentAccounts.AnyAsync(
                x => x.Id == request.CurrentAccountId.Value, cancellationToken))
        {
            throw new ArgumentException("Cari bulunamadı.");
        }

        if (request.ProjectId is not null && !await db.Projects.AnyAsync(
                x => x.Id == request.ProjectId.Value, cancellationToken))
        {
            throw new ArgumentException("Proje bulunamadı.");
        }

        if (await db.Cheques.AnyAsync(
                x => x.CompanyId == request.CompanyId
                    && x.Direction == direction
                    && x.ChequeNumber == request.ChequeNumber.Trim(),
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"'{request.ChequeNumber.Trim()}' numaralı çek zaten kayıtlı.");
        }

        var internalNumber = await documentNumberService.GenerateAsync(
            request.CompanyId,
            direction == ChequeDirection.Received ? "CHEQUE_RECEIVED" : "CHEQUE_ISSUED",
            direction == ChequeDirection.Received ? "ACK" : "VCK",
            cancellationToken);

        var cheque = new Cheque
        {
            CompanyId = request.CompanyId,
            Direction = direction,
            Status = direction == ChequeDirection.Received
                ? ChequeStatus.Portfolio
                : ChequeStatus.Issued,
            InternalNumber = internalNumber,
            ChequeNumber = request.ChequeNumber.Trim(),
            BankName = request.BankName.Trim(),
            BankBranch = Normalize(request.BankBranch),
            Drawer = Normalize(request.Drawer),
            CurrentAccountId = request.CurrentAccountId,
            ProjectId = request.ProjectId,
            CostCenterCode = Normalize(request.CostCenterCode),
            Amount = decimal.Round(request.Amount, 2),
            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
                ? "TRY"
                : request.CurrencyCode.Trim().ToUpperInvariant(),
            IssueDate = AsUtc(request.IssueDate),
            DueDate = AsUtc(request.DueDate),
            ProgressPaymentId = request.ProgressPaymentId,
            SupplierInvoiceId = request.SupplierInvoiceId,
            Description = Normalize(request.Description)
        };

        var ownsTransaction = db.Database.CurrentTransaction is null;
        var dbTransaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            db.Cheques.Add(cheque);
            await db.SaveChangesAsync(cancellationToken);

            // Dağılım fişten ÖNCE yazılır: fiş cari tarafını bu satırlara
            // göre böler, sonradan eklenirse fiş dağılımı görmezdi.
            var allocations = await BuildAllocationsAsync(
                cheque, request.Allocations, cancellationToken);

            if (allocations.Count > 0)
            {
                db.ChequeAllocations.AddRange(allocations);
                await db.SaveChangesAsync(cancellationToken);
            }

            var voucherId = await accountingIntegration.CreateChequeVoucherAsync(
                cheque, null, cheque.Status, cheque.IssueDate, null, cancellationToken);

            db.ChequeMovements.Add(new ChequeMovement
            {
                ChequeId = cheque.Id,
                MovementDate = cheque.IssueDate,
                FromStatus = null,
                ToStatus = cheque.Status,
                Description = cheque.Direction == ChequeDirection.Received
                    ? "Çek alındı, portföye girdi"
                    : "Çek düzenlendi ve tedarikçiye verildi",
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

        return await GetByIdAsync(cheque.Id, cancellationToken);
    }

    public async Task<ChequeDetailResponse> UpdateAsync(
        Guid id, UpdateChequeRequest request, CancellationToken cancellationToken)
    {
        var cheque = await db.Cheques.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (cheque is null)
            throw new KeyNotFoundException("Çek bulunamadı.");

        var isOpen = cheque.Status is ChequeStatus.Portfolio or ChequeStatus.Issued;
        if (!isOpen)
        {
            throw new InvalidOperationException(
                "Çek işlem gördüğü için yalnızca portföydeki/yeni verilen çekler düzenlenebilir.");
        }

        // Tutar ve cari, giriş fişine yazıldığı için değiştirilemez;
        // yanlışsa çek iptal edilip yeniden girilmelidir.
        if (decimal.Round(request.Amount, 2) != decimal.Round(cheque.Amount, 2))
        {
            throw new InvalidOperationException(
                "Çek tutarı giriş fişine işlendiği için değiştirilemez.");
        }

        if (request.CurrentAccountId != cheque.CurrentAccountId)
        {
            throw new InvalidOperationException(
                "Çekin carisi giriş fişine işlendiği için değiştirilemez.");
        }

        if (string.IsNullOrWhiteSpace(request.ChequeNumber))
            throw new ArgumentException("Çek numarası zorunludur.");

        cheque.ChequeNumber = request.ChequeNumber.Trim();
        cheque.BankName = string.IsNullOrWhiteSpace(request.BankName)
            ? cheque.BankName
            : request.BankName.Trim();
        cheque.BankBranch = Normalize(request.BankBranch);
        cheque.Drawer = Normalize(request.Drawer);
        cheque.ProjectId = request.ProjectId;
        cheque.CostCenterCode = Normalize(request.CostCenterCode);
        cheque.IssueDate = AsUtc(request.IssueDate);
        cheque.DueDate = AsUtc(request.DueDate);
        cheque.ProgressPaymentId = request.ProgressPaymentId;
        cheque.SupplierInvoiceId = request.SupplierInvoiceId;
        cheque.Description = Normalize(request.Description);

        await db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(cheque.Id, cancellationToken);
    }

    public async Task<ChequeDetailResponse> ChangeStatusAsync(
        Guid id, ChequeStatusChangeRequest request, CancellationToken cancellationToken)
    {
        var cheque = await db.Cheques.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (cheque is null)
            throw new KeyNotFoundException("Çek bulunamadı.");

        var toStatus = (ChequeStatus)request.ToStatus;
        var fromStatus = cheque.Status;

        if (!Enum.IsDefined(toStatus))
            throw new ArgumentException("Geçersiz çek durumu.");

        if (!NextStatuses(fromStatus).Contains(toStatus))
        {
            throw new InvalidOperationException(
                $"'{StatusName(fromStatus)}' durumundan '{StatusName(toStatus)}' " +
                "durumuna geçiş yapılamaz.");
        }

        CashAccount? cashAccount = null;
        if (request.CashAccountId is not null)
        {
            cashAccount = await db.CashAccounts
                .SingleOrDefaultAsync(
                    x => x.Id == request.CashAccountId.Value
                        && x.CompanyId == cheque.CompanyId,
                    cancellationToken);

            if (cashAccount is null)
                throw new ArgumentException("Kasa/banka hesabı bulunamadı.");
        }

        if (RequiresCashAccount(fromStatus, toStatus) && cashAccount is null)
            throw new ArgumentException("Bu geçiş için kasa/banka hesabı seçilmelidir.");

        var movementDate = AsUtc(request.MovementDate);

        var ownsTransaction = db.Database.CurrentTransaction is null;
        var dbTransaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var voucherId = await accountingIntegration.CreateChequeVoucherAsync(
                cheque, fromStatus, toStatus, movementDate, cashAccount, cancellationToken);

            var cashEffect = CashEffect(fromStatus, toStatus);
            if (cashEffect is not null && cashAccount is not null)
            {
                db.CashTransactions.Add(new CashTransaction
                {
                    CashAccountId = cashAccount.Id,
                    TransactionDate = movementDate,
                    TransactionType = cashEffect.Value.Type,
                    Direction = cashEffect.Value.Direction,
                    Amount = cheque.Amount,
                    CurrencyCode = cheque.CurrencyCode,
                    Description = $"{cheque.InternalNumber} — {StatusName(toStatus)} " +
                        $"(çek no {cheque.ChequeNumber})",
                    DocumentNumber = cheque.ChequeNumber,
                    CurrentAccountId = cheque.CurrentAccountId,
                    ProjectId = cheque.ProjectId,
                    SourceModule = "Cheque",
                    SourceEntityId = cheque.Id,
                    // Fiş çek modülünde üretildi; hareket aynı fişe bağlanır,
                    // ikinci bir fiş kesilmez.
                    AccountingVoucherId = voucherId
                });
            }

            cheque.Status = toStatus;
            if (cashAccount is not null)
                cheque.CashAccountId = cashAccount.Id;

            db.ChequeMovements.Add(new ChequeMovement
            {
                ChequeId = cheque.Id,
                MovementDate = movementDate,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                Description = string.IsNullOrWhiteSpace(request.Description)
                    ? $"{StatusName(fromStatus)} → {StatusName(toStatus)}"
                    : request.Description!.Trim(),
                CashAccountId = cashAccount?.Id,
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

        return await GetByIdAsync(cheque.Id, cancellationToken);
    }

    public async Task<ChequeDetailResponse> ReplaceAllocationsAsync(
        Guid id, ChequeAllocationsRequest request, CancellationToken cancellationToken)
    {
        var cheque = await db.Cheques.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Çek bulunamadı.");

        var movements = await db.ChequeMovements
            .Where(x => x.ChequeId == id)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        // Çek işlem gördükten sonra (tahsil, ödeme, karşılıksız...) dağılım
        // değiştirilemez: sonraki fişler ilk dağılıma göre kesilmiş olur ve
        // geriye dönük değişiklik onlarla tutarsız kalırdı.
        if (movements.Count > 1)
        {
            throw new InvalidOperationException(
                "Çek işlem gördüğü için dağılımı değiştirilemez. " +
                "Düzeltme gerekiyorsa muhasebede düzeltme fişi düzenleyin.");
        }

        var allocations = await BuildAllocationsAsync(
            cheque, request.Allocations, cancellationToken);

        var ownsTransaction = db.Database.CurrentTransaction is null;
        var dbTransaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var existing = await db.ChequeAllocations
                .Where(x => x.ChequeId == id)
                .ToListAsync(cancellationToken);

            db.ChequeAllocations.RemoveRange(existing);

            if (allocations.Count > 0)
                db.ChequeAllocations.AddRange(allocations);

            await db.SaveChangesAsync(cancellationToken);

            // Giriş fişi dağılıma göre yeniden üretilir. Eskisi SİLİNMEZ,
            // iptal edilir: muhasebede yazılan fişin izi kalmalı.
            var entryMovement = movements.SingleOrDefault(x => x.FromStatus is null);

            if (entryMovement?.AccountingVoucherId is Guid oldVoucherId)
            {
                await voucherService.CancelAsync(
                    oldVoucherId,
                    $"{cheque.InternalNumber} — çek dağılımı değiştirildi, fiş yeniden kesildi.",
                    cancellationToken);
            }

            var newVoucherId = await accountingIntegration.CreateChequeVoucherAsync(
                cheque, null, cheque.Status, cheque.IssueDate, null, cancellationToken);

            if (entryMovement is not null)
            {
                entryMovement.AccountingVoucherId = newVoucherId;
                entryMovement.UpdatedAtUtc = DateTime.UtcNow;
            }

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

        return await GetByIdAsync(cheque.Id, cancellationToken);
    }

    /// <summary>
    /// Dağılım satırlarını doğrular ve kurar.
    ///
    /// Fatura bağlantılı satırda proje ve masraf merkezi FATURADAN alınır;
    /// istemcinin gönderdiği değerler yok sayılır. Aksi halde aynı ödeme
    /// faturada bir projeye, çekte başka bir projeye yazılabilir ve iki
    /// rapor birbirini tutmazdı.
    /// </summary>
    private async Task<List<ChequeAllocation>> BuildAllocationsAsync(
        Cheque cheque,
        IReadOnlyCollection<ChequeAllocationRequest>? requests,
        CancellationToken cancellationToken)
    {
        if (requests is null || requests.Count == 0)
            return [];

        var result = new List<ChequeAllocation>();
        var lineNumber = 0;

        foreach (var request in requests)
        {
            lineNumber++;

            var amount = decimal.Round(request.Amount, 2);

            if (amount <= 0m)
                throw new ArgumentException($"Dağılım {lineNumber}: tutar sıfırdan büyük olmalıdır.");

            if (request.SupplierInvoiceId is not null && request.SalesInvoiceId is not null)
            {
                throw new ArgumentException(
                    $"Dağılım {lineNumber}: bir satır hem alış hem satış faturasına bağlanamaz.");
            }

            var allocation = new ChequeAllocation
            {
                ChequeId = cheque.Id,
                Amount = amount,
                Description = Normalize(request.Description)
            };

            if (request.SupplierInvoiceId is Guid supplierInvoiceId)
            {
                if (cheque.Direction != ChequeDirection.Issued)
                {
                    throw new ArgumentException(
                        $"Dağılım {lineNumber}: tedarikçi faturası yalnızca verilen çeke bağlanabilir.");
                }

                var invoice = await db.SupplierInvoices
                    .AsNoTracking()
                    .Where(x => x.Id == supplierInvoiceId && x.CompanyId == cheque.CompanyId)
                    .Select(x => new
                    {
                        x.Id,
                        x.InvoiceNumber,
                        x.SupplierCurrentAccountId,
                        x.ProjectId,
                        x.CostCenterCode,
                        x.GrandTotal,
                        x.Status,
                        ProjectCode = x.Project != null ? x.Project.Code : null
                    })
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new ArgumentException(
                        $"Dağılım {lineNumber}: tedarikçi faturası bulunamadı.");

                if (invoice.SupplierCurrentAccountId != cheque.CurrentAccountId)
                {
                    throw new ArgumentException(
                        $"Dağılım {lineNumber}: {invoice.InvoiceNumber} numaralı fatura " +
                        "çekin carisine ait değil.");
                }

                if (invoice.Status is SupplierInvoiceStatus.Cancelled
                    or SupplierInvoiceStatus.Rejected)
                {
                    throw new ArgumentException(
                        $"Dağılım {lineNumber}: iptal/reddedilmiş faturaya ödeme bağlanamaz.");
                }

                await EnsureInvoiceNotOverAllocatedAsync(
                    cheque.Id, supplierInvoiceId, null, invoice.GrandTotal,
                    requests, lineNumber, invoice.InvoiceNumber, cancellationToken);

                allocation.SupplierInvoiceId = supplierInvoiceId;
                allocation.ProjectId = invoice.ProjectId;
                allocation.CostCenterCode = invoice.CostCenterCode ?? invoice.ProjectCode;
                result.Add(allocation);
                continue;
            }

            if (request.SalesInvoiceId is Guid salesInvoiceId)
            {
                if (cheque.Direction != ChequeDirection.Received)
                {
                    throw new ArgumentException(
                        $"Dağılım {lineNumber}: satış faturası yalnızca alınan çeke bağlanabilir.");
                }

                var invoice = await db.SalesInvoices
                    .AsNoTracking()
                    .Where(x => x.Id == salesInvoiceId && x.CompanyId == cheque.CompanyId)
                    .Select(x => new
                    {
                        x.Id,
                        x.InternalNumber,
                        x.CustomerCurrentAccountId,
                        x.ProjectId,
                        x.GrandTotal,
                        x.Status,
                        ProjectCode = x.Project != null ? x.Project.Code : null
                    })
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new ArgumentException(
                        $"Dağılım {lineNumber}: satış faturası bulunamadı.");

                if (invoice.CustomerCurrentAccountId != cheque.CurrentAccountId)
                {
                    throw new ArgumentException(
                        $"Dağılım {lineNumber}: {invoice.InternalNumber} numaralı fatura " +
                        "çekin carisine ait değil.");
                }

                if (invoice.Status == SalesInvoiceStatus.Cancelled)
                {
                    throw new ArgumentException(
                        $"Dağılım {lineNumber}: iptal edilmiş faturaya tahsilat bağlanamaz.");
                }

                await EnsureInvoiceNotOverAllocatedAsync(
                    cheque.Id, null, salesInvoiceId, invoice.GrandTotal,
                    requests, lineNumber, invoice.InternalNumber, cancellationToken);

                allocation.SalesInvoiceId = salesInvoiceId;
                allocation.ProjectId = invoice.ProjectId;
                allocation.CostCenterCode = invoice.ProjectCode;
                result.Add(allocation);
                continue;
            }

            // Elle dağılım: fatura yoksa hedef birim mutlaka belirtilmeli;
            // yoksa pay hangi masraf merkezine gideceği belirsiz kalırdı.
            if (request.ProjectId is null && string.IsNullOrWhiteSpace(request.CostCenterCode))
            {
                throw new ArgumentException(
                    $"Dağılım {lineNumber}: proje, masraf merkezi ya da fatura seçilmelidir.");
            }

            string? projectCode = null;

            if (request.ProjectId is Guid projectId)
            {
                projectCode = await db.Projects
                    .Where(x => x.Id == projectId && x.CompanyId == cheque.CompanyId)
                    .Select(x => x.Code)
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new ArgumentException($"Dağılım {lineNumber}: proje bulunamadı.");
            }

            allocation.ProjectId = request.ProjectId;
            allocation.CostCenterCode = Normalize(request.CostCenterCode) ?? projectCode;
            result.Add(allocation);
        }

        var total = result.Sum(x => x.Amount);

        if (total != decimal.Round(cheque.Amount, 2))
        {
            throw new ArgumentException(
                $"Dağılım toplamı ({total:N2}) çek tutarına ({cheque.Amount:N2}) eşit olmalıdır.");
        }

        return result;
    }

    /// <summary>
    /// Bir faturaya bağlanan toplam ödeme fatura tutarını aşamaz; aşarsa
    /// cari kapatma yanlış olur ve fatura fazla ödenmiş görünürdü.
    /// Kontrol, bu çekin ESKİ satırları hariç tutularak yapılır.
    /// </summary>
    private async Task EnsureInvoiceNotOverAllocatedAsync(
        Guid chequeId,
        Guid? supplierInvoiceId,
        Guid? salesInvoiceId,
        decimal invoiceTotal,
        IReadOnlyCollection<ChequeAllocationRequest> requests,
        int lineNumber,
        string invoiceNumber,
        CancellationToken cancellationToken)
    {
        var otherCheques = await db.ChequeAllocations
            .AsNoTracking()
            .Where(x => x.ChequeId != chequeId &&
                        ((supplierInvoiceId != null && x.SupplierInvoiceId == supplierInvoiceId) ||
                         (salesInvoiceId != null && x.SalesInvoiceId == salesInvoiceId)))
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var thisRequest = requests
            .Where(x => (supplierInvoiceId != null && x.SupplierInvoiceId == supplierInvoiceId) ||
                        (salesInvoiceId != null && x.SalesInvoiceId == salesInvoiceId))
            .Sum(x => decimal.Round(x.Amount, 2));

        if (otherCheques + thisRequest > decimal.Round(invoiceTotal, 2))
        {
            throw new ArgumentException(
                $"Dağılım {lineNumber}: {invoiceNumber} numaralı faturaya bağlanan toplam " +
                $"({otherCheques + thisRequest:N2}) fatura tutarını ({invoiceTotal:N2}) aşıyor.");
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime AsUtc(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}
