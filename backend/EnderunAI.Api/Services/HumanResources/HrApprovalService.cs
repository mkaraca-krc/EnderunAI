using EnderunAI.Api.Contracts.HumanResources;
using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models.HumanResources;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.HumanResources;

public sealed class HrApprovalService(HrDbContext hrDb, AppDbContext appDb)
    : IHrApprovalService
{
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
        return ToLeaveResponse(entity);
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
        return (await query.OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.Month)
                .ThenBy(x => x.PersonnelId)
                .ToListAsync(cancellationToken))
            .Select(ToPayrollResponse).ToList();
    }

    public async Task<PayrollResponse> GetPayrollAsync(
        Guid id, CancellationToken cancellationToken) =>
        ToPayrollResponse(await FindPayrollAsync(id, cancellationToken));

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

        var personnel = await appDb.Personnel.AsNoTracking()
            .Where(x => x.CompanyId == request.CompanyId && x.IsActive &&
                        x.Status != EnderunAI.Api.Models.PersonnelStatus.Terminated)
            .Select(x => new { x.Id, Salary = x.MonthlySalary ?? 0m })
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
                salaryCard.GrossSalary <= 0m)
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

            var gross = salaryCard.GrossSalary;

            record.GrossSalary = gross;
            record.NormalWorkAmount = gross;
            record.TotalEarnings =
                record.NormalWorkAmount + record.OvertimeAmount +
                record.SundayWorkAmount + record.PublicHolidayAmount +
                record.BonusAmount + record.MealAmount + record.TravelAmount +
                record.OtherEarningAmount + record.CompensationAmount;

            cumulativeByPersonnel.TryGetValue(person.Id, out var cumulativeBefore);

            var result = PayrollCalculationService.Calculate(parameters, new PayrollInput(
                Month: request.Month,
                GrossEarnings: record.TotalEarnings,
                // Yemek/yol istisnaları kişiye özel kalemlerle birlikte
                // Faz E3'te devreye girecek; şu an tüm kazanç primlidir.
                SgkExemptEarnings: 0m,
                IncomeTaxExemptEarnings: 0m,
                CumulativeIncomeTaxBaseBefore: cumulativeBefore,
                OtherDeductions: record.AdvanceDeduction + record.OtherDeductionAmount));

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
        var total = await hrDb.PayrollRecords.AsNoTracking()
            .Where(x => x.CompanyId == request.CompanyId &&
                        x.Year == request.Year && x.Month == request.Month)
            .SumAsync(x => x.NetPayableAmount, cancellationToken);
        return new CompanyPayrollCalculationResult(
            request.CompanyId, request.Year, request.Month, personnel.Count,
            created, updated, skipped, total, missingSalaryDefinition);
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
