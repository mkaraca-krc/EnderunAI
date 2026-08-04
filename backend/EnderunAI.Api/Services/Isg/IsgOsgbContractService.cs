using EnderunAI.Api.Contracts.Isg;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Isg;

public interface IIsgOsgbContractService
{
    Task<IReadOnlyCollection<IsgOsgbContractListItem>> GetAllAsync(
        Guid? companyId, CancellationToken cancellationToken);

    Task<IsgOsgbContractDetail> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IsgOsgbContractDetail> CreateAsync(
        CreateIsgOsgbContractRequest request, CancellationToken cancellationToken);

    Task<IsgOsgbContractDetail> UpdateAsync(
        Guid id, UpdateIsgOsgbContractRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Bir projenin hakedişine önerilecek İSG kesintisi. Kayıt yazmaz;
    /// hakediş ekranı bunu kesinti satırına doldurur, kullanıcı onaylar.
    /// </summary>
    Task<IsgDeductionSuggestionResponse> SuggestDeductionAsync(
        Guid companyId, Guid projectId, DateOnly periodDate,
        CancellationToken cancellationToken);
}

public sealed class IsgOsgbContractService(AppDbContext db) : IIsgOsgbContractService
{
    /// <summary>Sözleşme bitişine bu kadar kala uyarı verilir.</summary>
    private const int ExpiryWarningDays = 30;

    public async Task<IReadOnlyCollection<IsgOsgbContractListItem>> GetAllAsync(
        Guid? companyId, CancellationToken cancellationToken)
    {
        var query = db.IsgOsgbContracts.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        var contracts = await query
            .Include(x => x.CurrentAccount)
            .Include(x => x.Experts)
            .OrderByDescending(x => x.StartDate)
            .ToListAsync(cancellationToken);

        var today = Today();

        return contracts.Select(x => new IsgOsgbContractListItem(
            x.Id, x.ContractNumber, x.CurrentAccountId, x.CurrentAccount.Title,
            x.StartDate, x.EndDate, (int)x.BillingType, BillingTypeName(x.BillingType),
            x.MonthlyFee, x.PerPersonFee, x.CurrencyCode,
            StatusName(x, today), DaysUntilExpiry(x, today),
            x.Experts.Count)).ToList();
    }

    public async Task<IsgOsgbContractDetail> GetByIdAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var contract = await db.IsgOsgbContracts
            .AsNoTracking()
            .Include(x => x.CurrentAccount)
            .Include(x => x.Experts)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("OSGB sözleşmesi bulunamadı.");

        // OSGB faturaları ayrı tutulmuyor: carinin tedarikçi faturaları.
        var invoices = await db.SupplierInvoices
            .AsNoTracking()
            .Where(x => x.CompanyId == contract.CompanyId &&
                        x.SupplierCurrentAccountId == contract.CurrentAccountId)
            .OrderByDescending(x => x.InvoiceDate)
            .Select(x => new IsgOsgbInvoiceResponse(
                x.Id, x.InternalNumber, x.InvoiceNumber, x.InvoiceDate,
                x.GrandTotal, x.CurrencyCode, (int)x.Status,
                x.Status == SupplierInvoiceStatus.Draft ? "Taslak"
                    : x.Status == SupplierInvoiceStatus.PendingApproval ? "Onay Bekliyor"
                    : x.Status == SupplierInvoiceStatus.Approved ? "Onaylandı"
                    : x.Status == SupplierInvoiceStatus.Rejected ? "Reddedildi"
                    : "İptal"))
            .ToListAsync(cancellationToken);

        return MapDetail(contract, invoices, Today());
    }

    public async Task<IsgOsgbContractDetail> CreateAsync(
        CreateIsgOsgbContractRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(
            request.CompanyId, request.CurrentAccountId, request.ContractNumber,
            request.StartDate, request.EndDate, request.BillingType,
            request.MonthlyFee, request.PerPersonFee, null, cancellationToken);

        var contract = new IsgOsgbContract
        {
            CompanyId = request.CompanyId,
            CurrentAccountId = request.CurrentAccountId,
            ContractNumber = request.ContractNumber.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            BillingType = (OsgbBillingType)request.BillingType,
            MonthlyFee = request.MonthlyFee,
            PerPersonFee = request.PerPersonFee,
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(),
            Notes = Normalize(request.Notes)
        };

        ApplyExperts(contract, request.Experts);

        db.IsgOsgbContracts.Add(contract);
        await db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(contract.Id, cancellationToken);
    }

