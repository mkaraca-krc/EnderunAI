using EnderunAI.Api.Contracts.HumanResources;
using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models.HumanResources;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.HumanResources;

public sealed class HrApprovalService(
    HrDbContext hrDb,
    AppDbContext appDb,
    EnderunAI.Api.Services.Accounting.IAccountingIntegrationService accountingIntegration,
    EnderunAI.Api.Security.IExtraPaymentVisibilityService extraPaymentVisibility,
    SalaryTakeHomeService takeHome)
    : IHrApprovalService
{
    /// <summary>
    /// Dönemin bordrosunu tek bir tahakkuk fişiyle muhasebeleştirir.
    /// Dönemde onaylı bordro yoksa ya da fiş zaten kesilmişse işlem
    /// açık bir mesajla durur — mükerrer tahakkuk defteri bozar.
    /// </summary>
    public async Task<PayrollPeriodPostingResult> PostPayrollPeriodAsync(
        PostPayrollPeriodRequest request, Guid? userId,
        CancellationToken cancellationToken)
    {
        ValidatePeriod(request.Year, request.Month);

        var reference = PayrollPeriodReference(request.Year, request.Month);

        if (await PeriodVoucherExistsAsync(
                request.CompanyId, "PayrollAccrual", reference, cancellationToken))
        {
            throw new InvalidOperationException(
                $"{request.Month:00}/{request.Year} dönemi bordrosu zaten " +
                "muhasebeleştirilmiş.");
        }

        var records = await hrDb.PayrollRecords
            .Where(x => x.CompanyId == request.CompanyId &&
                        x.Year == request.Year && x.Month == request.Month &&
                        x.Status == PayrollStatus.Approved)
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
        {
            throw new InvalidOperationException(
                $"{request.Month:00}/{request.Year} döneminde onaylanmış bordro yok.");
        }

        var totals = new EnderunAI.Api.Services.Accounting.PayrollAccrualTotals(
            TotalEarnings: records.Sum(x => x.TotalEarnings),
            NetPayable: records.Sum(x => x.OfficialNetPayableAmount),
            IncomeTax: records.Sum(x => x.IncomeTaxDeduction),
            StampTax: records.Sum(x => x.StampTaxDeduction),
            SgkEmployee: records.Sum(x => x.SgkEmployeeDeduction),
            UnemploymentEmployee: records.Sum(x => x.UnemploymentEmployeeDeduction),
            SgkEmployer: records.Sum(x => x.SgkEmployerAmount),
            UnemploymentEmployer: records.Sum(x => x.UnemploymentEmployerAmount),
            AdvanceAndOtherDeductions: records.Sum(
                x => x.AdvanceDeduction + x.OtherDeductionAmount),
            PersonnelCount: records.Count,
            CostCenters: await BuildPayrollCostCentersAsync(
                request.CompanyId, records, cancellationToken));

        var voucherId = await accountingIntegration.CreatePayrollAccrualVoucherAsync(
            request.CompanyId, request.Year, request.Month, totals, cancellationToken);

        var voucherNumber = await appDb.AccountingVouchers
            .Where(x => x.Id == voucherId)
            .Select(x => x.VoucherNumber)
            .SingleAsync(cancellationToken);

        var employerBurden = totals.SgkEmployer + totals.UnemploymentEmployer;

        return new PayrollPeriodPostingResult(
            request.CompanyId,
            request.Year,
            request.Month,
            records.Count,
            totals.TotalEarnings,
            totals.NetPayable,
            employerBurden,
            totals.TotalEarnings + employerBurden,
            voucherId,
            voucherNumber);
    }

    public async Task<PayrollPeriodPaymentResult> PayPayrollPeriodAsync(
        PayPayrollPeriodRequest request, Guid? userId,
        CancellationToken cancellationToken)
    {
        ValidatePeriod(request.Year, request.Month);

        var accrualReference = PayrollPeriodReference(request.Year, request.Month);
        var paymentReference = $"BORDRO-ODEME-{request.Year}-{request.Month:00}";

        if (!await PeriodVoucherExistsAsync(
                request.CompanyId, "PayrollAccrual", accrualReference, cancellationToken))
        {
            throw new InvalidOperationException(
                $"{request.Month:00}/{request.Year} dönemi önce muhasebeleştirilmelidir.");
        }

        if (await PeriodVoucherExistsAsync(
                request.CompanyId, "PayrollPayment", paymentReference, cancellationToken))
        {
            throw new InvalidOperationException(
                $"{request.Month:00}/{request.Year} dönemi bordrosu zaten ödenmiş.");
        }

        var records = await hrDb.PayrollRecords
            .Where(x => x.CompanyId == request.CompanyId &&
                        x.Year == request.Year && x.Month == request.Month &&
                        x.Status == PayrollStatus.Approved)
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
        {
            throw new InvalidOperationException(
                $"{request.Month:00}/{request.Year} döneminde ödenecek bordro yok.");
        }

        var amount = records.Sum(x => x.OfficialNetPayableAmount);

        var posting = await accountingIntegration.CreatePayrollPaymentVoucherAsync(
            request.CompanyId, request.Year, request.Month,
            request.CashAccountId, amount, request.PaymentDate, cancellationToken);

        var paidAt = request.PaymentDate == default
            ? DateTime.UtcNow
            : DateTime.SpecifyKind(request.PaymentDate.Date, DateTimeKind.Utc);

        foreach (var record in records)
        {
            record.Status = PayrollStatus.Paid;
            record.PaidAtUtc = paidAt;
            record.PaymentReference = Clean(request.PaymentReference) ?? paymentReference;
            Touch(record, userId);
        }

        await hrDb.SaveChangesAsync(cancellationToken);

        var voucherNumber = await appDb.AccountingVouchers
            .Where(x => x.Id == posting.VoucherId)
            .Select(x => x.VoucherNumber)
            .SingleAsync(cancellationToken);

        return new PayrollPeriodPaymentResult(
            request.CompanyId,
            request.Year,
            request.Month,
            records.Count,
            amount,
            posting.VoucherId,
            voucherNumber,
            posting.CashTransactionId);
    }

    private static string PayrollPeriodReference(int year, int month) =>
        $"BORDRO-{year}-{month:00}";

    /// <summary>
    /// Bordro giderinin masraf merkezi kırılımı.
    ///
    /// Merkez personelinin gideri merkez ofisin masraf merkezi koduna,
    /// şantiye personelininki çalıştığı PROJENİN koduna yazılır. Şantiye
    /// yerine proje kodu kullanılması bilinçli: satın alma, hakediş ve
    /// diğer tüm modüller fiş satırına proje kodunu yazıyor; şantiye kodu
    /// yazmak aynı işin maliyetini defterde iki ayrı etikete bölerdi.
    /// Şantiye kırılımı HrProjectLaborCost üzerinden izlenmeye devam
    /// ediyor.
    ///
    /// Görev yeri belirsiz personel şirket koduna düşer — tahmin
    /// yürütülmez.
    /// </summary>
    private async Task<IReadOnlyList<EnderunAI.Api.Services.Accounting.PayrollCostCenterShare>>
        BuildPayrollCostCentersAsync(
            Guid companyId,
            IReadOnlyCollection<HrPayrollRecord> records,
            CancellationToken cancellationToken)
    {
        var companyCode = await appDb.Companies
            .Where(x => x.Id == companyId)
            .Select(x => x.Code)
            .SingleAsync(cancellationToken);

        var personnelIds = records.Select(x => x.PersonnelId).Distinct().ToList();

        var personnel = await appDb.Personnel
            .AsNoTracking()
            .Where(x => personnelIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.WorkLocationType,
                BranchCode = x.Branch != null
                    ? (x.Branch.CostCenterCode ?? x.Branch.Code)
                    : null,
                BranchName = x.Branch != null ? x.Branch.Name : null
            })
            .ToListAsync(cancellationToken);

        var siteAssignments = await appDb.ProjectSiteAssignments
            .AsNoTracking()
            .Where(x => personnelIds.Contains(x.PersonnelId) &&
                        x.IsActive && x.EndDate == null)
            .Select(x => new
            {
                x.PersonnelId,
                ProjectCode = x.ProjectSite.Project.Code,
                ProjectName = x.ProjectSite.Project.Name
            })
            .ToListAsync(cancellationToken);

        var personnelById = personnel.ToDictionary(x => x.Id);
        var siteByPersonnel = siteAssignments
            .GroupBy(x => x.PersonnelId)
            .ToDictionary(x => x.Key, x => x.First());

        var buckets = new Dictionary<string, (string Label, decimal Amount, int Count)>();

        foreach (var record in records)
        {
            var expense = record.TotalEarnings +
                          record.SgkEmployerAmount +
                          record.UnemploymentEmployerAmount;

            personnelById.TryGetValue(record.PersonnelId, out var person);

            string code;
            string label;

            if (person?.WorkLocationType == EnderunAI.Api.Models.WorkLocationType.ProjectSite &&
                siteByPersonnel.TryGetValue(record.PersonnelId, out var site))
            {
                code = site.ProjectCode;
                label = site.ProjectName;
            }
            else if (person?.WorkLocationType == EnderunAI.Api.Models.WorkLocationType.HeadOffice &&
                     person.BranchCode is not null)
            {
                code = person.BranchCode;
                label = person.BranchName ?? "Merkez";
            }
            else
            {
                code = companyCode;
                label = "Görev yeri atanmamış";
            }

            if (buckets.TryGetValue(code, out var existing))
            {
                buckets[code] = (
                    existing.Label,
                    existing.Amount + expense,
                    existing.Count + 1);
            }
            else
            {
                buckets[code] = (label, expense, 1);
            }
        }

        return buckets
            .Select(x => new EnderunAI.Api.Services.Accounting.PayrollCostCenterShare(
                x.Key, x.Value.Label, decimal.Round(x.Value.Amount, 2), x.Value.Count))
            .ToList();
    }

    private async Task<bool> PeriodVoucherExistsAsync(
        Guid companyId, string sourceModule, string reference,
        CancellationToken cancellationToken) =>
        await appDb.AccountingVouchers.AnyAsync(
            x => x.CompanyId == companyId &&
                 x.SourceModule == sourceModule &&
                 x.ReferenceNumber == reference &&
                 x.Status != EnderunAI.Api.Models.AccountingVoucherStatus.Cancelled,
            cancellationToken);

    public async Task<IReadOnlyList<HrLeaveResponse>> GetLeavesAsync(
        Guid? companyId, Guid? personnelId, Guid? projectId, int? leaveType,
        int? status, DateTime? startDate, DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var query = hrDb.LeaveRequests.AsNoTracking();
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId);
        if (personnelId.HasValue) query = query.Where(x => x.PersonnelId == personnelId);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId);
        if (leaveType.HasValue) query = query.Where(x => (int)x.LeaveType == leaveType);
        if (status.HasValue) query = query.Where(x => (int)x.Status == status);
        if (startDate.HasValue) query = query.Where(x => x.EndDate >= startDate.Value.Date);
        if (endDate.HasValue) query = query.Where(x => x.StartDate <= endDate.Value.Date);
        return (await query.OrderByDescending(x => x.StartDate)
                .ToListAsync(cancellationToken))
            .Select(ToLeaveResponse).ToList();
    }

    /// <summary>
    /// Yıllık izin talebi bakiyeyi aşıyorsa açıklama üretir.
    ///
    /// Yalnızca YILLIK izin bakiyeye tabi: rapor, mazeret ve ücretsiz
    /// izin ayrı kalemler ve hak edişten düşmez. Kaydı engellemez.
    /// </summary>
    private async Task<string?> DescribeLeaveOverdraftAsync(
        HrLeaveRequest entity, CancellationToken cancellationToken)
    {
        if (entity.LeaveType != HrLeaveType.Annual)
            return null;

        var personnel = await appDb.Personnel.AsNoTracking()
            .Where(x => x.Id == entity.PersonnelId)
            .Select(x => new
            {
                x.EmployeeNumber,
                FullName = x.FirstName + " " + x.LastName,
                x.EmploymentStartDate
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (personnel is null)
            return null;

        // Bu talebin kendisi de sayıma girer: aynı günü iki kez vaat
        // etmemek için bekleyenler dahil hesaplanıyor.
        var days = await hrDb.LeaveRequests.AsNoTracking()
            .Where(x => x.PersonnelId == entity.PersonnelId &&
                        x.Id != entity.Id &&
                        x.LeaveType == HrLeaveType.Annual &&
                        (x.Status == HrApprovalStatus.Approved ||
                         x.Status == HrApprovalStatus.Pending))
            .Select(x => new { x.Status, x.TotalDays })
            .ToListAsync(cancellationToken);

        var balance = LeaveBalanceCalculator.Calculate(
            new LeaveBalanceInput(
                entity.PersonnelId,
                personnel.EmployeeNumber,
                personnel.FullName,
                personnel.EmploymentStartDate,
                days.Where(x => x.Status == HrApprovalStatus.Approved)
                    .Sum(x => x.TotalDays),
                days.Where(x => x.Status == HrApprovalStatus.Pending)
                    .Sum(x => x.TotalDays)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        return LeaveBalanceCalculator.DescribeOverdraft(balance, entity.TotalDays);
    }

    public async Task<HrLeaveResponse> CreateLeaveAsync(
        CreateHrLeaveRequest request, Guid? userId, CancellationToken cancellationToken)
    {
        ValidateLeave(request.LeaveType, request.StartDate, request.EndDate,
            request.TotalDays, request.Reason);
        await EnsurePersonnelAsync(
            request.CompanyId, request.PersonnelId, cancellationToken);
        var entity = new HrLeaveRequest
        {
            CompanyId = request.CompanyId,
            PersonnelId = request.PersonnelId,
            ProjectId = request.ProjectId,
            LeaveType = (HrLeaveType)request.LeaveType,
            StartDate = request.StartDate.Date,
            EndDate = request.EndDate.Date,
            TotalDays = request.TotalDays,
            Reason = request.Reason.Trim(),
            DocumentPath = Clean(request.DocumentPath),
            Status = HrApprovalStatus.Pending,
            CreatedByUserId = userId
        };
        hrDb.LeaveRequests.Add(entity);
        await hrDb.SaveChangesAsync(cancellationToken);

        return ToLeaveResponse(entity) with
        {
            BalanceWarning = await DescribeLeaveOverdraftAsync(entity, cancellationToken)
        };
    }

    public async Task<HrLeaveResponse> UpdateLeaveAsync(
        Guid id, UpdateHrLeaveRequest request, Guid? userId,
        CancellationToken cancellationToken)
    {
        ValidateLeave(request.LeaveType, request.StartDate, request.EndDate,
            request.TotalDays, request.Reason);
        EnsureApprovalStatus(request.Status);
        var entity = await FindLeaveAsync(id, cancellationToken);
        entity.ProjectId = request.ProjectId;
        entity.LeaveType = (HrLeaveType)request.LeaveType;
        entity.StartDate = request.StartDate.Date;
        entity.EndDate = request.EndDate.Date;
        entity.TotalDays = request.TotalDays;
        entity.Reason = request.Reason.Trim();
        entity.DocumentPath = Clean(request.DocumentPath);
        entity.Status = (HrApprovalStatus)request.Status;
        entity.ApprovalNote = Clean(request.ApprovalNote);
        Touch(entity, userId);
        await hrDb.SaveChangesAsync(cancellationToken);
        return ToLeaveResponse(entity);
    }

    public Task<HrLeaveResponse> ApproveLeaveAsync(
        Guid id, Guid? userId, CancellationToken cancellationToken) =>
        ChangeLeaveStatusAsync(
            id, HrApprovalStatus.Approved, null, userId, cancellationToken);

    public Task<HrLeaveResponse> RejectLeaveAsync(
        Guid id, string reason, Guid? userId, CancellationToken cancellationToken) =>
        ChangeLeaveStatusAsync(
            id, HrApprovalStatus.Rejected, RequiredReason(reason),
            userId, cancellationToken);

    public async Task DeleteLeaveAsync(
        Guid id, Guid? userId, CancellationToken cancellationToken)
    {
        var entity = await FindLeaveAsync(id, cancellationToken);
        SoftDelete(entity, userId);
        await hrDb.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HrOvertimeResponse>> GetOvertimesAsync(
        Guid? companyId, Guid? personnelId, Guid? projectId, int? status,
        DateTime? startDate, DateTime? endDate, CancellationToken cancellationToken)
    {
        var query = hrDb.OvertimeRequests.AsNoTracking();
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId);
        if (personnelId.HasValue) query = query.Where(x => x.PersonnelId == personnelId);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId);
        if (status.HasValue) query = query.Where(x => (int)x.Status == status);
        if (startDate.HasValue) query = query.Where(x => x.WorkDate >= startDate.Value.Date);
        if (endDate.HasValue) query = query.Where(x => x.WorkDate <= endDate.Value.Date);
        return (await query.OrderByDescending(x => x.WorkDate)
                .ToListAsync(cancellationToken))
            .Select(ToOvertimeResponse).ToList();
    }

    public async Task<HrOvertimeResponse> CreateOvertimeAsync(
        CreateHrOvertimeRequest request, Guid? userId,
        CancellationToken cancellationToken)
    {
        ValidateOvertime(request.RequestedHours, request.Reason);
        await EnsurePersonnelAsync(
            request.CompanyId, request.PersonnelId, cancellationToken);
        var entity = new HrOvertimeRequest
        {
            CompanyId = request.CompanyId,
            PersonnelId = request.PersonnelId,
            ProjectId = request.ProjectId,
            WorkDate = request.WorkDate.Date,
            RequestedHours = request.RequestedHours,
            IsSundayWork = request.IsSundayWork,
            IsPublicHolidayWork = request.IsPublicHolidayWork,
            IsNightWork = request.IsNightWork,
            Reason = request.Reason.Trim(),
            Status = HrApprovalStatus.Pending,
            CreatedByUserId = userId
        };
        hrDb.OvertimeRequests.Add(entity);
        await hrDb.SaveChangesAsync(cancellationToken);
        return ToOvertimeResponse(entity);
    }

    public async Task<HrOvertimeResponse> UpdateOvertimeAsync(
        Guid id, UpdateHrOvertimeRequest request, Guid? userId,
        CancellationToken cancellationToken)
    {
        ValidateOvertime(request.RequestedHours, request.Reason);
        if (request.ApprovedHours < 0 || request.ApprovedHours > request.RequestedHours)
            throw new InvalidOperationException(
                "Onaylanan mesai saati talep edilen saat aralığında olmalıdır.");
        EnsureApprovalStatus(request.Status);
        var entity = await FindOvertimeAsync(id, cancellationToken);
        entity.ProjectId = request.ProjectId;
        entity.WorkDate = request.WorkDate.Date;
        entity.RequestedHours = request.RequestedHours;
        entity.ApprovedHours = request.ApprovedHours;
        entity.IsSundayWork = request.IsSundayWork;
        entity.IsPublicHolidayWork = request.IsPublicHolidayWork;
        entity.IsNightWork = request.IsNightWork;
        entity.Reason = request.Reason.Trim();
        entity.Status = (HrApprovalStatus)request.Status;
        entity.ApprovalNote = Clean(request.ApprovalNote);
        Touch(entity, userId);
        await hrDb.SaveChangesAsync(cancellationToken);
        return ToOvertimeResponse(entity);
    }

    public async Task<HrOvertimeResponse> ApproveOvertimeAsync(
        Guid id, Guid? userId, CancellationToken cancellationToken)
    {
        var entity = await FindOvertimeAsync(id, cancellationToken);
        entity.Status = HrApprovalStatus.Approved;
        entity.ApprovedHours = entity.ApprovedHours > 0
            ? entity.ApprovedHours
            : entity.RequestedHours;
        SetApproval(entity, userId, null);
        await hrDb.SaveChangesAsync(cancellationToken);
        return ToOvertimeResponse(entity);
    }

    public async Task<HrOvertimeResponse> RejectOvertimeAsync(
        Guid id, string reason, Guid? userId, CancellationToken cancellationToken)
    {
        var entity = await FindOvertimeAsync(id, cancellationToken);
        entity.Status = HrApprovalStatus.Rejected;
        entity.ApprovedHours = 0;
        SetApproval(entity, userId, RequiredReason(reason));
        await hrDb.SaveChangesAsync(cancellationToken);
        return ToOvertimeResponse(entity);
    }

    public async Task DeleteOvertimeAsync(
        Guid id, Guid? userId, CancellationToken cancellationToken)
    {
        var entity = await FindOvertimeAsync(id, cancellationToken);
        SoftDelete(entity, userId);
        await hrDb.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HrAdvanceResponse>> GetAdvancesAsync(
        Guid? companyId, Guid? personnelId, Guid? projectId, int? status,
        DateTime? startDate, DateTime? endDate, CancellationToken cancellationToken)
    {
        var query = hrDb.AdvanceRequests.AsNoTracking();
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId);
        if (personnelId.HasValue) query = query.Where(x => x.PersonnelId == personnelId);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId);
        if (status.HasValue) query = query.Where(x => (int)x.Status == status);
        if (startDate.HasValue) query = query.Where(x => x.RequestDate >= startDate.Value.Date);
        if (endDate.HasValue) query = query.Where(x => x.RequestDate <= endDate.Value.Date);
        return (await query.OrderByDescending(x => x.RequestDate)
                .ToListAsync(cancellationToken))
            .Select(ToAdvanceResponse).ToList();
    }

    public async Task<HrAdvanceResponse> CreateAdvanceAsync(
        CreateHrAdvanceRequest request, Guid? userId,
        CancellationToken cancellationToken)
    {
        ValidateAdvance(
            request.RequestedAmount, request.CurrencyCode,
            request.DeductionInstallmentCount, request.Reason);
        await EnsurePersonnelAsync(
            request.CompanyId, request.PersonnelId, cancellationToken);
        var entity = new HrAdvanceRequest
        {
            CompanyId = request.CompanyId,
            PersonnelId = request.PersonnelId,
            ProjectId = request.ProjectId,
            RequestDate = request.RequestDate.Date,
            RequestedAmount = request.RequestedAmount,
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(),
            DeductionInstallmentCount = request.DeductionInstallmentCount,
            FirstDeductionDate = request.FirstDeductionDate?.Date,
            Reason = request.Reason.Trim(),
            Status = HrApprovalStatus.Pending,
            CreatedByUserId = userId
        };
        hrDb.AdvanceRequests.Add(entity);
        await hrDb.SaveChangesAsync(cancellationToken);
        return ToAdvanceResponse(entity);
    }

    public async Task<HrAdvanceResponse> UpdateAdvanceAsync(
        Guid id, UpdateHrAdvanceRequest request, Guid? userId,
        CancellationToken cancellationToken)
    {
        ValidateAdvance(
            request.RequestedAmount, request.CurrencyCode,
            request.DeductionInstallmentCount, request.Reason);
        if (request.ApprovedAmount < 0 ||
            request.ApprovedAmount > request.RequestedAmount)
            throw new InvalidOperationException(
                "Onaylanan avans tutarı talep edilen tutar aralığında olmalıdır.");
        EnsureApprovalStatus(request.Status);
        var entity = await FindAdvanceAsync(id, cancellationToken);
        entity.ProjectId = request.ProjectId;
        entity.RequestDate = request.RequestDate.Date;
        entity.RequestedAmount = request.RequestedAmount;
        entity.ApprovedAmount = request.ApprovedAmount;
        entity.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant();
        entity.DeductionInstallmentCount = request.DeductionInstallmentCount;
        entity.FirstDeductionDate = request.FirstDeductionDate?.Date;
        entity.Reason = request.Reason.Trim();
        entity.Status = (HrApprovalStatus)request.Status;
        entity.PaymentReference = Clean(request.PaymentReference);
        Touch(entity, userId);
        await hrDb.SaveChangesAsync(cancellationToken);
        return ToAdvanceResponse(entity);
    }

    public async Task<HrAdvanceResponse> ApproveAdvanceAsync(
        Guid id, Guid? userId, CancellationToken cancellationToken)
    {
        var entity = await FindAdvanceAsync(id, cancellationToken);
        entity.Status = HrApprovalStatus.Approved;
        entity.ApprovedAmount = entity.ApprovedAmount > 0
            ? entity.ApprovedAmount
            : entity.RequestedAmount;
        SetApproval(entity, userId, null);
        await hrDb.SaveChangesAsync(cancellationToken);
        return ToAdvanceResponse(entity);
    }

    public async Task<HrAdvanceResponse> RejectAdvanceAsync(
        Guid id, string reason, Guid? userId, CancellationToken cancellationToken)
    {
        var entity = await FindAdvanceAsync(id, cancellationToken);
        entity.Status = HrApprovalStatus.Rejected;
        entity.ApprovedAmount = 0;
        SetApproval(entity, userId, RequiredReason(reason));
        await hrDb.SaveChangesAsync(cancellationToken);
        return ToAdvanceResponse(entity);
    }

    public async Task<HrAdvanceResponse> MarkAdvancePaidAsync(
        Guid id, string? paymentReference, Guid? userId,
        CancellationToken cancellationToken)
    {
        var entity = await FindAdvanceAsync(id, cancellationToken);
        if (entity.Status != HrApprovalStatus.Approved)
            throw new InvalidOperationException(
                "Yalnızca onaylanmış avans ödenmiş olarak işaretlenebilir.");
        entity.Status = HrApprovalStatus.Paid;
        entity.PaidAtUtc = DateTime.UtcNow;
        entity.PaymentReference = Clean(paymentReference);
        Touch(entity, userId);
        await hrDb.SaveChangesAsync(cancellationToken);
        return ToAdvanceResponse(entity);
    }

    public async Task DeleteAdvanceAsync(
        Guid id, Guid? userId, CancellationToken cancellationToken)
    {
        var entity = await FindAdvanceAsync(id, cancellationToken);
        SoftDelete(entity, userId);
        await hrDb.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollResponse>> GetPayrollsAsync(
        Guid? companyId, Guid? personnelId, int? year, int? month, int? status,
        CancellationToken cancellationToken)
    {
        var query = hrDb.PayrollRecords.AsNoTracking();
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId);
        if (personnelId.HasValue) query = query.Where(x => x.PersonnelId == personnelId);
        if (year.HasValue) query = query.Where(x => x.Year == year);
        if (month.HasValue) query = query.Where(x => x.Month == month);
        if (status.HasValue) query = query.Where(x => (int)x.Status == status);
        var records = await query.OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .ThenBy(x => x.PersonnelId)
            .ToListAsync(cancellationToken);

        return await WithTakeHomeAsync(records, cancellationToken);
    }

    public async Task<PayrollResponse> GetPayrollAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var record = await FindPayrollAsync(id, cancellationToken);

        return (await WithTakeHomeAsync([record], cancellationToken))[0];
    }

    /// <summary>
    /// Bordro satırlarına "resmî net + elden + toplam ele geçen"
    /// üçlüsünü OKUMA ANINDA ekler.
    ///
    /// Elden tutar bordro tablosuna YAZILMAZ: bordro salary.view ile
    /// okunuyor, kolon olsaydı yetkisiz kullanıcıya sızardı. Yetki
    /// yoksa elden tablosuna hiç sorgu atılmaz — maskeleme arayüzde
    /// değil, sorgu seviyesinde.
    ///
    /// Resmî tutarlar, SGK matrahı ve muhasebe fişi bu işlemden
    /// ETKİLENMEZ; yalnızca yanıta üç alan eklenir.
    /// </summary>
    private async Task<List<PayrollResponse>> WithTakeHomeAsync(
        IReadOnlyList<HrPayrollRecord> records,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
            return [];

        if (!await extraPaymentVisibility.CanViewExtraPaymentAsync(cancellationToken))
            return records.Select(ToPayrollResponse).ToList();

        var result = new List<PayrollResponse>(records.Count);

        // Elden ödeme dönem bazlı: her bordro ayının SON gününde
        // yürürlükte olan tutar geçerli. "Bugün"e bakmak, geçmiş ay
        // bordrosuna sonradan yapılan zammı yansıtırdı.
        foreach (var monthGroup in records.GroupBy(x => new { x.Year, x.Month }))
        {
            var asOf = new DateTime(
                monthGroup.Key.Year, monthGroup.Key.Month,
                DateTime.DaysInMonth(monthGroup.Key.Year, monthGroup.Key.Month),
                0, 0, 0, DateTimeKind.Utc);

            var extras = await takeHome.LoadEffectiveExtraPaymentsAsync(
                monthGroup.Select(x => x.PersonnelId).Distinct().ToList(),
                asOf,
                cancellationToken);

            foreach (var record in monthGroup)
            {
                var extra = extras.GetValueOrDefault(record.PersonnelId, 0m);

                result.Add(ToPayrollResponse(record) with
                {
                    ExtraPaymentAmount = extra,
                    TotalTakeHome = decimal.Round(
                        record.OfficialNetPayableAmount + extra, 2),
                    ExtraPaymentHidden = false
                });
            }
        }

        // Gruplama sırayı bozduğu için kayıt sırası geri kuruluyor.
        var order = records
            .Select((record, index) => (record.Id, index))
            .ToDictionary(x => x.Id, x => x.index);

        return result.OrderBy(x => order[x.Id]).ToList();
    }

    public async Task<PayrollSummary> GetPayrollSummaryAsync(
        Guid companyId, int year, int month, CancellationToken cancellationToken)
    {
        ValidatePeriod(year, month);
        var items = await hrDb.PayrollRecords.AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.Year == year && x.Month == month)
            .ToListAsync(cancellationToken);
        return new PayrollSummary(
            companyId, year, month, items.Count,
            items.Count(x => x.Status == PayrollStatus.Draft),
            items.Count(x => x.Status == PayrollStatus.Calculated),
            items.Count(x => x.Status == PayrollStatus.Approved),
            items.Count(x => x.Status == PayrollStatus.Paid),
            items.Sum(x => x.GrossSalary),
            items.Sum(x => x.TotalEarnings),
            items.Sum(x => x.TotalDeductions),
            items.Sum(x => x.CompensationAmount),
            items.Sum(x => x.OfficialNetPayableAmount),
            items.Sum(x => x.NetPayableAmount),
            items.Select(x => x.CurrencyCode).FirstOrDefault() ?? "TRY");
    }

    public async Task<CompanyPayrollCalculationResult> CalculateCompanyPayrollAsync(
        CalculateCompanyPayrollRequest request, Guid? userId,
        CancellationToken cancellationToken)
    {
        ValidatePeriod(request.Year, request.Month);
        if (!await appDb.Companies.AnyAsync(
                x => x.Id == request.CompanyId && x.IsActive, cancellationToken))
            throw new InvalidOperationException("Şirket bulunamadı veya pasif.");

        var parameters = await LoadPayrollParametersAsync(
            request.CompanyId, request.Year, cancellationToken);

        var dailyWorkHours = await LoadDailyWorkHoursAsync(
            request.CompanyId, request.Year, cancellationToken);

        var personnel = await appDb.Personnel.AsNoTracking()
            .Where(x => x.CompanyId == request.CompanyId && x.IsActive &&
                        x.Status != EnderunAI.Api.Models.PersonnelStatus.Terminated)
            .Select(x => new { x.Id, x.FullName, Salary = x.MonthlySalary ?? 0m })
            .ToListAsync(cancellationToken);

        var personnelIds = personnel.Select(x => x.Id).ToList();

        // Resmi maaşın tek doğru kaynağı ücret kartı; dönem sonuna kadar
        // yürürlükte olan en güncel kart geçerlidir.
        var periodEnd = new DateTime(
            request.Year, request.Month,
            DateTime.DaysInMonth(request.Year, request.Month),
            0, 0, 0, DateTimeKind.Utc);

        var salaryByPersonnel = await hrDb.SalaryDefinitions.AsNoTracking()
            .Where(x => personnelIds.Contains(x.PersonnelId) &&
                        x.EffectiveStartDate <= periodEnd &&
                        (x.EffectiveEndDate == null || x.EffectiveEndDate >= periodEnd))
            .GroupBy(x => x.PersonnelId)
            .Select(g => g.OrderByDescending(x => x.EffectiveStartDate).First())
            .ToDictionaryAsync(x => x.PersonnelId, cancellationToken);

        var periodStart = new DateTime(
            request.Year, request.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Onaylanmış puantaj günleri: fazla mesai ve tatil çalışması
        // ücrete buradan dönüşür.
        var attendanceByPersonnel = (await appDb.AttendanceRecords.AsNoTracking()
            .Where(x => x.CompanyId == request.CompanyId &&
                        x.IsApproved &&
                        x.WorkDate >= periodStart && x.WorkDate <= periodEnd &&
                        personnelIds.Contains(x.PersonnelId))
            .Select(x => new
            {
                x.PersonnelId,
                x.Status,
                x.OvertimeHours,
                x.SundayHours,
                x.PublicHolidayHours
            })
            .ToListAsync(cancellationToken))
            .GroupBy(x => x.PersonnelId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyCollection<AttendanceDay>)g
                    .Select(x => new AttendanceDay(
                        (EnderunAI.Api.Models.AttendanceStatus)x.Status,
                        x.OvertimeHours,
                        x.SundayHours,
                        x.PublicHolidayHours))
                    .ToList());

        // Kümülatif gelir vergisi matrahı, aynı yıl içindeki önceki
        // ayların bordrolarından devreder — dilim atlamaları buradan
        // doğru yakalanır.
        var cumulativeByPersonnel = await hrDb.PayrollRecords.AsNoTracking()
            .Where(x => x.CompanyId == request.CompanyId &&
                        x.Year == request.Year && x.Month < request.Month &&
                        x.Status != PayrollStatus.Draft)
            .GroupBy(x => x.PersonnelId)
            .Select(g => new { PersonnelId = g.Key, Total = g.Sum(x => x.IncomeTaxBase) })
            .ToDictionaryAsync(x => x.PersonnelId, x => x.Total, cancellationToken);

        var existing = await hrDb.PayrollRecords
            .Where(x => x.CompanyId == request.CompanyId &&
                        x.Year == request.Year && x.Month == request.Month)
            .ToDictionaryAsync(x => x.PersonnelId, cancellationToken);

        // --- Avans taksitleri ---
        // Yalnızca ÖDENMİŞ avans kesilir: verilmemiş parayı geri almak
        // olmaz, onaylı ama ödenmemiş avans bekler.
        var openAdvances = await hrDb.AdvanceRequests.AsNoTracking()
            .Where(x => x.CompanyId == request.CompanyId &&
                        x.Status == HrApprovalStatus.Paid &&
                        x.ApprovedAmount > 0m &&
                        personnelIds.Contains(x.PersonnelId))
            .Select(x => new
            {
                x.Id,
                x.PersonnelId,
                x.ApprovedAmount,
                x.DeductionInstallmentCount,
                x.FirstDeductionDate,
                x.RequestDate,
                x.PaidAtUtc
            })
            .ToListAsync(cancellationToken);

        var advanceIds = openAdvances.Select(x => x.Id).ToList();

        // Bugüne kadarki kesintiler — HESAPLANAN DÖNEM HARİÇ. Bordro
        // yeniden hesaplandığında o dönemin kendi satırı sayılırsa
        // kesinti iki kez düşülmüş olurdu.
        var deductedByAdvance = advanceIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : (await hrDb.AdvanceDeductions.AsNoTracking()
                .Where(x => advanceIds.Contains(x.AdvanceRequestId) &&
                            !(x.Year == request.Year && x.Month == request.Month))
                .GroupBy(x => x.AdvanceRequestId)
                .Select(g => new { Id = g.Key, Total = g.Sum(x => x.Amount) })
                .ToListAsync(cancellationToken))
                .ToDictionary(x => x.Id, x => x.Total);

        var advancesByPersonnel = openAdvances
            .GroupBy(x => x.PersonnelId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => new AdvanceDeductionInput(
                        AdvanceId: x.Id,
                        ApprovedAmount: x.ApprovedAmount,
                        InstallmentCount: x.DeductionInstallmentCount,
                        // İlk kesinti tarihi girilmemişse ödemeyi izleyen
                        // ay: aynı ay kesmek, avansı verip aynı bordroda
                        // geri almak olurdu.
                        FirstDeductionDate: DateOnly.FromDateTime(
                            (x.FirstDeductionDate
                             ?? (x.PaidAtUtc ?? x.RequestDate).AddMonths(1)).Date),
                        AlreadyDeducted: deductedByAdvance.GetValueOrDefault(x.Id)))
                    .ToList());

        // Bu dönemin eski kesinti satırları silinip yeniden yazılır.
        var periodDeductions = advanceIds.Count == 0
            ? []
            : await hrDb.AdvanceDeductions
                .Where(x => advanceIds.Contains(x.AdvanceRequestId) &&
                            x.Year == request.Year && x.Month == request.Month)
                .ToListAsync(cancellationToken);

        // Dönemin eski kesinti satırları temizleniyor: yeniden hesap
        // aynı dönemi baştan yazar, üst üste bindirmez.
        if (periodDeductions.Count > 0)
            hrDb.AdvanceDeductions.RemoveRange(periodDeductions);

        // Kişiye özel ek ücret kalemleri (prim, yemek, yol, tazminat,
        // kesinti). Dönemde yürürlükte olanlar alınır; hangisinin
        // bordroya ve hangi matraha gireceğine kalemin kendi bayrakları
        // karar verir.
        var componentsByPersonnel = (await appDb.HrCompensationComponents
                .AsNoTracking()
                .Where(x => x.CompanyId == request.CompanyId &&
                            x.IsActive &&
                            personnelIds.Contains(x.PersonnelId) &&
                            x.EffectiveStartDate <= periodEnd &&
                            (x.EffectiveEndDate == null ||
                             x.EffectiveEndDate >= periodStart))
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.PersonnelId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CompensationComponentInput>)g
                    .Select(x => new CompensationComponentInput(
                        Name: string.IsNullOrWhiteSpace(x.Name) ? x.Code : x.Name,
                        ComponentType: x.ComponentType,
                        CalculationType: x.CalculationType,
                        PaymentMethod: x.PaymentMethod,
                        Amount: x.Amount,
                        IsAttendanceBased: x.IsAttendanceBased,
                        IsInKindBenefit: x.IsInKindBenefit,
                        IncludeInPayroll: x.IncludeInPayroll,
                        IncludeInSgkBase: x.IncludeInSgkBase,
                        IncludeInIncomeTaxBase: x.IncludeInIncomeTaxBase,
                        IncludeInStampTaxBase: x.IncludeInStampTaxBase,
                        EffectiveStartDate: x.EffectiveStartDate,
                        EffectiveEndDate: x.EffectiveEndDate))
                    .ToList());

        var exemptionCaps = await LoadExemptionCapsAsync(
            request.CompanyId, request.Year, cancellationToken);

        var componentWarnings = new List<string>();

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var missingSalaryDefinition = 0;
        foreach (var person in personnel)
        {
            // Dönemde yürürlükte ücret kartı yoksa bordro üretilmez.
            // Personel kartındaki maaşa düşmek, o alanın net tutması ve
            // tarih bilgisi taşımaması nedeniyle asgari ücret altı ya da
            // kişi henüz işe girmemişken bordro üretmek demekti.
            if (!salaryByPersonnel.TryGetValue(person.Id, out var salaryCard) ||
                !HasUsableAmount(salaryCard))
            {
                missingSalaryDefinition++;
                continue;
            }

            if (existing.TryGetValue(person.Id, out var record))
            {
                if (!request.RecalculateExisting ||
                    record.Status is PayrollStatus.Approved or PayrollStatus.Paid)
                {
                    skipped++;
                    continue;
                }
                updated++;
            }
            else
            {
                record = new HrPayrollRecord
                {
                    CompanyId = request.CompanyId,
                    PersonnelId = person.Id,
                    Year = request.Year,
                    Month = request.Month,
                    CreatedByUserId = userId
                };
                hrDb.PayrollRecords.Add(record);
                created++;
            }

            cumulativeByPersonnel.TryGetValue(person.Id, out var cumulativeBefore);

            // Net esaslı kartta brüt sabit değil: o ayın kümülatif
            // matrahıyla, girilen neti verecek brüt yeniden bulunur.
            // Brüt esaslı kartta davranış hiç değişmez.
            var gross = salaryCard.SalaryBasis == SalaryBasis.Net
                ? PayrollNetToGrossCalculator.CalculateGrossFromNet(
                        parameters,
                        salaryCard.TargetNetSalary,
                        request.Month,
                        cumulativeBefore)
                    .GrossEarnings
                : salaryCard.GrossSalary;

            var rates = BuildSalaryRates(salaryCard, gross, dailyWorkHours);

            attendanceByPersonnel.TryGetValue(person.Id, out var attendanceDays);

            var earnings = AttendanceEarningsCalculator.Calculate(
                rates, attendanceDays ?? Array.Empty<AttendanceDay>());

            // Ek ücret kalemleri: tutarlar bordro alanlarına, istisna
            // kısımları da matrah dışına ayrı ayrı çıkar.
            var compensation = componentsByPersonnel.TryGetValue(
                    person.Id, out var personComponents)
                ? CompensationComponentCalculator.Calculate(
                    personComponents,
                    request.Year,
                    request.Month,
                    gross,
                    earnings.PaidDays,
                    // Saatlik kalemin dayanağı ödenen gün × günlük
                    // çalışma süresi: puantaj normal çalışmayı saat
                    // olarak ayrıca tutmuyor.
                    earnings.PaidDays * dailyWorkHours,
                    dailyWorkHours,
                    exemptionCaps)
                : CompensationResult.Empty;

            foreach (var warning in compensation.Warnings)
            {
                var line = $"{person.FullName}: {warning}";

                if (!componentWarnings.Contains(line))
                    componentWarnings.Add(line);
            }

            record.GrossSalary = gross;
            record.NormalWorkAmount = earnings.NormalWorkAmount;
            record.OvertimeAmount = earnings.OvertimeAmount;
            record.SundayWorkAmount = earnings.SundayWorkAmount;
            record.PublicHolidayAmount = earnings.PublicHolidayAmount;

            record.BonusAmount = compensation.BonusAmount;
            record.MealAmount = compensation.MealAmount;
            record.TravelAmount = compensation.TravelAmount;
            record.OtherEarningAmount = compensation.OtherEarningAmount;
            record.CompensationAmount = compensation.CompensationAmount;
            record.OtherDeductionAmount = compensation.DeductionAmount;

            record.TotalEarnings =
                record.NormalWorkAmount + record.OvertimeAmount +
                record.SundayWorkAmount + record.PublicHolidayAmount +
                record.BonusAmount + record.MealAmount + record.TravelAmount +
                record.OtherEarningAmount + record.CompensationAmount;

            // İKİ GEÇİŞ: avans kesintisi netin üstüne çıkamaz, ama neti
            // bilmek için önce avanssız hesap gerekiyor. Diğer kesintiler
            // (icra vb.) net üzerinden düşüldüğü için ilk geçiş bordronun
            // vergi tarafını hiç değiştirmiyor.
            PayrollInput BuildInput(decimal advanceDeduction) => new(
                Month: request.Month,
                GrossEarnings: record.TotalEarnings,
                SgkExemptEarnings: compensation.SgkExemptEarnings,
                IncomeTaxExemptEarnings: compensation.IncomeTaxExemptEarnings,
                CumulativeIncomeTaxBaseBefore: cumulativeBefore,
                OtherDeductions: advanceDeduction + record.OtherDeductionAmount,
                StampTaxExemptEarnings: compensation.StampTaxExemptEarnings);

            var beforeAdvance = PayrollCalculationService.Calculate(
                parameters, BuildInput(0m));

            var advanceResult = advancesByPersonnel.TryGetValue(
                    person.Id, out var personAdvances)
                ? AdvanceInstallmentCalculator.Resolve(
                    personAdvances, request.Year, request.Month,
                    beforeAdvance.NetPay)
                : new AdvanceDeductionResult([], 0m, 0m);

            record.AdvanceDeduction = advanceResult.Total;

            foreach (var line in advanceResult.Lines)
            {
                hrDb.AdvanceDeductions.Add(new HrAdvanceDeduction
                {
                    CompanyId = request.CompanyId,
                    AdvanceRequestId = line.AdvanceId,
                    PersonnelId = person.Id,
                    Year = request.Year,
                    Month = request.Month,
                    Amount = line.Amount,
                    ScheduledAmount = line.ScheduledAmount
                });
            }

            var result = advanceResult.Total > 0m
                ? PayrollCalculationService.Calculate(
                    parameters, BuildInput(advanceResult.Total))
                : beforeAdvance;

            record.SgkBase = result.SgkBase;
            record.SgkEmployeeDeduction = result.SgkEmployeeAmount;
            record.UnemploymentEmployeeDeduction = result.UnemploymentEmployeeAmount;
            record.IncomeTaxBase = result.IncomeTaxBase;
            record.CumulativeIncomeTaxBase = result.CumulativeIncomeTaxBaseAfter;
            record.IncomeTaxExemption = result.IncomeTaxExemption;
            record.IncomeTaxDeduction = result.IncomeTaxAmount;
            record.StampTaxExemption = result.StampTaxExemption;
            record.StampTaxDeduction = result.StampTaxAmount;
            record.SgkEmployerAmount = result.SgkEmployerAmount;
            record.UnemploymentEmployerAmount = result.UnemploymentEmployerAmount;
            record.TotalEmployerCost = result.TotalEmployerCost;

            record.TotalDeductions = result.TotalDeductions;
            record.OfficialNetPayableAmount = Math.Max(0m, result.NetPay);
            record.ActualPayableAmount = record.OfficialNetPayableAmount;
            record.NetPayableAmount = record.ActualPayableAmount;
            record.CurrencyCode = "TRY";
            record.Status = PayrollStatus.Calculated;
            Touch(record, userId);
        }

        await hrDb.SaveChangesAsync(cancellationToken);

        await RegenerateLaborCostsAsync(
            request.CompanyId, periodStart, periodEnd,
            salaryByPersonnel, dailyWorkHours, userId, cancellationToken);

        var total = await hrDb.PayrollRecords.AsNoTracking()
            .Where(x => x.CompanyId == request.CompanyId &&
                        x.Year == request.Year && x.Month == request.Month)
            .SumAsync(x => x.NetPayableAmount, cancellationToken);
        return new CompanyPayrollCalculationResult(
            request.CompanyId, request.Year, request.Month, personnel.Count,
            created, updated, skipped, total, missingSalaryDefinition,
            componentWarnings);
    }

    public async Task<PayrollResponse> ApprovePayrollAsync(
        Guid id, Guid? userId, CancellationToken cancellationToken)
    {
        var entity = await FindPayrollAsync(id, cancellationToken);
        if (entity.Status != PayrollStatus.Calculated)
            throw new InvalidOperationException(
                "Yalnızca hesaplanmış bordro onaylanabilir.");

        await EnsurePayrollSettingsVerifiedAsync(
            entity.CompanyId, entity.Year, cancellationToken);

        entity.Status = PayrollStatus.Approved;
        entity.ApprovedAtUtc = DateTime.UtcNow;
        entity.ApprovedByUserId = userId;
        Touch(entity, userId);
        await hrDb.SaveChangesAsync(cancellationToken);
        return ToPayrollResponse(entity);
    }

    /// <summary>
    /// Puantajdaki her günü proje/şantiye işçilik maliyetine çevirir.
    /// Bordro yeniden hesaplandığında maliyetler de yeniden üretilir;
    /// kayıtlar puantaj kaydına bağlı olduğu için tekrar oluşmaz.
    /// </summary>
    private async Task RegenerateLaborCostsAsync(
        Guid companyId,
        DateTime periodStart,
        DateTime periodEnd,
        IReadOnlyDictionary<Guid, HrSalaryDefinition> salaryByPersonnel,
        decimal dailyWorkHours,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var attendance = await appDb.AttendanceRecords.AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.IsApproved &&
                        x.ProjectId != null &&
                        x.WorkDate >= periodStart && x.WorkDate <= periodEnd)
            .ToListAsync(cancellationToken);

        if (attendance.Count == 0)
            return;

        var attendanceIds = attendance.Select(x => x.Id).ToList();

        var existingCosts = await appDb.HrProjectLaborCosts
            .Where(x => x.AttendanceRecordId != null &&
                        attendanceIds.Contains(x.AttendanceRecordId!.Value))
            .ToDictionaryAsync(x => x.AttendanceRecordId!.Value, cancellationToken);

        foreach (var day in attendance)
        {
            if (!salaryByPersonnel.TryGetValue(day.PersonnelId, out var salaryCard) ||
                !HasUsableAmount(salaryCard))
            {
                continue;
            }

            // Proje maliyeti brüt üzerinden yürür. Net esaslı kartta
            // ocak esaslı referans brüt kullanılır: maliyet dağıtımı
            // için ay ay brütleştirme yapmak hem pahalı hem gereksiz
            // hassasiyet olurdu.
            var rates = BuildSalaryRates(
                salaryCard, ResolveReferenceGross(salaryCard), dailyWorkHours);

            var dayEarnings = AttendanceEarningsCalculator.CalculateDay(
                rates,
                new AttendanceDay(
                    (EnderunAI.Api.Models.AttendanceStatus)day.Status,
                    day.OvertimeHours,
                    day.SundayHours,
                    day.PublicHolidayHours));

            if (!existingCosts.TryGetValue(day.Id, out var cost))
            {
                cost = new EnderunAI.Api.Models.HrProjectLaborCost
                {
                    CompanyId = companyId,
                    AttendanceRecordId = day.Id,
                    CreatedByUserId = userId
                };
                appDb.HrProjectLaborCosts.Add(cost);
            }

            cost.ProjectId = day.ProjectId!.Value;
            cost.ProjectSiteId = day.ProjectSiteId;
            cost.PersonnelId = day.PersonnelId;
            cost.WorkDate = day.WorkDate;
            cost.WorkItemCode = day.WorkItemCode;
            cost.WorkItemName = day.WorkItemName;
            // Kısım puantajdan taşınır: maliyet analizinde işçilik
            // kısım bazında toplanabilsin.
            cost.ProjectHakedisSectionId = day.ProjectHakedisSectionId;

            cost.NormalHours = day.NormalHours;
            cost.OvertimeHours = day.OvertimeHours;
            cost.SundayHours = day.SundayHours;
            cost.PublicHolidayHours = day.PublicHolidayHours;

            cost.NormalCost = dayEarnings.NormalWorkAmount;
            cost.OvertimeCost = dayEarnings.OvertimeAmount;
            cost.SundayCost = dayEarnings.SundayWorkAmount;
            cost.PublicHolidayCost = dayEarnings.PublicHolidayAmount;
            cost.TotalLaborCost = dayEarnings.TotalEarnings
                + cost.MealCost + cost.AccommodationCost
                + cost.ShuttleCost + cost.OtherCost + cost.CompensationCost;

            cost.CurrencyCode = "TRY";
            cost.UpdatedAtUtc = DateTime.UtcNow;
            cost.UpdatedByUserId = userId;
        }

        await appDb.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Ücret kartında kullanılabilir bir tutar var mı: brüt esaslıda
    /// brüt, net esaslıda hedef net dolu olmalı.
    /// </summary>
    private static bool HasUsableAmount(HrSalaryDefinition card) =>
        card.SalaryBasis == SalaryBasis.Net
            ? card.TargetNetSalary > 0m
            : card.GrossSalary > 0m;

    /// <summary>
    /// Ay bağımsız referans brüt. Net esaslı kartta ocak esasıyla
    /// hesaplanıp karta yazılan değerdir; kart kaydedilirken doldurulur.
    /// Boşsa net tutarına düşülür — hesaplanmamış bir kartta sıfır brütle
    /// maliyet üretmektense yaklaşık ama makul bir değer yeğdir.
    /// </summary>
    private static decimal ResolveReferenceGross(HrSalaryDefinition card) =>
        card.SalaryBasis == SalaryBasis.Net
            ? (card.GrossSalary > 0m ? card.GrossSalary : card.TargetNetSalary)
            : card.GrossSalary;

    /// <summary>
    /// Puantaj birim ücretleri. Kartta elle girilmemişse aylık tutardan
    /// türetilir: günlük = aylık ÷ 30, saatlik = günlük ÷ günlük çalışma
    /// saati. Çalışma saati şirket bordro ayarından gelir.
    /// </summary>
    private static SalaryRates BuildSalaryRates(
        HrSalaryDefinition card, decimal monthlyGross, decimal dailyWorkHours)
    {
        var dailyRate = card.DailyRate > 0m
            ? card.DailyRate
            : decimal.Round(monthlyGross / 30m, 2);

        var hours = dailyWorkHours > 0m ? dailyWorkHours : 7.5m;

        var hourlyRate = card.HourlyRate > 0m
            ? card.HourlyRate
            : decimal.Round(dailyRate / hours, 2);

        return new SalaryRates(
            MonthlyGross: monthlyGross,
            DailyRate: dailyRate,
            HourlyRate: hourlyRate,
            OvertimeMultiplier: card.OvertimeMultiplier,
            SundayMultiplier: card.SundayMultiplier,
            PublicHolidayMultiplier: card.PublicHolidayMultiplier);
    }

    /// <summary>
    /// Günlük normal çalışma süresi. Ayar yoksa yasal haftalık 45 saatin
    /// 6 güne bölümü olan 7,5 saat kullanılır.
    /// </summary>
    private async Task<decimal> LoadDailyWorkHoursAsync(
        Guid companyId, int year, CancellationToken cancellationToken)
    {
        var hours = await appDb.CompanyPayrollSettings
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Year == year)
            .Select(x => (decimal?)x.DailyWorkHours)
            .SingleOrDefaultAsync(cancellationToken);

        return hours is > 0m ? hours.Value : 7.5m;
    }

    /// <summary>
    /// Hesaplama motorunun ihtiyaç duyduğu parametreleri şirketin bordro
    /// ayarlarından okur. Parametre yoksa hesap yapılmaz — sessizce
    /// varsayılan oranla hesaplamak, yanlış bordro üretmek demek.
    /// </summary>
    private async Task<PayrollParameters> LoadPayrollParametersAsync(
        Guid companyId, int year, CancellationToken cancellationToken)
    {
        var settings = await appDb.CompanyPayrollSettings
            .AsNoTracking()
            .Include(x => x.TaxBrackets)
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Year == year, cancellationToken);

        if (settings is null)
        {
            throw new InvalidOperationException(
                $"{year} yılı için bordro parametreleri tanımlı değil. " +
                "Şirket Ayarları → Bordro Parametreleri ekranından tanımlayın.");
        }

        if (settings.TaxBrackets.Count == 0)
        {
            throw new InvalidOperationException(
                $"{year} yılı için gelir vergisi dilimleri tanımlı değil.");
        }

        return new PayrollParameters(
            settings.MinimumWageGross,
            settings.SgkBaseFloor,
            settings.SgkBaseCeiling,
            settings.SgkEmployeeRate,
            settings.UnemploymentEmployeeRate,
            settings.SgkEmployerRate,
            settings.UnemploymentEmployerRate,
            settings.SgkEmployerDiscountEnabled,
            settings.SgkEmployerDiscountPoints,
            settings.StampTaxPerMille,
            settings.MinimumWageIncomeTaxExemptionEnabled,
            settings.MinimumWageStampTaxExemptionEnabled,
            settings.TaxBrackets
                .OrderBy(x => x.Order)
                .Select(x => new PayrollTaxBracketInput(
                    x.LowerBound, x.UpperBound, x.Rate))
                .ToList());
    }

    /// <summary>
    /// Bordronun onaylanabilmesi için o yıla ait bordro parametrelerinin
    /// tanımlı VE doğrulanmış olması şart. Doğrulanmamış (varsayılan)
    /// parametreyle üretilen resmi bordro, eksik prim/vergi beyanı
    /// anlamına geldiği için akış bilinçli olarak fail-closed.
    /// </summary>
    /// <summary>
    /// Nakdî yemek/yol istisna tavanları. Tanımlı değilse null döner ve
    /// istisna uygulanmaz — varsayılana düşmek, o yılın tebliğini
    /// beklemeden sessizce eksik vergi hesaplamak olurdu.
    /// </summary>
    private async Task<CompensationExemptionCaps> LoadExemptionCapsAsync(
        Guid companyId, int year, CancellationToken cancellationToken)
    {
        var settings = await appDb.CompanyPayrollSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Year == year, cancellationToken);

        return settings is null
            ? new CompensationExemptionCaps()
            : new CompensationExemptionCaps(
                MealSgkDaily: settings.MealSgkExemptionDailyCap,
                MealIncomeTaxDaily: settings.MealIncomeTaxExemptionDailyCap,
                TravelSgkDaily: settings.TravelSgkExemptionDailyCap,
                TravelIncomeTaxDaily: settings.TravelIncomeTaxExemptionDailyCap);
    }

    private async Task EnsurePayrollSettingsVerifiedAsync(
        Guid companyId, int year, CancellationToken cancellationToken)
    {
        var settings = await appDb.CompanyPayrollSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Year == year, cancellationToken);

        if (settings is null)
        {
            throw new InvalidOperationException(
                $"{year} yılı için bordro parametreleri tanımlı değil. " +
                "Şirket Ayarları → Bordro Parametreleri ekranından tanımlayın.");
        }

        if (settings.VerifiedAtUtc is null)
        {
            throw new InvalidOperationException(
                $"{year} yılı bordro parametreleri henüz doğrulanmadı. " +
                "Asgari ücret, SGK taban/tavan ve vergi dilimlerini yürürlükteki " +
                "mevzuatla karşılaştırıp Şirket Ayarları → Bordro Parametreleri " +
                "ekranından onaylayın.");
        }
    }

    public async Task<PayrollResponse> CancelPayrollAsync(
        Guid id, string reason, Guid? userId, CancellationToken cancellationToken)
    {
        var entity = await FindPayrollAsync(id, cancellationToken);
        if (entity.Status == PayrollStatus.Paid)
            throw new InvalidOperationException("Ödenmiş bordro iptal edilemez.");
        entity.Status = PayrollStatus.Draft;
        entity.Description = $"İptal: {RequiredReason(reason)}";
        entity.ApprovedAtUtc = null;
        entity.ApprovedByUserId = null;
        Touch(entity, userId);
        await hrDb.SaveChangesAsync(cancellationToken);
        return ToPayrollResponse(entity);
    }

    public async Task<PayrollResponse> MarkPayrollPaidAsync(
        Guid id, MarkPayrollPaidRequest request, Guid? userId,
        CancellationToken cancellationToken)
    {
        var entity = await FindPayrollAsync(id, cancellationToken);
        if (entity.Status != PayrollStatus.Approved)
            throw new InvalidOperationException(
                "Yalnızca onaylanmış bordro ödenmiş olarak işaretlenebilir.");
        entity.Status = PayrollStatus.Paid;
        entity.PaidAtUtc = request.PaymentDate == default
            ? DateTime.UtcNow
            : request.PaymentDate.ToUniversalTime();
        entity.PaymentReference = Clean(request.PaymentReference);
        Touch(entity, userId);
        await hrDb.SaveChangesAsync(cancellationToken);
        return ToPayrollResponse(entity);
    }

    public async Task DeletePayrollAsync(
        Guid id, Guid? userId, CancellationToken cancellationToken)
    {
        var entity = await FindPayrollAsync(id, cancellationToken);
        if (entity.Status == PayrollStatus.Paid)
            throw new InvalidOperationException("Ödenmiş bordro silinemez.");
        SoftDelete(entity, userId);
        await hrDb.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsurePersonnelAsync(
        Guid companyId, Guid personnelId, CancellationToken cancellationToken)
    {
        if (!await appDb.Personnel.AnyAsync(
                x => x.Id == personnelId && x.CompanyId == companyId && x.IsActive,
                cancellationToken))
            throw new InvalidOperationException(
                "Personel bulunamadı, pasif veya seçilen şirkete ait değil.");
    }

    private async Task<HrLeaveResponse> ChangeLeaveStatusAsync(
        Guid id, HrApprovalStatus status, string? note, Guid? userId,
        CancellationToken cancellationToken)
    {
        var entity = await FindLeaveAsync(id, cancellationToken);
        entity.Status = status;
        SetApproval(entity, userId, note);
        await hrDb.SaveChangesAsync(cancellationToken);
        return ToLeaveResponse(entity);
    }

    private async Task<HrLeaveRequest> FindLeaveAsync(
        Guid id, CancellationToken cancellationToken) =>
        await hrDb.LeaveRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
        ?? throw new KeyNotFoundException("İzin talebi bulunamadı.");

    private async Task<HrOvertimeRequest> FindOvertimeAsync(
        Guid id, CancellationToken cancellationToken) =>
        await hrDb.OvertimeRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
        ?? throw new KeyNotFoundException("Fazla mesai kaydı bulunamadı.");

    private async Task<HrAdvanceRequest> FindAdvanceAsync(
        Guid id, CancellationToken cancellationToken) =>
        await hrDb.AdvanceRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
        ?? throw new KeyNotFoundException("Avans talebi bulunamadı.");

    private async Task<HrPayrollRecord> FindPayrollAsync(
        Guid id, CancellationToken cancellationToken) =>
        await hrDb.PayrollRecords.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
        ?? throw new KeyNotFoundException("Bordro kaydı bulunamadı.");

    private static void ValidateLeave(
        int type, DateTime startDate, DateTime endDate, decimal days, string reason)
    {
        if (!Enum.IsDefined(typeof(HrLeaveType), type))
            throw new InvalidOperationException("Geçersiz izin türü.");
        if (endDate.Date < startDate.Date)
            throw new InvalidOperationException(
                "İzin bitiş tarihi başlangıç tarihinden önce olamaz.");
        if (days <= 0)
            throw new InvalidOperationException("İzin gün sayısı sıfırdan büyük olmalıdır.");
        RequiredReason(reason);
    }

    private static void ValidateOvertime(decimal hours, string reason)
    {
        if (hours <= 0 || hours > 24)
            throw new InvalidOperationException(
                "Fazla mesai saati 0 ile 24 arasında olmalıdır.");
        RequiredReason(reason);
    }

    private static void ValidateAdvance(
        decimal amount, string currencyCode, int installmentCount, string reason)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Avans tutarı sıfırdan büyük olmalıdır.");
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Trim().Length != 3)
            throw new InvalidOperationException("Para birimi üç karakter olmalıdır.");
        if (installmentCount is < 1 or > 120)
            throw new InvalidOperationException("Taksit sayısı 1 ile 120 arasında olmalıdır.");
        RequiredReason(reason);
    }

    private static void ValidatePeriod(int year, int month)
    {
        if (year is < 2000 or > 2200 || month is < 1 or > 12)
            throw new InvalidOperationException("Geçersiz bordro dönemi.");
    }

    private static void EnsureApprovalStatus(int status)
    {
        if (!Enum.IsDefined(typeof(HrApprovalStatus), status))
            throw new InvalidOperationException("Geçersiz onay durumu.");
    }

    private static string RequiredReason(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("Gerekçe zorunludur.")
            : value.Trim();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Touch(EnderunAI.Api.Models.BaseEntity entity, Guid? userId)
    {
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedByUserId = userId;
    }

    private static void SoftDelete(
        EnderunAI.Api.Models.BaseEntity entity, Guid? userId)
    {
        entity.IsDeleted = true;
        entity.IsActive = false;
        entity.DeletedAtUtc = DateTime.UtcNow;
        entity.DeletedByUserId = userId;
    }

    private static void SetApproval(
        HrLeaveRequest entity, Guid? userId, string? note)
    {
        entity.ApprovedByUserId = userId;
        entity.ApprovedAtUtc = DateTime.UtcNow;
        entity.ApprovalNote = note;
        Touch(entity, userId);
    }

    private static void SetApproval(
        HrOvertimeRequest entity, Guid? userId, string? note)
    {
        entity.ApprovedByUserId = userId;
        entity.ApprovedAtUtc = DateTime.UtcNow;
        entity.ApprovalNote = note;
        Touch(entity, userId);
    }

    private static void SetApproval(
        HrAdvanceRequest entity, Guid? userId, string? note)
    {
        entity.ApprovedByUserId = userId;
        entity.ApprovedAtUtc = DateTime.UtcNow;
        entity.ApprovalNote = note;
        Touch(entity, userId);
    }

    private static string ApprovalName(HrApprovalStatus status) => status switch
    {
        HrApprovalStatus.Draft => "Taslak",
        HrApprovalStatus.Pending => "Onay Bekliyor",
        HrApprovalStatus.Approved => "Onaylandı",
        HrApprovalStatus.Rejected => "Reddedildi",
        HrApprovalStatus.Paid => "Ödendi",
        HrApprovalStatus.Cancelled => "İptal",
        _ => status.ToString()
    };

    private static string PayrollName(PayrollStatus status) => status switch
    {
        PayrollStatus.Draft => "Taslak",
        PayrollStatus.Calculated => "Hesaplandı",
        PayrollStatus.Approved => "Onaylandı",
        PayrollStatus.Paid => "Ödendi",
        _ => status.ToString()
    };

    private static string LeaveTypeName(HrLeaveType type) => type switch
    {
        HrLeaveType.Annual => "Yıllık İzin",
        HrLeaveType.Excuse => "Mazeret İzni",
        HrLeaveType.Sick => "Sağlık İzni",
        HrLeaveType.Unpaid => "Ücretsiz İzin",
        HrLeaveType.Maternity => "Doğum İzni",
        HrLeaveType.Paternity => "Babalık İzni",
        HrLeaveType.Marriage => "Evlilik İzni",
        HrLeaveType.Bereavement => "Ölüm İzni",
        _ => "Diğer"
    };

    private static HrLeaveResponse ToLeaveResponse(HrLeaveRequest x) =>
        new(x.Id, x.CompanyId, x.PersonnelId, x.ProjectId, (int)x.LeaveType,
            LeaveTypeName(x.LeaveType), x.StartDate, x.EndDate, x.TotalDays,
            x.Reason, x.DocumentPath, (int)x.Status, ApprovalName(x.Status),
            x.ApprovedByUserId, x.ApprovedAtUtc, x.ApprovalNote, x.CreatedAtUtc);

    private static HrOvertimeResponse ToOvertimeResponse(HrOvertimeRequest x) =>
        new(x.Id, x.CompanyId, x.PersonnelId, x.ProjectId, x.WorkDate,
            x.RequestedHours, x.ApprovedHours, x.IsSundayWork,
            x.IsPublicHolidayWork, x.IsNightWork, x.Reason, (int)x.Status,
            ApprovalName(x.Status), x.ApprovedByUserId, x.ApprovedAtUtc,
            x.ApprovalNote, x.CreatedAtUtc);

    private static HrAdvanceResponse ToAdvanceResponse(HrAdvanceRequest x) =>
        new(x.Id, x.CompanyId, x.PersonnelId, x.ProjectId, x.RequestDate,
            x.RequestedAmount, x.ApprovedAmount, x.CurrencyCode,
            x.DeductionInstallmentCount, x.FirstDeductionDate, x.Reason,
            (int)x.Status, ApprovalName(x.Status), x.ApprovedByUserId,
            x.ApprovedAtUtc, x.PaidAtUtc, x.PaymentReference, x.CreatedAtUtc);

    private static PayrollResponse ToPayrollResponse(HrPayrollRecord x) =>
        new(x.Id, x.CompanyId, x.PersonnelId, x.Year, x.Month, x.GrossSalary,
            x.NormalWorkAmount, x.OvertimeAmount, x.SundayWorkAmount,
            x.PublicHolidayAmount, x.BonusAmount, x.MealAmount, x.TravelAmount,
            x.OtherEarningAmount, x.CompensationAmount, x.TotalEarnings,
            x.SgkEmployeeDeduction, x.IncomeTaxDeduction, x.StampTaxDeduction,
            x.AdvanceDeduction, x.OtherDeductionAmount, x.TotalDeductions,
            x.OfficialNetPayableAmount, x.ActualPayableAmount,
            x.NetPayableAmount, x.CurrencyCode, (int)x.Status,
            PayrollName(x.Status), x.ApprovedAtUtc, x.ApprovedByUserId,
            x.PaidAtUtc, x.PaymentReference, x.Description, x.CreatedAtUtc);
}
