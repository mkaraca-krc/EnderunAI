using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hakedis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/project-boqs")]
public sealed class ProjectBoqController(
    AppDbContext db,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] ProjectBoqStatus? status,
        CancellationToken cancellationToken)
    {
        var query = db.ProjectBoqs.AsNoTracking().AsQueryable();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.ProjectId,
                x.Project.Code,
                ProjectName = x.Project.Name,
                x.BoqNumber,
                x.Name,
                x.RevisionNumber,
                x.Status,
                x.IsCurrentRevision,
                x.CurrencyCode,
                x.TotalAmount,
                x.CreatedAtUtc,
                ItemCount = x.Items.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(items.Select(x => new
        {
            x.Id,
            x.CompanyId,
            x.ProjectId,
            ProjectCode = x.Code,
            x.ProjectName,
            x.BoqNumber,
            x.Name,
            x.RevisionNumber,
            RevisionCode = $"R{x.RevisionNumber}",
            x.Status,
            x.IsCurrentRevision,
            x.CurrencyCode,
            x.TotalAmount,
            x.ItemCount,
            x.CreatedAtUtc
        }));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await db.ProjectBoqs
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Project)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Sözleşme icmali bulunamadı." });

        // Kısım adları icmalle birlikte dönüyor ki arayüz ayrı bir
        // istek atmasın ve satırlar kısım altında gruplanabilsin.
        var sections = await db.ProjectHakedisSections
            .AsNoTracking()
            .Where(x => x.ProjectId == item.ProjectId)
            .OrderBy(x => x.Order)
            .Select(x => new { x.Id, x.Order, x.Name, x.Code })
            .ToListAsync(cancellationToken);

        var lines = item.Items
            .OrderBy(x => x.LineNumber)
            .Select(x => new
            {
                x.Id,
                x.EngineeringPositionId,
                x.ProjectHakedisSectionId,
                x.LineNumber,
                x.PositionCode,
                x.Description,
                x.Unit,
                x.ContractQuantity,
                x.MaterialUnitPrice,
                x.LaborUnitPrice,
                x.OverheadUnitPrice,
                x.UnitPrice,
                x.TotalAmount,
                x.ItemType,
                x.Category,
                x.Notes
            })
            .ToList();

        // Kısım ara toplamları. Kısma bağlanmamış kalemler ayrı bir
        // "Kısımsız" grubunda toplanıyor — sessizce genel toplamdan
        // düşmeleri, icmalin tutmadığı izlenimi verirdi.
        var sectionSummaries = sections
            .Select(section => new
            {
                section.Id,
                section.Order,
                section.Name,
                section.Code,
                ItemCount = lines.Count(x => x.ProjectHakedisSectionId == section.Id),
                MaterialAmount = lines
                    .Where(x => x.ProjectHakedisSectionId == section.Id)
                    .Sum(x => decimal.Round(x.ContractQuantity * x.MaterialUnitPrice, 2)),
                LaborAmount = lines
                    .Where(x => x.ProjectHakedisSectionId == section.Id)
                    .Sum(x => decimal.Round(x.ContractQuantity * x.LaborUnitPrice, 2)),
                OverheadAmount = lines
                    .Where(x => x.ProjectHakedisSectionId == section.Id)
                    .Sum(x => decimal.Round(x.ContractQuantity * x.OverheadUnitPrice, 2)),
                TotalAmount = lines
                    .Where(x => x.ProjectHakedisSectionId == section.Id)
                    .Sum(x => x.TotalAmount)
            })
            .ToList();

        var unsectionedCount = lines.Count(x => x.ProjectHakedisSectionId is null);

        return Ok(new
        {
            item.Id,
            item.CompanyId,
            item.ProjectId,
            ProjectCode = item.Project.Code,
            ProjectName = item.Project.Name,
            item.BoqNumber,
            item.Name,
            item.RevisionNumber,
            RevisionCode = $"R{item.RevisionNumber}",
            item.Status,
            item.IsCurrentRevision,
            item.IsContractBaseline,
            // Onaylı icmal kilitlidir; arayüz düzenlemeyi kapatır.
            IsLocked = item.Status != ProjectBoqStatus.Draft,
            item.CurrencyCode,
            item.TotalAmount,
            ItemCount = lines.Count,
            item.CreatedAtUtc,
            item.Description,
            item.Notes,
            item.ApprovedAtUtc,
            Sections = sectionSummaries,
            UnsectionedItemCount = unsectionedCount,
            UnsectionedAmount = lines
                .Where(x => x.ProjectHakedisSectionId is null)
                .Sum(x => x.TotalAmount),
            Items = lines
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.HakedisCreate)]
    public async Task<IActionResult> Create(
        CreateProjectBoqRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return BadRequest(new
            {
                message = "Metraj en az bir kalem içermelidir."
            });
        }

        var duplicate = await db.ProjectBoqs.AnyAsync(
            x => x.CompanyId == request.CompanyId &&
                 x.BoqNumber == request.BoqNumber &&
                 x.RevisionNumber == request.RevisionNumber,
            cancellationToken);
        if (duplicate)
        {
            return Conflict(new
            {
                message = "Bu numara ve revizyon için metraj zaten mevcut."
            });
        }

        var sectionError = await ValidateSectionsAsync(
            request.ProjectId, request.Items, cancellationToken);

        if (sectionError is not null)
            return BadRequest(new { message = sectionError });

        var items = BuildItems(request.Items);

        var boq = new ProjectBoq
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            BoqNumber = request.BoqNumber,
            Name = request.Name,
            RevisionNumber = request.RevisionNumber,
            Status = ProjectBoqStatus.Draft,
            IsCurrentRevision = true,
            CurrencyCode = request.CurrencyCode,
            TotalAmount = items.Sum(x => x.TotalAmount),
            Description = request.Description,
            Notes = request.Notes,
            Items = items
        };

        db.ProjectBoqs.Add(boq);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            boq.Id,
            boq.BoqNumber,
            boq.RevisionNumber,
            boq.Status,
            boq.TotalAmount
        });
    }

    /// <summary>
    /// İcmal kalemlerini yeniden yazar (tam değiştirme).
    ///
    /// Onaylı icmal KİLİTLİDİR: değişiklik revizyonla yapılır. Onaydan
    /// sonra kalem düzenlemeye izin vermek, geçmiş hakedişlerin
    /// dayandığı sözleşme miktarını sessizce oynatırdı.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.HakedisEdit)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateProjectBoqRequest request,
        CancellationToken cancellationToken)
    {
        // Kalemler yüklenmiyor: ReplaceItemsAsync onları doğrudan
        // veritabanından siliyor, izlenen kopya bayatlardı.
        var boq = await db.ProjectBoqs
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (boq is null)
            return NotFound(new { message = "Sözleşme icmali bulunamadı." });

        if (boq.Status != ProjectBoqStatus.Draft)
        {
            return Conflict(new
            {
                message = "Onaylanmış icmal düzenlenemez. " +
                          "Değişiklik için revizyon oluşturun."
            });
        }

        if (request.Items is null || request.Items.Count == 0)
            return BadRequest(new { message = "İcmal en az bir kalem içermelidir." });

        var sectionError = await ValidateSectionsAsync(
            boq.ProjectId, request.Items, cancellationToken);

        if (sectionError is not null)
            return BadRequest(new { message = sectionError });

        boq.Name = request.Name.Trim();
        boq.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant();
        boq.Description = request.Description?.Trim();
        boq.Notes = request.Notes?.Trim();
        boq.UpdatedAtUtc = DateTime.UtcNow;
        boq.UpdatedByUserId = currentUser.UserId;

        var items = BuildItems(request.Items);
        boq.TotalAmount = items.Sum(x => x.TotalAmount);

        await ReplaceItemsAsync(boq, items, cancellationToken);

        return Ok(new
        {
            boq.Id,
            boq.TotalAmount,
            ItemCount = items.Count,
            message = "Sözleşme icmali güncellendi."
        });
    }

    /// <summary>
    /// İcmalin kalemlerini tam olarak değiştirir.
    ///
    /// Silme ve ekleme AYRI ADIMDA: kalemlerde
    /// (ProjectBoqId, LineNumber) tekil indeksi var, ikisi tek
    /// SaveChanges'e girdiğinde yeni satır eskisiyle aynı sıra numarası
    /// üzerinde çakışıyor. İki adım tek işlem (transaction) içinde
    /// yürüyor: yarısı yazılıp yarısı yazılmamış icmal kalmaz.
    /// </summary>
    private async Task ReplaceItemsAsync(
        ProjectBoq boq,
        IReadOnlyList<ProjectBoqItem> items,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        await db.ProjectBoqItems
            .Where(x => x.ProjectBoqId == boq.Id)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var item in items)
        {
            item.ProjectBoqId = boq.Id;
            db.ProjectBoqItems.Add(item);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Kalem listesini kurar. Birim fiyat bileşenleri verilmişse
    /// <c>UnitPrice</c> üçünün toplamı olarak TÜRETİLİR; verilmemişse
    /// tek fiyat malzemeye yazılır. İki durumda da toplam aynı çıkar.
    /// </summary>
    private static List<ProjectBoqItem> BuildItems(
        IReadOnlyList<ProjectBoqItemRequest> requests)
    {
        return requests.Select((line, index) =>
        {
            var hasComponents =
                line.MaterialUnitPrice.HasValue ||
                line.LaborUnitPrice.HasValue ||
                line.OverheadUnitPrice.HasValue;

            var material = hasComponents
                ? line.MaterialUnitPrice ?? 0m
                : line.UnitPrice;
            var labor = line.LaborUnitPrice ?? 0m;
            var overhead = line.OverheadUnitPrice ?? 0m;

            var unitPrice = material + labor + overhead;

            return new ProjectBoqItem
            {
                EngineeringPositionId = line.EngineeringPositionId,
                ProjectHakedisSectionId = line.ProjectHakedisSectionId,
                LineNumber = index + 1,
                PositionCode = line.PositionCode.Trim(),
                Description = line.Description.Trim(),
                Unit = line.Unit.Trim(),
                ContractQuantity = line.ContractQuantity,
                MaterialUnitPrice = material,
                LaborUnitPrice = labor,
                OverheadUnitPrice = overhead,
                UnitPrice = unitPrice,
                TotalAmount = decimal.Round(line.ContractQuantity * unitPrice, 2),
                ItemType = line.ItemType,
                Category = line.Category?.Trim(),
                Notes = line.Notes?.Trim()
            };
        }).ToList();
    }

    /// <summary>
    /// Kalemlere verilen kısımların gerçekten bu projeye ait olduğunu
    /// doğrular. Başka projenin kısmına bağlanan kalem, icmalde
    /// görünmeyen ama toplamı bozan bir satır olurdu.
    /// </summary>
    private async Task<string?> ValidateSectionsAsync(
        Guid projectId,
        IReadOnlyList<ProjectBoqItemRequest> requests,
        CancellationToken cancellationToken)
    {
        var sectionIds = requests
            .Select(x => x.ProjectHakedisSectionId)
            .OfType<Guid>()
            .Distinct()
            .ToList();

        if (sectionIds.Count == 0)
            return null;

        var validCount = await db.ProjectHakedisSections
            .CountAsync(x => x.ProjectId == projectId && sectionIds.Contains(x.Id),
                cancellationToken);

        return validCount == sectionIds.Count
            ? null
            : "Kalemlerden biri bu projeye ait olmayan bir kısma bağlanmış.";
    }

    /// <summary>Doldurulacak boş Excel şablonu.</summary>
    [HttpGet("/api/project-boqs/icmal-sablonu")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public IActionResult DownloadTemplate() =>
        File(
            ContractSummaryExcelParser.BuildTemplate(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "sozlesme-icmali-sablonu.xlsx");

    /// <summary>
    /// Excel dosyasını okur ve ÖNİZLEME döner — hiçbir şey yazmaz.
    ///
    /// Bozuk satırlar tüm dosyayı reddettirmez; hata listesiyle birlikte
    /// döner ve yazma kararı kullanıcıya kalır. Yarım bir icmali sessizce
    /// kaydetmek, toplamı tutmayan bir sözleşme tabanı üretirdi.
    /// </summary>
    [HttpPost("{id:guid}/icmal-aktar/onizleme")]
    [RequirePermission(PermissionCatalog.Keys.HakedisEdit)]
    [RequestSizeLimit(20L * 1024 * 1024)]
    public async Task<IActionResult> ImportPreview(
        Guid id,
        IFormFile file,
        [FromServices] Services.Engineering.IPositionMatchService matcher,
        CancellationToken cancellationToken)
    {
        var boq = await db.ProjectBoqs
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (boq is null)
            return NotFound(new { message = "Sözleşme icmali bulunamadı." });

        if (boq.Status != ProjectBoqStatus.Draft)
        {
            return Conflict(new
            {
                message = "Onaylanmış icmale aktarım yapılamaz. " +
                          "Değişiklik için revizyon oluşturun."
            });
        }

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Excel dosyası seçilmedi." });

        ContractSummaryParseResult parsed;

        try
        {
            await using var stream = file.OpenReadStream();
            parsed = ContractSummaryExcelParser.Parse(stream);
        }
        catch (Exception exception)
        {
            // Bozuk/şifreli dosya: kullanıcıya teknik yığın değil,
            // ne yapması gerektiği söyleniyor.
            return BadRequest(new
            {
                message = "Dosya okunamadı. Şablonu indirip onun üzerine " +
                          $"doldurduğunuzdan emin olun. ({exception.GetType().Name})"
            });
        }

        return Ok(await BuildPreview(boq.ProjectId, parsed, matcher, cancellationToken));
    }

    /// <summary>
    /// Önizlemesi görülen dosyayı icmale yazar. Mevcut kalemlerin
    /// üzerine yazar (tam değiştirme) — kısmi birleştirme, hangi satırın
    /// nereden geldiğini takip edilemez hale getirirdi.
    ///
    /// Dosyadaki kısım başlıkları projede yoksa OLUŞTURULUR; varsa ada
    /// göre eşleştirilir.
    /// </summary>
    [HttpPost("{id:guid}/icmal-aktar")]
    [RequirePermission(PermissionCatalog.Keys.HakedisEdit)]
    [RequestSizeLimit(20L * 1024 * 1024)]
    public async Task<IActionResult> ImportCommit(
        Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        // Kalemler yüklenmiyor: ReplaceItemsAsync onları doğrudan
        // veritabanından siliyor, izlenen kopya bayatlardı.
        var boq = await db.ProjectBoqs
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (boq is null)
            return NotFound(new { message = "Sözleşme icmali bulunamadı." });

        if (boq.Status != ProjectBoqStatus.Draft)
        {
            return Conflict(new
            {
                message = "Onaylanmış icmale aktarım yapılamaz. " +
                          "Değişiklik için revizyon oluşturun."
            });
        }

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Excel dosyası seçilmedi." });

        ContractSummaryParseResult parsed;

        try
        {
            await using var stream = file.OpenReadStream();
            parsed = ContractSummaryExcelParser.Parse(stream);
        }
        catch (Exception exception)
        {
            return BadRequest(new
            {
                message = "Dosya okunamadı. Şablonu indirip onun üzerine " +
                          $"doldurduğunuzdan emin olun. ({exception.GetType().Name})"
            });
        }

        if (parsed.ItemCount == 0)
            return BadRequest(new { message = "Dosyada okunabilir poz satırı yok." });

        var sectionMap = await EnsureSectionsAsync(
            boq.ProjectId, parsed, cancellationToken);

        var lineNumber = 1;

        var items = parsed.Lines
            .Where(x => !x.IsSectionHeader)
            .Select(line =>
            {
                Guid? sectionId = null;

                if (line.SectionName is not null &&
                    sectionMap.TryGetValue(line.SectionName, out var resolved))
                {
                    sectionId = resolved;
                }

                return new ProjectBoqItem
                {
                    ProjectHakedisSectionId = sectionId,
                    LineNumber = lineNumber++,
                    PositionCode = line.PositionCode,
                    Description = line.Description,
                    Unit = line.Unit,
                    ContractQuantity = line.ContractQuantity,
                    MaterialUnitPrice = line.MaterialUnitPrice,
                    LaborUnitPrice = line.LaborUnitPrice,
                    OverheadUnitPrice = line.OverheadUnitPrice,
                    UnitPrice = line.UnitPrice,
                    TotalAmount = line.TotalAmount,
                    ItemType = ProjectBoqItemType.Mixed
                };
            })
            .ToList();

        boq.TotalAmount = items.Sum(x => x.TotalAmount);
        boq.UpdatedAtUtc = DateTime.UtcNow;
        boq.UpdatedByUserId = currentUser.UserId;

        await ReplaceItemsAsync(boq, items, cancellationToken);

        return Ok(new
        {
            message = $"{parsed.ItemCount} poz aktarıldı.",
            SectionCount = parsed.SectionCount,
            ItemCount = parsed.ItemCount,
            SkippedRowCount = parsed.Errors.Count,
            boq.TotalAmount
        });
    }

    /// <summary>
    /// Dosyadaki kısımları projeye kurar; zaten varsa ada göre eşler.
    /// Karşılaştırma büyük/küçük harften bağımsız: "Panolar" ile
    /// "PANOLAR" iki ayrı kısım açmamalı.
    ///
    /// KAYDETMEZ: yeni kısımlar yalnızca izlemeye eklenir, hepsi
    /// aktarımın tek SaveChanges'i içinde yazılır. Aksi halde kısımlar
    /// yazılıp kalemler yazılamadığında proje, karşılığı olmayan boş
    /// kısımlarla kalırdı. Id'ler BaseEntity'de istemci tarafında
    /// üretildiği için eşleme kaydetmeden de kurulabiliyor.
    /// </summary>
    private async Task<Dictionary<string, Guid>> EnsureSectionsAsync(
        Guid projectId,
        ContractSummaryParseResult parsed,
        CancellationToken cancellationToken)
    {
        var existing = await db.ProjectHakedisSections
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => new { x.Id, x.Name, x.Order })
            .ToListAsync(cancellationToken);

        var map = existing.ToDictionary(
            x => x.Name, x => x.Id, StringComparer.OrdinalIgnoreCase);

        var nextOrder = existing.Count == 0 ? 1 : existing.Max(x => x.Order) + 1;

        foreach (var header in parsed.Lines.Where(x => x.IsSectionHeader))
        {
            var name = header.SectionName!;

            if (map.ContainsKey(name))
                continue;

            var section = new ProjectHakedisSection
            {
                ProjectId = projectId,
                Order = nextOrder++,
                Name = name,
                IsActive = true
            };

            db.ProjectHakedisSections.Add(section);
            map[name] = section.Id;
        }

        return map;
    }

    private async Task<object> BuildPreview(
        Guid projectId,
        ContractSummaryParseResult parsed,
        Services.Engineering.IPositionMatchService matcher,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new { x.CompanyId })
            .SingleOrDefaultAsync(cancellationToken);

        var existingSections = await db.ProjectHakedisSections
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => x.Name)
            .ToListAsync(cancellationToken);

        var known = new HashSet<string>(existingSections, StringComparer.OrdinalIgnoreCase);

        var sections = parsed.Lines
            .Where(x => x.IsSectionHeader)
            .Select(header => new
            {
                header.RowNumber,
                Name = header.SectionName!,
                IsNew = !known.Contains(header.SectionName!),
                ItemCount = parsed.Lines.Count(x =>
                    !x.IsSectionHeader && x.SectionName == header.SectionName),
                TotalAmount = parsed.Lines
                    .Where(x => !x.IsSectionHeader && x.SectionName == header.SectionName)
                    .Sum(x => x.TotalAmount)
            })
            .ToList();

        // Önizlemede gösterilen satır sayısı sınırlı; eşleştirme de
        // yalnızca gösterilenler için yapılır.
        var previewLines = parsed.Lines
            .Where(x => !x.IsSectionHeader)
            .Take(200)
            .ToList();

        var matches = new Dictionary<int, Services.Engineering.BulkMatchRow>();

        if (project is not null && previewLines.Count > 0)
        {
            var rows = previewLines
                .Select(x => (
                    x.RowNumber,
                    Query: string.IsNullOrWhiteSpace(x.PositionCode)
                        ? x.Description
                        : $"{x.PositionCode} {x.Description}"))
                .Where(x => !string.IsNullOrWhiteSpace(x.Query))
                .ToList();

            var suggestions = await matcher.SuggestBulkAsync(
                project.CompanyId, rows, cancellationToken: cancellationToken);

            foreach (var suggestion in suggestions)
                matches[suggestion.RowNumber] = suggestion;
        }

        return new
        {
            SectionCount = parsed.SectionCount,
            ItemCount = parsed.ItemCount,
            parsed.TotalAmount,
            Sections = sections,
            UnsectionedItemCount = parsed.Lines
                .Count(x => !x.IsSectionHeader && x.SectionName is null),
            Errors = parsed.Errors.Select(x => new { x.RowNumber, x.Message }).ToList(),
            Items = previewLines
                .Select(x => new
                {
                    x.RowNumber,
                    x.SectionName,
                    x.PositionCode,
                    x.Description,
                    x.Unit,
                    x.ContractQuantity,
                    x.MaterialUnitPrice,
                    x.LaborUnitPrice,
                    x.OverheadUnitPrice,
                    x.UnitPrice,
                    x.TotalAmount,
                    // Poz önerisi: kesinse otomatik seçilebilir, değilse
                    // kullanıcı aday listesinden seçer ya da özel poz açar.
                    Match = matches.TryGetValue(x.RowNumber, out var match)
                        ? new
                        {
                            match.IsCertain,
                            match.CertaintyReason,
                            Candidates = match.Suggestions.Select(s => new
                            {
                                s.PositionId,
                                Code = s.OfficialCode ?? s.Code,
                                s.Name,
                                s.Unit,
                                s.Institution,
                                s.Score,
                                s.UnitPrice,
                                s.MaterialPrice,
                                s.LaborPrice
                            })
                        }
                        : null
                })
                .ToList()
        };
    }

    [HttpPost("{id:guid}/approve")]
    [RequirePermission(PermissionCatalog.Keys.HakedisApprove)]
    public async Task<IActionResult> Approve(
        Guid id,
        CancellationToken cancellationToken)
    {
        var boq = await db.ProjectBoqs.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (boq is null)
            return NotFound(new { message = "Metraj (BOQ) kaydı bulunamadı." });

        if (boq.Status == ProjectBoqStatus.Approved)
        {
            return Conflict(new
            {
                message = "Metraj zaten onaylanmış."
            });
        }

        boq.Status = ProjectBoqStatus.Approved;
        boq.ApprovedAtUtc = DateTime.UtcNow;
        boq.ApprovedByUserId = currentUser.UserId;
        boq.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            boq.Id,
            boq.BoqNumber,
            boq.RevisionNumber,
            boq.Status,
            message = "Metraj onaylandı."
        });
    }

    /// <summary>
    /// Onaylı icmalden yeni revizyon (zeyilname) oluşturur.
    ///
    /// Kalemler yeni revizyona kopyalanır, eski kayıt
    /// <see cref="ProjectBoqStatus.Superseded"/> olur ve güncel revizyon
    /// bayrağı yeniye geçer. ESKİ KAYIT SİLİNMEZ: geçmiş hakedişler ve
    /// metrajlar ona dayanıyor; silinseydi kesinleşmiş belgelerin
    /// sözleşme miktarı kaynaksız kalırdı.
    /// </summary>
    [HttpPost("{id:guid}/revizyon")]
    [RequirePermission(PermissionCatalog.Keys.HakedisEdit)]
    public async Task<IActionResult> CreateRevision(
        Guid id,
        CreateBoqRevisionRequest request,
        CancellationToken cancellationToken)
    {
        var source = await db.ProjectBoqs
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (source is null)
            return NotFound(new { message = "Sözleşme icmali bulunamadı." });

        if (source.Status == ProjectBoqStatus.Draft)
        {
            return Conflict(new
            {
                message = "Taslak icmalde revizyona gerek yok; " +
                          "kalemleri doğrudan düzenleyin."
            });
        }

        if (source.Status == ProjectBoqStatus.Archived)
            return Conflict(new { message = "Arşivlenmiş icmalden revizyon üretilemez." });

        // Aynı icmalden ikinci kez revizyon açılması, hangi zincirin
        // güncel olduğunu belirsizleştirir.
        var alreadyRevised = await db.ProjectBoqs
            .AnyAsync(x => x.SupersededBoqId == id, cancellationToken);

        if (alreadyRevised)
        {
            return Conflict(new
            {
                message = "Bu icmalden zaten bir revizyon üretilmiş."
            });
        }

        var nextRevision = await db.ProjectBoqs
            .Where(x => x.ProjectId == source.ProjectId &&
                        x.BoqNumber == source.BoqNumber)
            .MaxAsync(x => (int?)x.RevisionNumber, cancellationToken) ?? source.RevisionNumber;

        var revision = new ProjectBoq
        {
            CompanyId = source.CompanyId,
            ProjectId = source.ProjectId,
            BoqNumber = source.BoqNumber,
            Name = source.Name,
            RevisionNumber = nextRevision + 1,
            Status = ProjectBoqStatus.Draft,
            IsCurrentRevision = true,
            IsContractBaseline = source.IsContractBaseline,
            CurrencyCode = source.CurrencyCode,
            Description = source.Description,
            Notes = source.Notes,
            SupersededBoqId = source.Id,
            AmendmentNumber = string.IsNullOrWhiteSpace(request.AmendmentNumber)
                ? null
                : request.AmendmentNumber.Trim(),
            AmendmentDate = request.AmendmentDate is DateTime date
                ? DateTime.SpecifyKind(date.Date, DateTimeKind.Utc)
                : null,
            RevisionReason = string.IsNullOrWhiteSpace(request.Reason)
                ? null
                : request.Reason.Trim(),
            Items = source.Items
                .OrderBy(x => x.LineNumber)
                .Select(x => new ProjectBoqItem
                {
                    EngineeringPositionId = x.EngineeringPositionId,
                    ProjectHakedisSectionId = x.ProjectHakedisSectionId,
                    InventoryItemId = x.InventoryItemId,
                    LineNumber = x.LineNumber,
                    PositionCode = x.PositionCode,
                    Description = x.Description,
                    Unit = x.Unit,
                    ContractQuantity = x.ContractQuantity,
                    MaterialUnitPrice = x.MaterialUnitPrice,
                    LaborUnitPrice = x.LaborUnitPrice,
                    OverheadUnitPrice = x.OverheadUnitPrice,
                    UnitPrice = x.UnitPrice,
                    TotalAmount = x.TotalAmount,
                    ItemType = x.ItemType,
                    Category = x.Category,
                    Notes = x.Notes
                })
                .ToList()
        };

        revision.TotalAmount = revision.Items.Sum(x => x.TotalAmount);

        // Eski kayıt donuyor: güncel revizyon ve sözleşme tabanı yeniye
        // geçiyor, ama kaydın kendisi ve kalemleri olduğu gibi kalıyor.
        source.Status = ProjectBoqStatus.Superseded;
        source.IsCurrentRevision = false;
        source.IsContractBaseline = false;
        source.UpdatedAtUtc = DateTime.UtcNow;
        source.UpdatedByUserId = currentUser.UserId;

        db.ProjectBoqs.Add(revision);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            revision.Id,
            revision.BoqNumber,
            revision.RevisionNumber,
            RevisionCode = $"R{revision.RevisionNumber}",
            revision.Status,
            revision.TotalAmount,
            ItemCount = revision.Items.Count,
            SupersededBoqId = source.Id,
            message = $"R{revision.RevisionNumber} revizyonu taslak olarak oluşturuldu."
        });
    }

    [HttpPost("{id:guid}/archive")]
    [RequirePermission(PermissionCatalog.Keys.HakedisEdit)]
    public async Task<IActionResult> Archive(
        Guid id,
        CancellationToken cancellationToken)
    {
        var boq = await db.ProjectBoqs.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (boq is null)
            return NotFound(new { message = "Metraj (BOQ) kaydı bulunamadı." });

        boq.Status = ProjectBoqStatus.Archived;
        boq.IsCurrentRevision = false;
        boq.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            boq.Id,
            boq.BoqNumber,
            boq.RevisionNumber,
            boq.Status,
            message = "Metraj arşivlendi."
        });
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.HakedisDelete)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var boq = await db.ProjectBoqs.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (boq is null)
            return NotFound(new { message = "Metraj (BOQ) kaydı bulunamadı." });

        if (boq.Status != ProjectBoqStatus.Draft)
        {
            return Conflict(new
            {
                message = "Yalnızca taslak durumundaki metrajlar silinebilir."
            });
        }

        boq.IsActive = false;
        boq.IsDeleted = true;
        boq.DeletedAtUtc = DateTime.UtcNow;
        boq.DeletedByUserId = currentUser.UserId;

        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}

