using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hakedis;
using EnderunAI.Api.Services.Subcontractors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record CreateSubcontractorProgressPaymentRequest(
    Guid SubcontractorContractId,
    string? ProgressPaymentNumber,
    DateTime PeriodStartDate,
    DateTime PeriodEndDate,
    DateTime ProgressPaymentDate,
    string? Notes);

/// <summary>Birim fiyatlı kalem; miktar KÜMÜLATİF girilir.</summary>
public sealed record SubcontractorItemRequest(
    Guid? Id,
    Guid? ProjectHakedisSectionId,
    Guid? ProjectBoqItemId,
    string PositionCode,
    string Description,
    string Unit,
    decimal ContractQuantity,
    decimal SuggestedQuantity,
    decimal AgreedQuantity,
    decimal UnitPrice,
    string? Notes);

/// <summary>Götürü kısım; ilerleme KÜMÜLATİF yüzde olarak girilir.</summary>
public sealed record SubcontractorSectionRequest(
    Guid ProjectHakedisSectionId,
    decimal SectionAmount,
    decimal SuggestedProgressRate,
    decimal AgreedProgressRate,
    string? Notes);

public sealed record SubcontractorDeductionLineRequest(
    string Name,
    decimal UnitPrice,
    decimal Quantity,
    decimal VatRate);

public sealed record SubcontractorDeductionRequest(
    int DeductionType,
    string Description,
    decimal Rate,
    decimal? ManualAmount,
    string? SuggestionBasis,
    IReadOnlyList<SubcontractorDeductionLineRequest>? Lines);

public sealed record SaveSubcontractorProgressPaymentRequest(
    IReadOnlyList<SubcontractorItemRequest>? Items,
    IReadOnlyList<SubcontractorSectionRequest>? Sections,
    IReadOnlyList<SubcontractorDeductionRequest>? Deductions,
    string? Notes);

