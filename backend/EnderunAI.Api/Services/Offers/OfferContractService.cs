using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Offers;

/// <summary>Kazanılan teklifin sözleşme künyesi ve hedef projesi.</summary>
/// <param name="ProjectId">
/// Mevcut projeye bağlanacaksa proje; boşsa yeni proje açılır.
/// </param>
/// <param name="BranchId">Yeni projede zorunlu.</param>
/// <param name="Code">Yeni projenin kodu.</param>
/// <param name="Name">Yeni projenin adı; boşsa teklif başlığı.</param>
/// <param name="ContractNumber">Sözleşme no.</param>
/// <param name="ContractDate">İmza tarihi.</param>
/// <param name="ContractAmount">Sözleşme bedeli; boşsa teklif tutarı.</param>
/// <param name="ContractType">
/// Sözleşme tipi; belirtilmezse teklif tipinden türetilir.
/// </param>
/// <param name="PlannedStartDate">İşe başlama.</param>
/// <param name="PlannedEndDate">Termin.</param>
/// <param name="CashRetentionRate">Nakit teminat kesintisi (%).</param>
/// <param name="VatRate">KDV (%).</param>
/// <param name="WithholdingTaxRate">Stopaj (%).</param>
/// <param name="MaterialDeductionRate">Malzeme kesintisi (%).</param>
/// <param name="ProgressPaymentPeriod">Hakediş periyodu.</param>
/// <param name="PaymentTerms">Ödeme koşulları.</param>
/// <param name="City">İl.</param>
/// <param name="District">İlçe.</param>
/// <param name="Address">Adres.</param>
/// <param name="TransferToBoq">İcmal aktarılsın mı.</param>
/// <param name="BoqName">İcmal adı.</param>
public sealed record OfferContractInput(
    Guid? ProjectId,
    Guid? BranchId,
    string? Code,
    string? Name,
    string? ContractNumber,
    DateTime? ContractDate,
    decimal? ContractAmount,
    ProjectContractType? ContractType,
    DateTime? PlannedStartDate,
    DateTime? PlannedEndDate,
    decimal CashRetentionRate,
    decimal VatRate,
    decimal WithholdingTaxRate,
    decimal MaterialDeductionRate,
    ProjectProgressPaymentPeriod ProgressPaymentPeriod,
    string? PaymentTerms,
    string? City,
    string? District,
    string? Address,
    bool TransferToBoq,
    string? BoqName);