    public async Task<IsgOsgbContractDetail> UpdateAsync(
        Guid id, UpdateIsgOsgbContractRequest request, CancellationToken cancellationToken)
    {
        var contract = await db.IsgOsgbContracts
            .Include(x => x.Experts)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("OSGB sözleşmesi bulunamadı.");

        await ValidateAsync(
            contract.CompanyId, request.CurrentAccountId, request.ContractNumber,
            request.StartDate, request.EndDate, request.BillingType,
            request.MonthlyFee, request.PerPersonFee, id, cancellationToken);

        contract.CurrentAccountId = request.CurrentAccountId;
        contract.ContractNumber = request.ContractNumber.Trim();
        contract.StartDate = request.StartDate;
        contract.EndDate = request.EndDate;
        contract.BillingType = (OsgbBillingType)request.BillingType;
        contract.MonthlyFee = request.MonthlyFee;
        contract.PerPersonFee = request.PerPersonFee;
        contract.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant();
        contract.Notes = Normalize(request.Notes);
        contract.UpdatedAtUtc = DateTime.UtcNow;

        db.IsgOsgbExperts.RemoveRange(contract.Experts);
        contract.Experts.Clear();
        ApplyExperts(contract, request.Experts);

        await db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(contract.Id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var contract = await db.IsgOsgbContracts
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("OSGB sözleşmesi bulunamadı.");

        contract.IsDeleted = true;
        contract.DeletedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IsgDeductionSuggestionResponse> SuggestDeductionAsync(
        Guid companyId, Guid projectId, DateOnly periodDate,
        CancellationToken cancellationToken)
    {
        var contracts = await db.IsgOsgbContracts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.StartDate)
            .ToListAsync(cancellationToken);

        var contract = contracts.FirstOrDefault(
            x => OsgbDeductionCalculator.CoversPeriod(x, periodDate));

        if (contract is null)
        {
            return NoSuggestion(
                "Bu dönemi kapsayan aktif bir OSGB sözleşmesi yok. " +
                "Kesintiyi elle girebilirsiniz.");
        }

        var personCount = contract.BillingType == OsgbBillingType.PerPerson
            ? await CountActiveSitePersonnelAsync(projectId, periodDate, cancellationToken)
            : 0;

        var suggestion = OsgbDeductionCalculator.Calculate(
            contract, periodDate, personCount);

        if (suggestion is null)
        {
            return NoSuggestion(
                contract.BillingType == OsgbBillingType.PerPerson
                    ? "Kişi başı sözleşmede bu dönemde projenin şantiyelerinde " +
                      "aktif atanmış personel bulunamadı; tutar hesaplanamadı."
                    : "Sözleşmede aylık bedel tanımlı değil; tutar hesaplanamadı.");
        }

        return new IsgDeductionSuggestionResponse(
            HasSuggestion: true,
            DeductionType: (int)HakedisDeductionType.OhsContribution,
            Description: suggestion.Description,
            ManualAmount: suggestion.Amount,
            PersonCount: suggestion.PersonCount,
            OsgbContractId: contract.Id,
            ContractNumber: contract.ContractNumber,
            Reason: null);
    }

    /// <summary>
    /// Dönem içinde projenin şantiyelerinde aktif atanmış personel sayısı.
    /// Atama dönem başlamadan bitmişse veya dönem bittikten sonra
    /// başlamışsa sayılmaz.
    /// </summary>
    private async Task<int> CountActiveSitePersonnelAsync(
        Guid projectId, DateOnly periodDate, CancellationToken cancellationToken)
    {
        // Şantiye ataması tarihleri DateTime; dönem sınırlarını aynı
        // türe çeviriyoruz ki karşılaştırma veritabanında yapılabilsin.
        var periodStart = new DateTime(
            periodDate.Year, periodDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1).AddTicks(-1);

        return await db.ProjectSiteAssignments
            .AsNoTracking()
            .Where(x => x.ProjectSite.ProjectId == projectId &&
                        x.StartDate <= periodEnd &&
                        (x.EndDate == null || x.EndDate >= periodStart))
            .Select(x => x.PersonnelId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    private async Task ValidateAsync(
        Guid companyId, Guid currentAccountId, string contractNumber,
        DateOnly startDate, DateOnly? endDate, int billingType,
        decimal monthlyFee, decimal perPersonFee, Guid? excludeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contractNumber))
            throw new ArgumentException("Sözleşme numarası zorunludur.");

        if (endDate is DateOnly end && end < startDate)
            throw new ArgumentException("Bitiş tarihi başlangıçtan önce olamaz.");

        if (!Enum.IsDefined(typeof(OsgbBillingType), billingType))
            throw new ArgumentException("Geçersiz bedel tipi.");

        if (monthlyFee < 0m || perPersonFee < 0m)
            throw new ArgumentException("Bedel negatif olamaz.");

        var accountExists = await db.CurrentAccounts.AnyAsync(
            x => x.Id == currentAccountId && x.CompanyId == companyId, cancellationToken);

        if (!accountExists)
            throw new ArgumentException("OSGB carisi bulunamadı.");

        var number = contractNumber.Trim();

        var duplicate = await db.IsgOsgbContracts.AnyAsync(
            x => x.CompanyId == companyId &&
                 x.ContractNumber == number &&
                 (excludeId == null || x.Id != excludeId),
            cancellationToken);

        if (duplicate)
            throw new ArgumentException($"'{number}' numaralı sözleşme zaten kayıtlı.");
    }

    private static void ApplyExperts(
        IsgOsgbContract contract, IReadOnlyCollection<IsgOsgbExpertRequest>? requests)
    {
        if (requests is null)
            return;

        foreach (var request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.FullName))
                throw new ArgumentException("Uzman/hekim adı zorunludur.");

            if (!Enum.IsDefined(typeof(OsgbExpertType), request.ExpertType))
                throw new ArgumentException("Geçersiz görevli tipi.");

            contract.Experts.Add(new IsgOsgbExpert
            {
                ExpertType = (OsgbExpertType)request.ExpertType,
                FullName = request.FullName.Trim(),
                CertificateNumber = Normalize(request.CertificateNumber),
                ExpertClass = Normalize(request.ExpertClass)?.ToUpperInvariant(),
                Phone = Normalize(request.Phone),
                Email = Normalize(request.Email),
                StartDate = request.StartDate,
                EndDate = request.EndDate
            });
        }
    }

    private static IsgOsgbContractDetail MapDetail(
        IsgOsgbContract contract,
        IReadOnlyCollection<IsgOsgbInvoiceResponse> invoices,
        DateOnly today) =>
        new(
            contract.Id,
            contract.CompanyId,
            contract.ContractNumber,
            contract.CurrentAccountId,
            contract.CurrentAccount.Title,
            contract.CurrentAccount.TaxNumber,
            contract.StartDate,
            contract.EndDate,
            (int)contract.BillingType,
            BillingTypeName(contract.BillingType),
            contract.MonthlyFee,
            contract.PerPersonFee,
            contract.CurrencyCode,
            contract.Notes,
            StatusName(contract, today),
            DaysUntilExpiry(contract, today),
            contract.Experts
                .OrderBy(x => x.ExpertType)
                .ThenBy(x => x.FullName)
                .Select(x => new IsgOsgbExpertResponse(
                    x.Id, (int)x.ExpertType, ExpertTypeName(x.ExpertType), x.FullName,
                    x.CertificateNumber, x.ExpertClass, x.Phone, x.Email,
                    x.StartDate, x.EndDate,
                    x.StartDate <= today && (x.EndDate is null || x.EndDate >= today)))
                .ToList(),
            invoices);

    private static string StatusName(IsgOsgbContract contract, DateOnly today)
    {
        if (contract.StartDate > today)
            return "Başlamadı";

        if (contract.EndDate is not DateOnly end)
            return "Aktif";

        if (end < today)
            return "Süresi doldu";

        return end.DayNumber - today.DayNumber <= ExpiryWarningDays
            ? "Süresi doluyor"
            : "Aktif";
    }

    private static int? DaysUntilExpiry(IsgOsgbContract contract, DateOnly today) =>
        contract.EndDate is DateOnly end ? end.DayNumber - today.DayNumber : null;

    private static string BillingTypeName(OsgbBillingType type) => type switch
    {
        OsgbBillingType.MonthlyFixed => "Aylık sabit",
        OsgbBillingType.PerPerson => "Kişi başı",
        _ => "—"
    };

    private static string ExpertTypeName(OsgbExpertType type) => type switch
    {
        OsgbExpertType.SafetySpecialist => "İş güvenliği uzmanı",
        OsgbExpertType.WorkplacePhysician => "İşyeri hekimi",
        OsgbExpertType.OtherHealthStaff => "Diğer sağlık personeli",
        _ => "—"
    };

    private static IsgDeductionSuggestionResponse NoSuggestion(string reason) =>
        new(false, (int)HakedisDeductionType.OhsContribution,
            string.Empty, 0m, null, null, null, reason);

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