public sealed record ProjectBoqItemRequest(
    Guid? EngineeringPositionId,
    string PositionCode,
    string Description,
    string Unit,
    decimal ContractQuantity,
    decimal UnitPrice,
    ProjectBoqItemType ItemType,
    string? Category,
    string? Notes,
    /// <summary>
    /// Kalemin kısmı (ProjectHakedisSection). Opsiyonel — kısımsız
    /// icmal de kurulabilir.
    /// </summary>
    Guid? ProjectHakedisSectionId = null,
    /// <summary>
    /// Birim fiyat bileşenleri. Üçü de verilmezse <c>UnitPrice</c> tek
    /// fiyat kabul edilir ve malzemeye yazılır; toplam değişmez.
    /// </summary>
    decimal? MaterialUnitPrice = null,
    decimal? LaborUnitPrice = null,
    decimal? OverheadUnitPrice = null);

public sealed record CreateProjectBoqRequest(
    Guid CompanyId,
    Guid ProjectId,
    string BoqNumber,
    string Name,
    int RevisionNumber,
    string CurrencyCode,
    string? Description,
    string? Notes,
    List<ProjectBoqItemRequest> Items);

public sealed record CreateBoqRevisionRequest(
    string? AmendmentNumber,
    DateTime? AmendmentDate,
    string? Reason);

public sealed record UpdateProjectBoqRequest(
    string Name,
    string CurrencyCode,
    string? Description,
    string? Notes,
    List<ProjectBoqItemRequest> Items);
