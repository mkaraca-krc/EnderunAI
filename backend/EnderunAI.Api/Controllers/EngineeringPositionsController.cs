using EnderunAI.Api.Contracts.Core;
using EnderunAI.Api.Contracts.Engineering;
using EnderunAI.Api.Services.Engineering;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/engineering-positions")]
public sealed class EngineeringPositionsController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Pozun GERÇEK alış maliyeti: reçetesindeki malzemelerin onaylı
    /// alış faturalarından son fiyat ve ağırlıklı ortalama, resmî
    /// birim fiyatla birlikte.
    ///
    /// Zincirin (poz → reçete → stok kartı → alış faturası) koptuğu
    /// yerde sayı üretilmez; eksik toplam maliyeti olduğundan düşük
    /// gösterirdi. Nerede koptuğu uyarı olarak döner.
    /// </summary>
    [HttpGet("{id:guid}/purchase-intelligence")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringView)]
    public async Task<IActionResult> GetPurchaseIntelligence(
        Guid id,
        [FromQuery] Guid companyId,
        [FromQuery] int? months,
        [FromQuery] int? year,
        [FromServices] Services.Purchasing.SupplierPriceIntelligenceService intelligence,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçilmedi." });

        var result = await intelligence.AnalyzeAsync(
            companyId, id, months, year, cancellationToken);

        return result is null
            ? NotFound(new { message = "Poz bulunamadı." })
            : Ok(result);
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.EngineeringView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] int? source,
        [FromQuery] int? discipline,
        [FromQuery] int? status,
        [FromQuery] string? search,
        [FromQuery] int? take,
        [FromQuery] int? page,
        CancellationToken cancellationToken)
    {
        var query = db.EngineeringPositions.AsNoTracking();

        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (source.HasValue) query = query.Where(x => (int)x.Source == source.Value);
        if (discipline.HasValue) query = query.Where(x => (int)x.Discipline == discipline.Value);
        if (status.HasValue) query = query.Where(x => (int)x.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                x.Code.ToLower().Contains(term) ||
                x.Name.ToLower().Contains(term) ||
                (x.OfficialCode != null && x.OfficialCode.ToLower().Contains(term)) ||
                (x.SearchKeywords != null && x.SearchKeywords.ToLower().Contains(term)));
        }

        // Kütüphane 20 binin üzerinde poz taşıyor; tamamını döndürmek
        // istemciyi kilitler. Varsayılan tavan uygulanır, arama
        // yapılmadan tüm kütüphane çekilemez.
        var limit = take is > 0 and <= 500 ? take.Value : 100;

        // TOPLAM, TAVANDAN ÖNCE SAYILIR. Yalnız diziyi döndürdüğümüz
        // sürece arayüz kırpıldığını bilemiyordu ve gelen kaydı toplam
        // sanıyordu — poz ekranı 23.531 kayıtlık kütüphane için
        // "Toplam Poz: 100" gösteriyordu. Süzgeçler uygulanmış sorgu
        // üzerinden sayıyoruz ki arama sonucu da doğru raporlansın.
        var total = await query.CountAsync(cancellationToken);

        // SAYFA SUNUCUDA ATLANIR. 23.531 poz için istemciye hepsini
        // yollayıp orada dilimlemek ekranı kilitler; kullanıcı da
        // 101. poza yalnız aramayla ulaşabiliyordu.
        var currentPage = page is > 0 ? page.Value : 1;

        var items = await query
            .OrderBy(x => x.Code)
            .Skip((currentPage - 1) * limit)
            .Take(limit)
            .Select(x => new
            {
                x.Id, x.CompanyId, CompanyName = x.Company.Name,
                x.Code, x.Name, x.Unit, x.Source, x.Discipline, x.Status,
                x.OfficialInstitution, x.OfficialCode, x.Category,
                x.RevisionNumber,
                x.DefaultLaborHours, x.DefaultHelperHours, x.DefaultMachineHours,
                TotalLaborHours = x.DefaultLaborHours + x.DefaultHelperHours,
                x.CreatedAtUtc, x.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(PagedResult<object>.FromPage(items, total, limit, currentPage));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.EngineeringPositions.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.CompanyId, CompanyName = x.Company.Name,
                x.Code, x.Name, x.Unit, x.Source, x.Discipline, x.Status,
                x.OfficialInstitution, x.OfficialCode, x.Category,
                x.Description, x.TechnicalSpecification, x.SearchKeywords,
                x.RevisionNumber, x.DefaultLaborHours, x.DefaultHelperHours,
                x.DefaultMachineHours, x.ApprovedAtUtc, x.ApprovedByUserId,
                x.CreatedAtUtc, x.UpdatedAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        return item is null
            ? NotFound(new { message = "Poz bulunamadı." })
            : Ok(item);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    public async Task<IActionResult> Create(
        CreateEngineeringPositionRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(EngineeringPositionSource), request.Source))
            return BadRequest(new { message = "Geçersiz poz kaynağı." });

        if (!Enum.IsDefined(typeof(EngineeringPositionDiscipline), request.Discipline))
            return BadRequest(new { message = "Geçersiz disiplin." });

        if (!await db.Companies.AnyAsync(
                x => x.Id == request.CompanyId && x.IsActive,
                cancellationToken))
            return BadRequest(new { message = "Geçerli şirket seçilmelidir." });

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Unit))
            return BadRequest(new { message = "Poz adı ve birim zorunludur." });

        var source = (EngineeringPositionSource)request.Source;
        var discipline = (EngineeringPositionDiscipline)request.Discipline;

        var code = source == EngineeringPositionSource.Enderun
            ? await GenerateEnderunCode(request.CompanyId, discipline, cancellationToken)
            : (request.Code ?? request.OfficialCode ?? string.Empty).Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(code))
            return BadRequest(new { message = "Resmi pozlarda poz kodu zorunludur." });

        if (await db.EngineeringPositions.AnyAsync(
                x => x.CompanyId == request.CompanyId && x.Code == code,
                cancellationToken))
            return Conflict(new { message = "Bu poz kodu zaten kullanılıyor." });

        var position = new EngineeringPosition
        {
            CompanyId = request.CompanyId,
            Code = code,
            Name = request.Name.Trim(),
            Unit = request.Unit.Trim(),
            Source = source,
            Discipline = discipline,
            Status = EngineeringPositionStatus.Draft,
            OfficialInstitution = request.OfficialInstitution?.Trim(),
            OfficialCode = request.OfficialCode?.Trim(),
            Category = request.Category?.Trim(),
            Description = request.Description?.Trim(),
            TechnicalSpecification = request.TechnicalSpecification?.Trim(),
            SearchKeywords = request.SearchKeywords?.Trim(),
            DefaultLaborHours = request.DefaultLaborHours,
            DefaultHelperHours = request.DefaultHelperHours,
            DefaultMachineHours = request.DefaultMachineHours
        };

        db.EngineeringPositions.Add(position);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Mühendislik pozu oluşturuldu.",
            position.Id, position.Code, position.Name,
            position.RevisionNumber, position.Status
        });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateEngineeringPositionRequest request,
        CancellationToken cancellationToken)
    {
        var position = await db.EngineeringPositions
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (position is null)
            return NotFound(new { message = "Poz bulunamadı." });

        position.Name = request.Name.Trim();
        position.Unit = request.Unit.Trim();
        position.Discipline = (EngineeringPositionDiscipline)request.Discipline;
        position.Status = (EngineeringPositionStatus)request.Status;
        position.OfficialInstitution = request.OfficialInstitution?.Trim();
        position.OfficialCode = request.OfficialCode?.Trim();
        position.Category = request.Category?.Trim();
        position.Description = request.Description?.Trim();
        position.TechnicalSpecification = request.TechnicalSpecification?.Trim();
        position.SearchKeywords = request.SearchKeywords?.Trim();
        position.DefaultLaborHours = request.DefaultLaborHours;
        position.DefaultHelperHours = request.DefaultHelperHours;
        position.DefaultMachineHours = request.DefaultMachineHours;
        position.RevisionNumber += 1;
        position.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Poz güncellendi ve revizyon artırıldı.",
            position.Id, position.Code, position.RevisionNumber
        });
    }

    /// <summary>
    /// Kütüphanede karşılığı olmayan bir kalemi tek adımda şirkete özel
    /// poz olarak açar.
    ///
    /// Genel oluşturma ucundan farkı: kod otomatik üretilir, poz
    /// doğrudan AKTİF açılır ve varsa fiyat aynı istekte "Şirket"
    /// kurumuyla yazılır. Amaç keşif hazırlarken akışı kesmemek —
    /// kullanıcı ayrı bir ekrana gidip taslak poz açıp sonra
    /// onaylamak zorunda kalmasın.
    /// </summary>
    // YETKİ KASITLI OLARAK DAR: özel poz şirketin mühendislik
    // kütüphanesine KALICI satır yazar. Talep açabilen herkese
    // (Şantiye Şefi'nde purchasing-requests.create var) açılsaydı 23
    // binlik kütüphane mükerrer ve gelişigüzel kalemlerle dolardı.
    // Talep tarafı kaleme poz bulamazsa serbest metinle açmaya devam
    // eder; pozu teknik taraf tanımlar.
    [HttpPost("custom")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    public async Task<IActionResult> CreateCustom(
        CreateCustomPositionRequest request,
        [FromServices] IPositionPriceService prices,
        CancellationToken cancellationToken)
    {
        if (!await db.Companies.AnyAsync(x => x.Id == request.CompanyId, cancellationToken))
            return BadRequest(new { message = "Geçerli şirket seçilmelidir." });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Poz tanımı zorunludur." });

        if (!Enum.IsDefined(typeof(EngineeringPositionDiscipline), request.Discipline))
            return BadRequest(new { message = "Geçersiz disiplin." });

        var discipline = (EngineeringPositionDiscipline)request.Discipline;
        var unit = string.IsNullOrWhiteSpace(request.Unit) ? "AD" : request.Unit.Trim();

        // Kod elle verilebilir; verilmezse şirket serisinden üretilir.
        var code = string.IsNullOrWhiteSpace(request.Code)
            ? await GenerateEnderunCode(request.CompanyId, discipline, cancellationToken)
            : request.Code.Trim().ToUpperInvariant();

        if (await db.EngineeringPositions.AnyAsync(
                x => x.CompanyId == request.CompanyId && x.Code == code,
                cancellationToken))
        {
            return Conflict(new
            {
                message = $"'{code}' kodu bu şirkette zaten kullanılıyor."
            });
        }

        var name = request.Name.Trim();

        var position = new EngineeringPosition
        {
            CompanyId = request.CompanyId,
            Code = code,
            Name = name.Length > 500 ? name[..500] : name,
            Unit = unit.Length > 30 ? unit[..30] : unit,
            Source = EngineeringPositionSource.Enderun,
            Discipline = discipline,
            // Özel poz hemen kullanılabilir olmalı; taslak bırakmak
            // kullanıcıyı ikinci bir onay adımına zorlardı.
            Status = EngineeringPositionStatus.Active,
            OfficialInstitution = "Şirket",
            OfficialCode = code,
            Category = request.Category?.Trim(),
            Description = request.Notes?.Trim(),
            SearchKeywords = $"{code} {name} {request.Category}".Trim()
        };

        db.EngineeringPositions.Add(position);
        await db.SaveChangesAsync(cancellationToken);

        object? priceRow = null;

        if (request.UnitPrice is > 0)
        {
            var year = request.Year is >= 2000 and <= 2100
                ? request.Year.Value
                : DateTime.UtcNow.Year;

            priceRow = await prices.UpsertAsync(
                position.Id,
                new UpsertPositionPriceInput(
                    year,
                    PositionPriceInstitution.Company,
                    request.UnitPrice.Value,
                    "TRY",
                    null,
                    "Keşif sırasında açılan özel poz",
                    PositionPriceComponent.Total),
                cancellationToken);
        }

        return Ok(new
        {
            message = "Özel poz şirket kütüphanesine eklendi.",
            position.Id,
            position.Code,
            position.Name,
            position.Unit,
            Institution = position.OfficialInstitution,
            Price = priceRow
        });
    }

    /// <summary>
    /// Serbest metin iş tanımından poz önerir.
    ///
    /// Adayları kütüphane üretir; dil modeli yalnızca sıralar ve
    /// gerekçelendirir. Model listede olmayan bir poz döndürürse
    /// doğrulamada elenir — uydurma poz gelemez.
    /// </summary>
    [HttpGet("suggest")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringView)]
    public async Task<IActionResult> Suggest(
        [FromQuery] Guid companyId,
        [FromQuery] string query,
        [FromQuery] int? year,
        [FromQuery] int? limit,
        [FromQuery] bool useAi,
        [FromServices] IPositionMatchService matcher,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçilmelidir." });

        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(new { message = "Aranacak iş tanımını yazın." });

        var take = limit is > 0 and <= 25 ? limit.Value : PositionMatcher.DefaultLimit;

        return Ok(await matcher.SuggestAsync(
            companyId, query, year, take, useAi, cancellationToken));
    }

    /// <summary>
    /// Pozun kurum bazında referans fiyatları — ÇŞB ve TEDAŞ yan yana.
    /// İhalede hangi kitaba göre teklif verildiği önemli olduğu için
    /// ikisi de döner; poz yalnızca birinde varsa diğeri "yok" der.
    /// </summary>
    [HttpGet("{id:guid}/reference-prices")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringView)]
    public async Task<IActionResult> GetReferencePrices(
        Guid id,
        [FromQuery] int? year,
        [FromServices] IPositionPriceService prices,
        CancellationToken cancellationToken)
    {
        var institutions = new[]
        {
            PositionPriceInstitution.Csb,
            PositionPriceInstitution.Tedas,
            PositionPriceInstitution.Company
        };

        var results = new List<object>();

        foreach (var institution in institutions)
        {
            var resolution = await prices.ResolveAsync(
                id, year, institution, cancellationToken);

            results.Add(new
            {
                institution = (int)institution,
                institutionName = PositionPriceService.InstitutionNameOf(institution),
                resolution.Found,
                resolution.UnitPrice,
                resolution.MaterialPrice,
                resolution.LaborPrice,
                resolution.Year,
                resolution.Explanation
            });
        }

        return Ok(results);
    }

    /// <summary>Pozun yıl/kurum bazlı birim fiyat geçmişi.</summary>
    [HttpGet("{id:guid}/prices")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringView)]
    public async Task<IActionResult> GetPrices(
        Guid id,
        [FromServices] IPositionPriceService prices,
        CancellationToken cancellationToken)
        => Ok(await prices.GetHistoryAsync(id, cancellationToken));

    /// <summary>
    /// Belirli bir yıl/kurum için uygulanacak fiyat. Bulunamazsa daha
    /// eski bir yıla düşmez; gerekçesiyle "yok" döner.
    /// </summary>
    [HttpGet("{id:guid}/prices/resolve")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringView)]
    public async Task<IActionResult> ResolvePrice(
        Guid id,
        [FromQuery] int? year,
        [FromQuery] int? institution,
        [FromServices] IPositionPriceService prices,
        CancellationToken cancellationToken)
    {
        PositionPriceInstitution? source = null;

        if (institution.HasValue)
        {
            if (!Enum.IsDefined(typeof(PositionPriceInstitution), institution.Value))
                return BadRequest(new { message = "Geçersiz kurum." });

            source = (PositionPriceInstitution)institution.Value;
        }

        return Ok(await prices.ResolveAsync(id, year, source, cancellationToken));
    }

    /// <summary>
    /// Fiyat ekler veya aynı yıl/kurumdaki fiyatı günceller. Geçmiş yıl
    /// satırlarına dokunulmaz.
    /// </summary>
    [HttpPut("{id:guid}/prices")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    public async Task<IActionResult> UpsertPrice(
        Guid id,
        [FromBody] UpsertPositionPriceInput input,
        [FromServices] IPositionPriceService prices,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(PositionPriceInstitution), input.Institution))
            return BadRequest(new { message = "Geçersiz kurum." });

        try
        {
            return Ok(await prices.UpsertAsync(id, input, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("prices/{priceId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    public async Task<IActionResult> DeletePrice(
        Guid priceId,
        [FromServices] IPositionPriceService prices,
        CancellationToken cancellationToken)
        => await prices.DeleteAsync(priceId, cancellationToken)
            ? Ok(new { message = "Fiyat kaydı silindi." })
            : NotFound(new { message = "Fiyat kaydı bulunamadı." });

    [HttpPatch("{id:guid}/status")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    public async Task<IActionResult> ChangeStatus(
        Guid id,
        ChangeEngineeringPositionStatusRequest request,
        CancellationToken cancellationToken)
    {
        var position = await db.EngineeringPositions
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (position is null)
            return NotFound(new { message = "Poz bulunamadı." });

        if (!Enum.IsDefined(typeof(EngineeringPositionStatus), request.Status))
            return BadRequest(new { message = "Geçersiz poz durumu." });

        position.Status = (EngineeringPositionStatus)request.Status;
        position.IsActive = position.Status == EngineeringPositionStatus.Active;
        position.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Poz durumu güncellendi.", position.Id, position.Status });
    }

    private async Task<string> GenerateEnderunCode(
        Guid companyId,
        EngineeringPositionDiscipline discipline,
        CancellationToken cancellationToken)
    {
        var prefix = discipline switch
        {
            EngineeringPositionDiscipline.Electrical => "END-ELK",
            EngineeringPositionDiscipline.MediumVoltage => "END-OG",
            EngineeringPositionDiscipline.LowCurrent => "END-ZAY",
            EngineeringPositionDiscipline.DataCenter => "END-DC",
            EngineeringPositionDiscipline.Fiber => "END-FBR",
            EngineeringPositionDiscipline.Mechanical => "END-MEK",
            EngineeringPositionDiscipline.Civil => "END-INS",
            _ => "END-GEN"
        };

        var codes = await db.EngineeringPositions.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Code.StartsWith(prefix + "-"))
            .Select(x => x.Code)
            .ToListAsync(cancellationToken);

        var last = codes.Select(code =>
        {
            var suffix = code[(prefix.Length + 1)..];
            return int.TryParse(suffix, out var n) ? n : 0;
        }).DefaultIfEmpty(0).Max();

        return $"{prefix}-{last + 1:000000}";
    }
}

/// <summary>
/// Keşif akışından açılan şirkete özel poz. Kod boş bırakılırsa
/// şirket serisinden üretilir.
/// </summary>
public sealed record CreateCustomPositionRequest(
    Guid CompanyId,
    string Name,
    string? Unit,
    int Discipline,
    string? Code = null,
    string? Category = null,
    string? Notes = null,
    decimal? UnitPrice = null,
    int? Year = null);