/// <summary>Sözleşme açma sonucu.</summary>
/// <param name="ProjectId">Oluşan ya da bağlanan proje.</param>
/// <param name="ProjectCode">Proje kodu.</param>
/// <param name="ProjectCreated">Yeni proje mi açıldı.</param>
/// <param name="WarehouseId">Yeni projeyle açılan şantiye deposu.</param>
/// <param name="ProjectBoqId">Aktarılan icmal.</param>
/// <param name="BoqNumber">İcmal numarası.</param>
/// <param name="BoqItemCount">İcmale giren kalem sayısı.</param>
/// <param name="BoqTotalAmount">İcmal toplamı.</param>
/// <param name="Warnings">Dikkat edilmesi gerekenler.</param>
public sealed record OfferContractResult(
    Guid ProjectId,
    string ProjectCode,
    bool ProjectCreated,
    Guid? WarehouseId,
    Guid? ProjectBoqId,
    string? BoqNumber,
    int BoqItemCount,
    decimal BoqTotalAmount,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Kazanılan teklifi sözleşmeye ve projeye bağlar.
///
/// İki yol var ve ikisi farklı iş gerçeğine karşılık gelir:
///
/// YENİ PROJE — işi ilk kez alıyoruz. Sözleşme künyesi projeyi kurar,
/// şantiye deposu açılır, icmal teklif kalemlerinden üretilir.
///
/// MEVCUT PROJEYE BAĞLAMA — aynı işverende EK İŞ kazandık. Bu durumda
/// projenin sözleşme künyesi KORUNUR, üzerine yazılmaz: asıl sözleşme
/// no, bedeli ve termini ek işin künyesiyle ezilirse projenin mali
/// geçmişi yalan söylemeye başlar. Yalnız ek icmal açılır ve teklif
/// projeye bağlanır.
///
/// Tamamı tek transaction: proje açılıp icmal aktarılamazsa ortada
/// sahipsiz bir proje kalmasın.
/// </summary>
public sealed class OfferContractService(
    AppDbContext db,
    OfferBoqTransferService boqTransfer)
{
    public async Task<OfferContractResult> CreateAsync(
        Guid offerId,
        OfferContractInput input,
        CancellationToken cancellationToken)
    {
        var offer = await db.Offers
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == offerId, cancellationToken)
            ?? throw new KeyNotFoundException("Teklif bulunamadı.");

        if (offer.Status != OfferStatus.Won)
        {
            throw new InvalidOperationException(
                "Sözleşme yalnız kazanılmış teklif için açılabilir. " +
                "Önce teklifi Kazanıldı olarak işaretleyin.");
        }

        if (offer.ProjectId.HasValue && input.ProjectId != offer.ProjectId)
        {
            throw new InvalidOperationException(
                "Bu teklif zaten bir projeye bağlı; sözleşmesi ikinci kez " +
                "açılamaz.");
        }

        var warnings = new List<string>();

        await using var transaction = await db.Database
            .BeginTransactionAsync(cancellationToken);

        try
        {
            Project project;
            Guid? warehouseId = null;
            bool created;

            if (input.ProjectId is Guid existingId)
            {
                project = await db.Projects
                    .SingleOrDefaultAsync(x => x.Id == existingId, cancellationToken)
                    ?? throw new KeyNotFoundException("Proje bulunamadı.");

                if (project.CompanyId != offer.CompanyId)
                {
                    throw new InvalidOperationException(
                        "Teklif ile proje farklı şirketlere ait.");
                }

                if (project.IsArchived)
                {
                    throw new InvalidOperationException(
                        "Arşivlenmiş projeye ek iş bağlanamaz.");
                }

                created = false;

                // Ek iş: asıl sözleşme künyesi korunuyor.
                warnings.Add(
                    $"Ek iş olarak {project.Code} projesine bağlandı; " +
                    "projenin mevcut sözleşme künyesi değiştirilmedi.");

                // Kaynak teklif bağı yalnız boşsa yazılır; projenin
                // doğduğu teklif ek işin teklifiyle değiştirilemez.
                project.SourceOfferId ??= offer.Id;
            }
            else
            {
                created = true;
                project = await CreateProjectAsync(offer, input, cancellationToken);

                var warehouse = new Warehouse
                {
                    CompanyId = project.CompanyId,
                    BranchId = project.BranchId,
                    ProjectId = project.Id,
                    Code = $"{project.Code}-DEPO",
                    Name = $"{project.Name} Şantiye Deposu",
                    Type = WarehouseType.Site,
                    Address = project.Address
                };

                db.Warehouses.Add(warehouse);
                warehouseId = warehouse.Id;
            }

            // Teklif artık projeye bağlı: zincirin iki yönü de kurulur.
            offer.ProjectId = project.Id;

            await db.SaveChangesAsync(cancellationToken);

            Guid? boqId = null;
            string? boqNumber = null;
            var itemCount = 0;
            var boqTotal = 0m;

            if (input.TransferToBoq)
            {
                var transfer = await boqTransfer.TransferAsync(
                    offer.Id,
                    project.Id,
                    input.BoqName,
                    null,
                    cancellationToken);

                boqId = transfer.ProjectBoqId;
                boqNumber = transfer.BoqNumber;
                itemCount = transfer.ItemCount;
                boqTotal = transfer.TotalAmount;

                warnings.AddRange(transfer.Warnings);
            }
            else
            {
                warnings.Add(
                    "İcmal aktarılmadı; hakediş için icmali sonra " +
                    "oluşturmanız gerekir.");
            }

            await transaction.CommitAsync(cancellationToken);

            return new OfferContractResult(
                project.Id,
                project.Code,
                created,
                warehouseId,
                boqId,
                boqNumber,
                itemCount,
                boqTotal,
                warnings);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<Project> CreateProjectAsync(
        Offer offer, OfferContractInput input, CancellationToken cancellationToken)
    {
        if (input.BranchId is not Guid branchId)
        {
            throw new InvalidOperationException(
                "Yeni proje için şube seçilmelidir.");
        }

        var branchOk = await db.Branches.AnyAsync(
            x => x.Id == branchId &&
                 x.CompanyId == offer.CompanyId &&
                 x.IsActive,
            cancellationToken);

        if (!branchOk)
        {
            throw new InvalidOperationException(
                "Seçilen şube teklifin şirketine ait değil veya pasif.");
        }

        if (offer.CounterpartyCurrentAccountId is not Guid employerId)
        {
            throw new InvalidOperationException(
                "Teklifin karşı tarafı (işveren / ana yüklenici) seçilmeden " +
                "sözleşme açılamaz.");
        }

        var code = string.IsNullOrWhiteSpace(input.Code)
            ? throw new InvalidOperationException("Proje kodu zorunludur.")
            : input.Code.Trim().ToUpperInvariant();

        var codeTaken = await db.Projects.AnyAsync(
            x => x.CompanyId == offer.CompanyId && x.Code == code,
            cancellationToken);

        if (codeTaken)
            throw new InvalidOperationException("Bu proje kodu zaten kullanılıyor.");

        // Sözleşme tipi verilmediyse teklif tipinden türetilir; ikisi
        // aynı ayrımı taşıyor ve elle yeniden seçtirmek yazım hatasına
        // açık olurdu.
        var contractType = input.ContractType ?? offer.Kind switch
        {
            OfferKind.UnitPrice => ProjectContractType.UnitPrice,
            OfferKind.LumpSum => ProjectContractType.LumpSum,
            _ => ProjectContractType.Undetermined
        };

        var project = new Project
        {
            CompanyId = offer.CompanyId,
            BranchId = branchId,
            EmployerCurrentAccountId = employerId,
            Code = code,
            Name = string.IsNullOrWhiteSpace(input.Name)
                ? offer.Title
                : input.Name.Trim(),
            ContractNumber = Clean(input.ContractNumber),
            ContractDate = AsUtc(input.ContractDate),

            // Bedel verilmezse teklif tutarı esas alınır: sözleşme
            // teklif üzerinden imzalandığında ikisi zaten aynıdır ve
            // sıfır bedelli proje kâr analizini bozardı.
            ContractAmount = input.ContractAmount ?? offer.GrandTotal,

            CurrencyCode = offer.Currency,
            ContractType = contractType,
            VatRate = input.VatRate,
            CashRetentionRate = input.CashRetentionRate,
            WithholdingTaxRate = input.WithholdingTaxRate,
            MaterialDeductionRate = input.MaterialDeductionRate,
            ProgressPaymentPeriod = input.ProgressPaymentPeriod,
            PaymentTerms = Clean(input.PaymentTerms),
            PlannedStartDate = AsUtc(input.PlannedStartDate),
            PlannedEndDate = AsUtc(input.PlannedEndDate),
            City = Clean(input.City),
            District = Clean(input.District),
            Address = Clean(input.Address),

            // Sözleşmesi imzalanan iş artık yürüyor.
            Status = ProjectStatus.Active,
            HealthStatus = ProjectHealthStatus.Green,

            // İcmalle yürüyen proje: teklif kalemleri icmale aktarıldığı
            // için hakediş referansı hazır.
            UsesContractSummary = input.TransferToBoq,

            SourceOfferId = offer.Id
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken);

        return project;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime? AsUtc(DateTime? value) =>
        value is null
            ? null
            : DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Utc);
}