/// <summary>
/// Taşeron hakedişi — işveren hakedişimizin ters yönü.
///
/// İKİ KATMANLI: satırlarda öneri (puantaj/saha) ve mutabakat ayrı
/// tutulur; HESAP HER ZAMAN MUTABAKATLA yapılır. Öneri sessizce
/// ödemeye dönüşseydi sahanın tahmini imzalanmış rakam yerine geçerdi.
///
/// Kesinti KALEMLERİ sözleşmenin kapsam tiklerinden gelir, kullanıcı
/// listeyi kurmaz; TUTARLAR öneridir ve elle düzeltilebilir.
/// </summary>
[ApiController]
[Authorize]
[Route("api/subcontractor-progress-payments")]
public sealed class SubcontractorProgressPaymentsController(
    AppDbContext db,
    SubcontractorDeductionPlanner planner,
    SubcontractorLedgerService ledger) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? subcontractorContractId,
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken)
    {
        var query = db.SubcontractorProgressPayments.AsNoTracking();

        if (subcontractorContractId is Guid contractId)
            query = query.Where(x => x.SubcontractorContractId == contractId);

        if (projectId is Guid project)
        {
            query = query.Where(x =>
                x.SubcontractorContract.ProjectId == project);
        }

        var items = await query
            .OrderByDescending(x => x.PeriodNumber)
            .Select(x => new
            {
                x.Id,
                x.SubcontractorContractId,
                ContractNumber = x.SubcontractorContract.ContractNumber,
                SubcontractorTitle = x.SubcontractorContract.CurrentAccount.Title,
                ProjectName = x.SubcontractorContract.Project.Name,
                x.ProgressPaymentNumber,
                x.PeriodNumber,
                x.PeriodStartDate,
                x.PeriodEndDate,
                x.ProgressPaymentDate,
                Status = (int)x.Status,
                StatusName = StatusName(x.Status),
                x.CurrencyCode,
                x.ContractAmount,
                x.PreviousAmount,
                x.CurrentAmount,
                x.CumulativeAmount,
                x.TotalDeductionAmount,
                x.GrossPayableAmount,
                x.NetPayableAmount
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorView)]
    public async Task<IActionResult> GetById(
        Guid id, CancellationToken cancellationToken)
    {
        var item = await db.SubcontractorProgressPayments
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Sections)
            .Include(x => x.Deductions)
            .Include(x => x.SubcontractorContract)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Taşeron hakedişi bulunamadı." });

        var sectionNames = await db.ProjectHakedisSections
            .AsNoTracking()
            .Where(x => x.ProjectId == item.SubcontractorContract.ProjectId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return Ok(new
        {
            item.Id,
            item.SubcontractorContractId,
            item.ProgressPaymentNumber,
            item.PeriodNumber,
            item.PeriodStartDate,
            item.PeriodEndDate,
            item.ProgressPaymentDate,
            item.Year,
            item.Month,
            Status = (int)item.Status,
            StatusName = StatusName(item.Status),
            item.CurrencyCode,
            ContractType = (int)item.SubcontractorContract.ContractType,
            item.ContractAmount,
            item.PreviousAmount,
            item.CurrentAmount,
            item.CumulativeAmount,
            item.TotalDeductionAmount,
            item.GrossPayableAmount,
            item.NetPayableAmount,
            item.Notes,
            Items = item.Items.OrderBy(x => x.LineNumber).Select(x => new
            {
                x.Id,
                x.ProjectHakedisSectionId,
                SectionName = x.ProjectHakedisSectionId.HasValue
                    ? sectionNames.GetValueOrDefault(x.ProjectHakedisSectionId.Value)
                    : null,
                x.ProjectBoqItemId,
                x.LineNumber,
                x.PositionCode,
                x.Description,
                x.Unit,
                x.ContractQuantity,
                x.PreviousQuantity,
                x.SuggestedQuantity,
                x.AgreedQuantity,
                x.CurrentQuantity,
                x.UnitPrice,
                x.PreviousAmount,
                x.CurrentAmount,
                x.CumulativeAmount,
                x.Notes
            }),
            Sections = item.Sections.OrderBy(x => x.Order).Select(x => new
            {
                x.Id,
                x.ProjectHakedisSectionId,
                SectionName = sectionNames.GetValueOrDefault(
                    x.ProjectHakedisSectionId),
                x.Order,
                x.SectionAmount,
                x.PreviousProgressRate,
                x.SuggestedProgressRate,
                x.AgreedProgressRate,
                x.PreviousAmount,
                x.CurrentAmount,
                x.CumulativeAmount,
                x.Notes
            }),
            Deductions = item.Deductions.OrderBy(x => x.LineNumber).Select(x => new
            {
                x.Id,
                x.LineNumber,
                x.DeductionType,
                x.Description,
                x.Rate,
                x.CumulativeBaseAmount,
                x.PreviousAmount,
                x.CumulativeAmount,
                x.Amount,
                x.IsManualAmount,
                x.SuggestionBasis
            })
        });
    }

    /// <summary>
    /// Yeni hakediş açar ve kesinti kalemlerini SÖZLEŞMEDEN kurar.
    /// Götürü sözleşmede kısımlar da sözleşmeden kopyalanır; birim
    /// fiyatlıda kalemler kullanıcı tarafından girilir.
    /// </summary>
    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorManage)]
    public async Task<IActionResult> Create(
        CreateSubcontractorProgressPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var contract = await db.SubcontractorContracts
            .Include(x => x.Sections)
            .SingleOrDefaultAsync(
                x => x.Id == request.SubcontractorContractId, cancellationToken);

        if (contract is null)
            return BadRequest(new { message = "Taşeron sözleşmesi bulunamadı." });

        if (contract.Status == SubcontractorContractStatus.Cancelled)
            return BadRequest(new { message = "İptal edilmiş sözleşmeye hakediş açılamaz." });

        if (request.PeriodEndDate.Date < request.PeriodStartDate.Date)
        {
            return BadRequest(new
            {
                message = "Dönem bitişi başlangıcından önce olamaz."
            });
        }

        // Önceki hakedişler: kümülatif zincir buradan kuruluyor. İptal
        // edilenler sayılmaz.
        var previous = await db.SubcontractorProgressPayments
            .AsNoTracking()
            .Where(x => x.SubcontractorContractId == contract.Id &&
                        x.Status != SubcontractorProgressPaymentStatus.Cancelled)
            .OrderByDescending(x => x.PeriodNumber)
            .FirstOrDefaultAsync(cancellationToken);

        var periodNumber = (previous?.PeriodNumber ?? 0) + 1;
        var previousAmount = previous?.CumulativeAmount ?? 0m;

        var number = string.IsNullOrWhiteSpace(request.ProgressPaymentNumber)
            ? $"{contract.ContractNumber}-HK{periodNumber:00}"
            : request.ProgressPaymentNumber.Trim();

        var duplicate = await db.SubcontractorProgressPayments.AnyAsync(
            x => x.CompanyId == contract.CompanyId &&
                 x.ProgressPaymentNumber == number,
            cancellationToken);

        if (duplicate)
            return Conflict(new { message = "Bu hakediş numarası zaten kullanılıyor." });

        // Dönemin yılı/ayı bitiş tarihinden alınır: ay ortasında
        // başlayan bir dönem bittiği aya aittir.
        var periodEnd = UtcDate(request.PeriodEndDate);

        var item = new SubcontractorProgressPayment
        {
            CompanyId = contract.CompanyId,
            SubcontractorContractId = contract.Id,
            ProgressPaymentNumber = number,
            PeriodNumber = periodNumber,
            PeriodStartDate = UtcDate(request.PeriodStartDate),
            PeriodEndDate = periodEnd,
            ProgressPaymentDate = UtcDate(request.ProgressPaymentDate),
            Year = periodEnd.Year,
            Month = periodEnd.Month,
            CurrencyCode = contract.CurrencyCode,
            ContractAmount = contract.ContractAmount,
            PreviousAmount = previousAmount,
            Notes = string.IsNullOrWhiteSpace(request.Notes)
                ? null
                : request.Notes.Trim()
        };

        // Götürüde kısımlar sözleşmeden kopyalanır; önceki ilerleme
        // zincirden gelir.
        if (contract.ContractType == ProjectContractType.LumpSum)
        {
            var previousRates = previous is null
                ? new Dictionary<Guid, decimal>()
                : await db.SubcontractorProgressPaymentSections
                    .AsNoTracking()
                    .Where(x => x.SubcontractorProgressPaymentId == previous.Id)
                    .ToDictionaryAsync(
                        x => x.ProjectHakedisSectionId,
                        x => x.AgreedProgressRate,
                        cancellationToken);

            foreach (var section in contract.Sections.OrderBy(x => x.Order))
            {
                var previousRate = previousRates.GetValueOrDefault(
                    section.ProjectHakedisSectionId);

                item.Sections.Add(new SubcontractorProgressPaymentSection
                {
                    ProjectHakedisSectionId = section.ProjectHakedisSectionId,
                    Order = section.Order,
                    SectionAmount = section.SectionAmount,
                    PreviousProgressRate = previousRate,
                    AgreedProgressRate = previousRate,
                    PreviousAmount = decimal.Round(
                        section.SectionAmount * previousRate / 100m, 2)
                });
            }
        }

        await AddPlannedDeductionsAsync(
            item, contract, previous?.Id, previousAmount, cancellationToken);

        RecalculateWork(item, contract.ContractType);
        RecalculateTotals(item);

        db.SubcontractorProgressPayments.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { item.Id, item.ProgressPaymentNumber, message = "Taşeron hakedişi açıldı." });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorManage)]
    public async Task<IActionResult> Update(
        Guid id,
        SaveSubcontractorProgressPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.SubcontractorProgressPayments
            .Include(x => x.Items)
            .Include(x => x.Sections)
            .Include(x => x.Deductions)
            .Include(x => x.SubcontractorContract)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Taşeron hakedişi bulunamadı." });

        // Onaylanmış hakediş kilitli: ödeme ve muhasebe ona dayanıyor.
        if (item.Status is SubcontractorProgressPaymentStatus.Approved
            or SubcontractorProgressPaymentStatus.Paid
            or SubcontractorProgressPaymentStatus.Cancelled)
        {
            return BadRequest(new
            {
                message =
                    "Onaylanmış, ödenmiş veya iptal edilmiş hakediş " +
                    "değiştirilemez."
            });
        }

        var contractType = item.SubcontractorContract.ContractType;

        var failure = contractType == ProjectContractType.LumpSum
            ? ApplySections(item, request.Sections ?? [])
            : await ApplyItemsAsync(item, request.Items ?? [], cancellationToken);

        if (failure is not null)
            return BadRequest(new { message = failure });

        // SIRA ÖNEMLİ: oransal kesintiler (teminat) kümülatif iş
        // tutarını taban alıyor. Kesintiler iş toplamlarından ÖNCE
        // hesaplanırsa taban bir dönem geriden gelir ve dönemin ilk
        // kaydında sıfır olduğu için teminat hiç kesilmez.
        RecalculateWork(item, contractType);

        ApplyDeductions(item, request.Deductions ?? []);

        await ApplyAdvanceOffsetSuggestionAsync(item, cancellationToken);

        item.Notes = string.IsNullOrWhiteSpace(request.Notes)
            ? null
            : request.Notes.Trim();

        RecalculateTotals(item);
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Taşeron hakedişi güncellendi.",
            item.CurrentAmount,
            item.TotalDeductionAmount,
            item.NetPayableAmount
        });
    }

    [HttpPost("{id:guid}/approve")]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorApprove)]
    public async Task<IActionResult> Approve(
        Guid id, CancellationToken cancellationToken)
    {
        var item = await db.SubcontractorProgressPayments
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Taşeron hakedişi bulunamadı." });

        if (item.Status != SubcontractorProgressPaymentStatus.Draft &&
            item.Status != SubcontractorProgressPaymentStatus.Submitted)
        {
            return BadRequest(new
            {
                message = "Yalnızca taslak veya sunulmuş hakediş onaylanabilir."
            });
        }

        item.Status = SubcontractorProgressPaymentStatus.Approved;
        item.ApprovedAtUtc = DateTime.UtcNow;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Taşeron hakedişi onaylandı." });
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorManage)]
    public async Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken)
    {
        var item = await db.SubcontractorProgressPayments
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Taşeron hakedişi bulunamadı." });

        // Onaylı hakediş silinmez, iptal edilir: kümülatif zincirdeki
        // yerini ve izini koruyor.
        if (item.Status is SubcontractorProgressPaymentStatus.Approved
            or SubcontractorProgressPaymentStatus.Paid)
        {
            return BadRequest(new
            {
                message = "Onaylanmış hakediş silinemez."
            });
        }

        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Taşeron hakedişi silindi." });
    }

    // ---------- Uygulama ----------

    private async Task AddPlannedDeductionsAsync(
        SubcontractorProgressPayment item,
        SubcontractorContract contract,
        Guid? previousPaymentId,
        decimal previousWorkAmount,
        CancellationToken cancellationToken)
    {
        var planned = await planner.PlanAsync(
            contract, item.Year, item.Month, previousWorkAmount, cancellationToken);

        // Aynı türden önceki kesintilerin toplamı — kümülatif minha.
        var previousByType = previousPaymentId is Guid previousId
            ? await db.SubcontractorProgressPaymentDeductions
                .AsNoTracking()
                .Where(x => x.SubcontractorProgressPaymentId == previousId)
                .GroupBy(x => x.DeductionType)
                .Select(g => new
                {
                    DeductionType = g.Key,
                    Total = g.Sum(x => x.CumulativeAmount)
                })
                .ToDictionaryAsync(x => x.DeductionType, x => x.Total, cancellationToken)
            : [];

        var lineNumber = 1;

        foreach (var plan in planned)
        {
            item.Deductions.Add(new SubcontractorProgressPaymentDeduction
            {
                LineNumber = lineNumber++,
                DeductionType = plan.DeductionType,
                Description = plan.Description,
                Rate = plan.Rate,
                PreviousAmount = previousByType.GetValueOrDefault(plan.DeductionType),
                Amount = plan.Amount ?? 0m,
                IsManualAmount = plan.Amount.HasValue && plan.Rate <= 0m,
                SuggestionBasis = plan.Basis
            });
        }
    }

    private async Task<string?> ApplyItemsAsync(
        SubcontractorProgressPayment payment,
        IReadOnlyList<SubcontractorItemRequest> requested,
        CancellationToken cancellationToken)
    {
        if (requested.Any(x => x.AgreedQuantity < 0m || x.UnitPrice < 0m))
            return "Mutabakat miktarı ve birim fiyat negatif olamaz.";

        var sectionIds = requested
            .Where(x => x.ProjectHakedisSectionId.HasValue)
            .Select(x => x.ProjectHakedisSectionId!.Value)
            .Distinct()
            .ToArray();

        if (sectionIds.Length > 0)
        {
            var projectId = payment.SubcontractorContract.ProjectId;

            var validCount = await db.ProjectHakedisSections.CountAsync(
                x => sectionIds.Contains(x.Id) && x.ProjectId == projectId,
                cancellationToken);

            if (validCount != sectionIds.Length)
                return "Seçilen icmal kısımlarının tamamı bu projeye ait değil.";
        }

        // Önceki dönemin kümülatif miktarları — kümülatif zincir.
        var previousQuantities = await LoadPreviousQuantitiesAsync(
            payment, cancellationToken);

        foreach (var existing in payment.Items.ToList())
        {
            if (requested.Any(x => x.Id == existing.Id))
                continue;

            payment.Items.Remove(existing);
            db.SubcontractorProgressPaymentItems.Remove(existing);
        }

        var lineNumber = 1;

        foreach (var line in requested)
        {
            var entity = line.Id is Guid lineId
                ? payment.Items.SingleOrDefault(x => x.Id == lineId)
                : null;

            if (entity is null)
            {
                entity = new SubcontractorProgressPaymentItem
                {
                    SubcontractorProgressPaymentId = payment.Id
                };

                payment.Items.Add(entity);

                // Durum AÇIKÇA Added: BaseEntity yapıcıda Id atadığı için,
                // izlenen bir üst kaydın navigasyonuna eklenen yeni satırı
                // EF "anahtarı dolu, demek ki mevcut kayıt" sayıp Modified
                // izliyor ve olmayan satıra UPDATE atıyor (0 satır →
                // DbUpdateConcurrencyException). Üst kayıt da yeni
                // eklendiğinde tüm graf Added olduğundan sorun çıkmıyor;
                // burada üst kayıt zaten veritabanında.
                db.Entry(entity).State = EntityState.Added;
            }

            var key = line.PositionCode.Trim();
            var previousQuantity = previousQuantities.GetValueOrDefault(key);

            var result = SubcontractorHakedisCalculator.CalculateItem(
                new SubcontractorItemInput(
                    line.ContractQuantity,
                    previousQuantity,
                    line.AgreedQuantity,
                    line.UnitPrice));

            entity.ProjectHakedisSectionId = line.ProjectHakedisSectionId;
            entity.ProjectBoqItemId = line.ProjectBoqItemId;
            entity.LineNumber = lineNumber++;
            entity.PositionCode = key;
            entity.Description = line.Description.Trim();
            entity.Unit = line.Unit.Trim();
            entity.ContractQuantity = line.ContractQuantity;
            entity.SuggestedQuantity = line.SuggestedQuantity;
            entity.PreviousQuantity = result.PreviousQuantity;
            entity.AgreedQuantity = result.AgreedQuantity;
            entity.CurrentQuantity = result.CurrentQuantity;
            entity.UnitPrice = line.UnitPrice;
            entity.PreviousAmount = result.PreviousAmount;
            entity.CurrentAmount = result.CurrentAmount;
            entity.CumulativeAmount = result.CumulativeAmount;
            entity.Notes = string.IsNullOrWhiteSpace(line.Notes)
                ? null
                : line.Notes.Trim();
        }

        return null;
    }

    /// <summary>
    /// Önceki hakedişteki kümülatif miktarlar, poz koduna göre.
    /// Kullanıcının girdiği "önceki" değerine güvenilmez: kümülatif
    /// zincir kayıttan okunur.
    /// </summary>
    private async Task<Dictionary<string, decimal>> LoadPreviousQuantitiesAsync(
        SubcontractorProgressPayment payment,
        CancellationToken cancellationToken)
    {
        var previous = await db.SubcontractorProgressPayments
            .AsNoTracking()
            .Where(x => x.SubcontractorContractId == payment.SubcontractorContractId &&
                        x.PeriodNumber < payment.PeriodNumber &&
                        x.Status != SubcontractorProgressPaymentStatus.Cancelled)
            .OrderByDescending(x => x.PeriodNumber)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (previous == Guid.Empty)
            return [];

        return await db.SubcontractorProgressPaymentItems
            .AsNoTracking()
            .Where(x => x.SubcontractorProgressPaymentId == previous)
            .GroupBy(x => x.PositionCode)
            .Select(g => new
            {
                PositionCode = g.Key,
                Quantity = g.Sum(x => x.AgreedQuantity)
            })
            .ToDictionaryAsync(x => x.PositionCode, x => x.Quantity, cancellationToken);
    }

    private string? ApplySections(
        SubcontractorProgressPayment payment,
        IReadOnlyList<SubcontractorSectionRequest> requested)
    {
        if (requested.Any(x => x.AgreedProgressRate is < 0m or > 100m))
        {
            return
                "Götürüde ilerleme yüzdesi 0 ile 100 arasında olmalıdır; " +
                "%100 üstü iş ilave iş sözleşmesidir.";
        }

        foreach (var line in requested)
        {
            var entity = payment.Sections.SingleOrDefault(
                x => x.ProjectHakedisSectionId == line.ProjectHakedisSectionId);

            // Sözleşmede olmayan kısım hakedişe eklenemez: sözleşme
            // kapsamı dışına ödeme yapılırdı.
            if (entity is null)
                continue;

            // İlerleme geriye alınamaz: önceki dönemde %60 kabul
            // edilmişse bu dönem %50 demek, ödenmiş işi geri istemektir
            // ve mutabakat konusudur.
            if (line.AgreedProgressRate < entity.PreviousProgressRate)
            {
                return
                    $"İlerleme geriye alınamaz: önceki dönemde " +
                    $"%{TurkishAmountFormat.Rate(entity.PreviousProgressRate)} kabul " +
                    $"edilmiş, %{TurkishAmountFormat.Rate(line.AgreedProgressRate)} " +
                    "girilmiş.";
            }

            var result = SubcontractorHakedisCalculator.CalculateSection(
                new SubcontractorSectionInput(
                    entity.SectionAmount,
                    entity.PreviousProgressRate,
                    line.AgreedProgressRate));

            entity.SuggestedProgressRate = line.SuggestedProgressRate;
            entity.AgreedProgressRate = result.AgreedProgressRate;
            entity.PreviousAmount = result.PreviousAmount;
            entity.CurrentAmount = result.CurrentAmount;
            entity.CumulativeAmount = result.CumulativeAmount;
            entity.Notes = string.IsNullOrWhiteSpace(line.Notes)
                ? null
                : line.Notes.Trim();
        }

        return null;
    }

    /// <summary>
    /// Kesinti satırlarını günceller.
    ///
    /// TÜM satırlar üzerinden dönülüyor, yalnızca istemcinin
    /// gönderdikleri değil: ORANSAL kesinti (teminat) türetilmiş bir
    /// değerdir, kullanıcı girdisi değil. Yalnızca gönderilenler
    /// hesaplansaydı, arayüz her kaydetmede bütün kesinti satırlarını
    /// geri göndermedikçe teminat sessizce sıfır kalırdı — ve bunu
    /// kimse fark etmezdi, çünkü satır ekranda duruyor.
    ///
    /// Elle girilmiş satırlar (IsManualAmount) istek gelmediği sürece
    /// olduğu gibi bırakılır.
    /// </summary>
    private void ApplyDeductions(
        SubcontractorProgressPayment payment,
        IReadOnlyList<SubcontractorDeductionRequest> requested)
    {
        foreach (var entity in payment.Deductions)
        {
            var line = requested.SingleOrDefault(
                x => x.DeductionType == entity.DeductionType);

            if (line is null)
            {
                // İstek yok: yalnızca oransal satır yeniden hesaplanır.
                // Tutarı öneriden gelen (İSG, SGK) ya da elle girilmiş
                // satırlara dokunulmaz.
                if (entity.Rate <= 0m || entity.IsManualAmount)
                    continue;

                var recalculated = HakedisCalculationService.CalculateDeduction(
                    new HakedisCalculationService.DeductionInput(
                        DeductionType: entity.DeductionType,
                        Description: entity.Description,
                        Rate: entity.Rate,
                        CumulativeBaseAmount: payment.CumulativeAmount,
                        PreviousAmount: entity.PreviousAmount));

                entity.CumulativeBaseAmount = recalculated.CumulativeBaseAmount;
                entity.CumulativeAmount = recalculated.CumulativeAmount;
                entity.Amount = recalculated.Amount;
                continue;
            }

            var result = HakedisCalculationService.CalculateDeduction(
                new HakedisCalculationService.DeductionInput(
                    DeductionType: line.DeductionType,
                    Description: line.Description,
                    Rate: line.Rate,
                    CumulativeBaseAmount: payment.CumulativeAmount,
                    PreviousAmount: entity.PreviousAmount,
                    ManualAmount: line.ManualAmount,
                    Lines: (line.Lines ?? [])
                        .Select(x => new HakedisCalculationService.DeductionLineInput(
                            x.Name, x.UnitPrice, x.Quantity, x.VatRate))
                        .ToList()));

            entity.Description = result.Description;
            entity.Rate = result.Rate;
            entity.CumulativeBaseAmount = result.CumulativeBaseAmount;
            entity.CumulativeAmount = result.CumulativeAmount;
            entity.Amount = result.Amount;
            entity.IsManualAmount = result.IsManualAmount;

            if (line.SuggestionBasis is not null)
                entity.SuggestionBasis = line.SuggestionBasis;
        }
    }

    /// <summary>
    /// İş toplamlarını satırlardan hesaplar. Toplam hiçbir zaman
    /// kullanıcıdan alınmaz: satırlarla toplamın ayrışması, hakedişte
    /// en pahalı hata türüdür.
    ///
    /// Kesintilerden ÖNCE çalışmalı — oransal kesintiler kümülatif iş
    /// tutarını taban alıyor.
    /// </summary>
    private static void RecalculateWork(
        SubcontractorProgressPayment payment, ProjectContractType contractType)
    {
        var current = contractType == ProjectContractType.LumpSum
            ? payment.Sections.Sum(x => x.CurrentAmount)
            : payment.Items.Sum(x => x.CurrentAmount);

        var cumulative = contractType == ProjectContractType.LumpSum
            ? payment.Sections.Sum(x => x.CumulativeAmount)
            : payment.Items.Sum(x => x.CumulativeAmount);

        payment.CurrentAmount = decimal.Round(current, 2);
        payment.CumulativeAmount = decimal.Round(cumulative, 2);
    }

    /// <summary>Kesinti toplamı ve ödeme satırı.</summary>
    private static void RecalculateTotals(SubcontractorProgressPayment payment)
    {
        payment.TotalDeductionAmount = decimal.Round(
            payment.Deductions.Sum(x => x.Amount), 2);

        var (gross, net, _) = SubcontractorHakedisCalculator.CalculatePayment(
            payment.CurrentAmount, payment.TotalDeductionAmount);

        payment.GrossPayableAmount = gross;
        payment.NetPayableAmount = net;
    }

    /// <summary>
    /// Avans mahsubu satırına ÖNERİ tutarını yazar.
    ///
    /// Yalnızca kullanıcı o satıra elle bir tutar girmediyse çalışır:
    /// elle girilen mahsup (IsManualAmount) bir daha ezilmez — sıfır
    /// girilmişse bile, çünkü "bu dönem mahsup yapma" da bir karardır.
    ///
    /// Öneri YALNIZCA RESMÎ avanslardan hesaplanıyor (canViewCash:
    /// false). Elden avanslar da toplansaydı, mahsup tutarı resmî açık
    /// avanstan büyük çıkar ve hakedişi okuyan yetkisiz kullanıcı elden
    /// avans verildiğini bu farktan anlardı. Elden avansın mahsubu,
    /// yetkisi olan kişinin elle gireceği bir karar.
    /// </summary>
    private async Task ApplyAdvanceOffsetSuggestionAsync(
        SubcontractorProgressPayment payment, CancellationToken cancellationToken)
    {
        var line = payment.Deductions.SingleOrDefault(
            x => x.DeductionType == (int)HakedisDeductionType.AdvanceOffset);

        if (line is null || line.IsManualAmount)
            return;

        var suggestion = await ledger.SuggestAdvanceOffsetAsync(
            payment.SubcontractorContractId,
            payment.CurrentAmount,
            canViewCash: false,
            cancellationToken);

        if (suggestion is not (var amount, var basis))
            return;

        line.Amount = amount;
        line.CumulativeAmount = decimal.Round(line.PreviousAmount + amount, 2);
        line.SuggestionBasis = basis;
    }

    private static DateTime UtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private static string StatusName(SubcontractorProgressPaymentStatus status) =>
        status switch
        {
            SubcontractorProgressPaymentStatus.Draft => "Taslak",
            SubcontractorProgressPaymentStatus.Submitted => "Sunuldu",
            SubcontractorProgressPaymentStatus.Approved => "Onaylandı",
            SubcontractorProgressPaymentStatus.Paid => "Ödendi",
            SubcontractorProgressPaymentStatus.Cancelled => "İptal",
            _ => "Bilinmiyor"
        };
}
