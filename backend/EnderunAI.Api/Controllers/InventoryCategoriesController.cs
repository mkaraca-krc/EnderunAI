using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// STOK KATEGORİLERİ VE ÖZELLİK ŞABLONLARI.
///
/// SİSTEM GENELİ: kategori şirkete bağlı değil (karar). Bu yüzden uçlar
/// `companyId` almıyor — kart şirkete ait, şablon ortak.
///
/// Liste ucu kart açma ekranının tek kaynağı: kategori seçilince izin
/// verilen birimler ve özellik/değer listeleri aynı yanıttan gelir.
/// Ayrı çağrılara bölmek, ekranın yarım şablonla kart açmasına yol
/// açardı.
/// </summary>
[ApiController]
[Authorize]
[Route("api/inventory/categories")]
public sealed class InventoryCategoriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.InventoryView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = db.InventoryCategories.AsNoTracking();

        if (!includeInactive) query = query.Where(x => x.IsActive);

        var categories = await query
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                Kind = (int)x.Kind,
                AccountingKind = (int)x.AccountingKind,
                x.IsActive,
                x.SortOrder,
                Units = x.AllowedUnits
                    .Where(unit => unit.IsActive)
                    .OrderBy(unit => unit.SortOrder)
                    .Select(unit => unit.Unit)
                    .ToList(),
                Attributes = x.Attributes
                    .Where(attribute => attribute.IsActive)
                    .OrderBy(attribute => attribute.SortOrder)
                    .Select(attribute => new
                    {
                        attribute.Id,
                        attribute.Code,
                        attribute.Name,
                        attribute.IsRequired,
                        attribute.SortOrder,
                        Options = attribute.Options
                            .Where(option => option.IsActive)
                            .OrderBy(option => option.SortOrder)
                            .Select(option => new
                            {
                                option.Id,
                                option.Value,
                                Display = option.Display ?? option.Value,
                                option.SortOrder
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }

    public sealed record CategoryRequest(
        string Code,
        string Name,
        int Kind,
        string[] Units,
        int SortOrder);

    /// <summary>
    /// Yeni kategori. Kod SİSTEM GENELİNDE tekil.
    ///
    /// Kategori yönetimi depo sorumlusu ve GM işi — bu yüzden
    /// `inventory.manage`; `inventory.create` (hareket açma) yetmez.
    /// </summary>
    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.InventoryManage)]
    public async Task<IActionResult> Create(
        CategoryRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Kod ve ad zorunludur." });

        if (request.Units is null || request.Units.Length == 0)
            return BadRequest(new
            {
                message = "En az bir birim tanımlanmalıdır. Birim kart "
                    + "açılırken bu listeden seçilir ve bir daha değişmez."
            });

        if (await db.InventoryCategories.AnyAsync(x => x.Code == code, cancellationToken))
            return Conflict(new { message = $"'{code}' kodlu kategori zaten var." });

        var category = new InventoryCategory
        {
            Code = code,
            Name = request.Name.Trim(),
            Kind = (InventoryCategoryKind)request.Kind,
            SortOrder = request.SortOrder
        };

        var order = 10;

        foreach (var unit in request.Units
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            category.AllowedUnits.Add(new InventoryCategoryUnit
            {
                Unit = unit!,
                SortOrder = order
            });

            order += 10;
        }

        db.InventoryCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Kategori oluşturuldu.", category.Id, category.Code });
    }

    public sealed record AccountingKindRequest(int AccountingKind);

    /// <summary>
    /// KATEGORİNİN MUHASEBE KARŞILIĞINI DEĞİŞTİR.
    ///
    /// Kategori oluşturma ucundan AYRI ve İZNİ DE AYRI: kart/kategori
    /// açmak depo sorumlusunun işi, hangi hesaba yazılacağına karar
    /// vermek mali müşavirin. Yanlış işaretlenmiş bir kategori, stoku
    /// yanlış hesaba taşır ve fark ancak mizanda görülür.
    ///
    /// Varsayılan SARF olduğu için "unutulursa" güvenli tarafta kalır;
    /// ticari mal işareti bilinçli bir eylem gerektirir.
    /// </summary>
    [HttpPut("{categoryId:guid}/accounting-kind")]
    [RequirePermission(PermissionCatalog.Keys.AccountingManage)]
    public async Task<IActionResult> SetAccountingKind(
        Guid categoryId, AccountingKindRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(InventoryAccountingKind), request.AccountingKind))
            return BadRequest(new { message = "Geçersiz muhasebe karşılığı." });

        var category = await db.InventoryCategories
            .SingleOrDefaultAsync(x => x.Id == categoryId, cancellationToken);

        if (category is null) return NotFound();

        var kind = (InventoryAccountingKind)request.AccountingKind;
        if (category.AccountingKind == kind)
            return Ok(new { message = "Değişiklik yok.", AccountingKind = (int)kind });

        category.AccountingKind = kind;
        await db.SaveChangesAsync(cancellationToken);

        var ad = kind == InventoryAccountingKind.TradeGood
            ? "ticari mal (153 / 621)"
            : "sarf malzeme (150 / 740)";

        return Ok(new
        {
            message = $"'{category.Name}' artık {ad} olarak muhasebeleşecek. "
                + "Bu tarihten SONRAKİ hareketler yeni hesaba yazılır; "
                + "geçmiş fişler değişmez.",
            AccountingKind = (int)kind
        });
    }

    public sealed record AttributeRequest(string Code, string Name, int SortOrder, bool IsRequired);

    [HttpPost("{categoryId:guid}/attributes")]
    [RequirePermission(PermissionCatalog.Keys.InventoryManage)]
    public async Task<IActionResult> AddAttribute(
        Guid categoryId, AttributeRequest request, CancellationToken cancellationToken)
    {
        var category = await db.InventoryCategories
            .SingleOrDefaultAsync(x => x.Id == categoryId, cancellationToken);

        if (category is null) return NotFound(new { message = "Kategori bulunamadı." });

        if (category.Kind == InventoryCategoryKind.Free)
            return BadRequest(new
            {
                message = "SERBEST kategoride özellik tanımlanmaz — ad elle yazılır."
            });

        var code = request.Code?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Kod ve ad zorunludur." });

        if (await db.InventoryAttributes.AnyAsync(
                x => x.InventoryCategoryId == categoryId && x.Code == code, cancellationToken))
            return Conflict(new { message = $"Bu kategoride '{code}' özelliği zaten var." });

        var attribute = new InventoryAttribute
        {
            InventoryCategoryId = categoryId,
            Code = code,
            Name = request.Name.Trim(),
            SortOrder = request.SortOrder,
            IsRequired = request.IsRequired
        };

        db.InventoryAttributes.Add(attribute);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Özellik eklendi.", attribute.Id });
    }

    public sealed record OptionRequest(string Value, string? Display, int SortOrder);

    [HttpPost("attributes/{attributeId:guid}/options")]
    [RequirePermission(PermissionCatalog.Keys.InventoryManage)]
    public async Task<IActionResult> AddOption(
        Guid attributeId, OptionRequest request, CancellationToken cancellationToken)
    {
        var exists = await db.InventoryAttributes
            .AnyAsync(x => x.Id == attributeId, cancellationToken);

        if (!exists) return NotFound(new { message = "Özellik bulunamadı." });

        var value = request.Value?.Trim();

        if (string.IsNullOrWhiteSpace(value))
            return BadRequest(new { message = "Değer zorunludur." });

        if (await db.InventoryAttributeOptions.AnyAsync(
                x => x.InventoryAttributeId == attributeId && x.Value == value, cancellationToken))
            return Conflict(new { message = $"'{value}' değeri bu özellikte zaten var." });

        var option = new InventoryAttributeOption
        {
            InventoryAttributeId = attributeId,
            Value = value,
            Display = string.IsNullOrWhiteSpace(request.Display) ? null : request.Display.Trim(),
            SortOrder = request.SortOrder
        };

        db.InventoryAttributeOptions.Add(option);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Değer eklendi.", option.Id });
    }
}
