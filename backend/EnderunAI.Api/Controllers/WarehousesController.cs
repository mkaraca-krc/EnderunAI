using EnderunAI.Api.Contracts.Inventory;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/warehouses")]
public sealed class WarehousesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.InventoryView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = db.Warehouses.AsNoTracking();

        // Varsayılan yalnızca aktif depolar: hareket formlarındaki depo
        // listesi buradan besleniyor, kapatılmış depo seçilebilmemeli.
        if (!includeInactive)
            query = query.Where(x => x.IsActive);

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);

        var warehouses = await query
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.BranchId,
                BranchName = x.Branch.Name,
                x.ProjectId,
                ProjectCode = x.Project != null ? x.Project.Code : null,
                ProjectName = x.Project != null ? x.Project.Name : null,
                x.ProjectSiteId,
                SiteName = x.ProjectSite != null ? x.ProjectSite.Name : null,
                x.Code,
                x.Name,
                x.Type,
                x.Address,
                x.IsActive,
                StockLineCount = db.WarehouseStocks.Count(s => s.WarehouseId == x.Id),
                StockValue = db.WarehouseStocks
                    .Where(s => s.WarehouseId == x.Id)
                    .Sum(s => (decimal?)(s.Quantity * s.InventoryItem.AverageUnitCost)) ?? 0m
            })
            .ToListAsync(cancellationToken);

        return Ok(warehouses);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.InventoryManage)]
    public async Task<IActionResult> Create(
        CreateWarehouseRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(request.CompanyId, request.BranchId,
            request.ProjectId, request.ProjectSiteId, request.Code, request.Name,
            request.Type, null, cancellationToken);

        if (validation is not null)
            return validation;

        var entity = new Warehouse
        {
            CompanyId = request.CompanyId,
            BranchId = request.BranchId,
            ProjectId = request.ProjectId,
            ProjectSiteId = request.ProjectSiteId,
            Code = request.Code.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            Type = (WarehouseType)request.Type,
            Address = request.Address?.Trim()
        };

        db.Warehouses.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Depo oluşturuldu.", entity.Id, entity.Code, entity.Name });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.InventoryManage)]
    public async Task<IActionResult> Update(
        Guid id, UpdateWarehouseRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.Warehouses
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Depo bulunamadı." });

        var validation = await ValidateAsync(entity.CompanyId, request.BranchId,
            request.ProjectId, request.ProjectSiteId, entity.Code, request.Name,
            request.Type, id, cancellationToken);

        if (validation is not null)
            return validation;

        // Depoda stok varken kapatmak, o stoğun defterde görünmeden
        // kalmasına yol açar; önce boşaltılmalı.
        if (!request.IsActive && entity.IsActive)
        {
            var hasStock = await db.WarehouseStocks
                .AnyAsync(x => x.WarehouseId == id && x.Quantity != 0m, cancellationToken);

            if (hasStock)
            {
                return BadRequest(new
                {
                    message = "Depoda stok varken kapatılamaz. " +
                              "Önce transfer veya çıkış ile boşaltın."
                });
            }
        }

        entity.BranchId = request.BranchId;
        entity.ProjectId = request.ProjectId;
        entity.ProjectSiteId = request.ProjectSiteId;
        entity.Name = request.Name.Trim();
        entity.Type = (WarehouseType)request.Type;
        entity.Address = request.Address?.Trim();
        entity.IsActive = request.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Depo güncellendi." });
    }

    /// <summary>
    /// Ortak doğrulama. Kod yalnızca oluşturmada kontrol edilir; depo
    /// kodu hareket belgelerinde geçtiği için sonradan değiştirilmiyor.
    /// </summary>
    private async Task<IActionResult?> ValidateAsync(
        Guid companyId, Guid branchId, Guid? projectId, Guid? projectSiteId,
        string code, string name, int type, Guid? existingId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Depo kodu ve adı zorunludur." });

        if (!Enum.IsDefined(typeof(WarehouseType), type))
            return BadRequest(new { message = "Geçersiz depo tipi." });

        var companyExists = await db.Companies
            .AnyAsync(x => x.Id == companyId && x.IsActive, cancellationToken);
        if (!companyExists)
            return BadRequest(new { message = "Geçerli bir şirket seçilmelidir." });

        var branchValid = await db.Branches
            .AnyAsync(x => x.Id == branchId && x.CompanyId == companyId, cancellationToken);
        if (!branchValid)
            return BadRequest(new { message = "Şube bu şirkete ait değil." });

        if (projectId is Guid project)
        {
            var projectValid = await db.Projects
                .AnyAsync(x => x.Id == project && x.CompanyId == companyId, cancellationToken);
            if (!projectValid)
                return BadRequest(new { message = "Proje bu şirkete ait değil." });
        }

        if (projectSiteId is Guid site)
        {
            if (projectId is null)
                return BadRequest(new { message = "Şantiye seçildiyse proje de seçilmelidir." });

            var siteValid = await db.ProjectSites
                .AnyAsync(x => x.Id == site && x.ProjectId == projectId, cancellationToken);
            if (!siteValid)
                return BadRequest(new { message = "Şantiye seçilen projeye ait değil." });
        }

        if (existingId is null)
        {
            var normalized = code.Trim().ToUpperInvariant();

            // Karşılaştırma büyük/küçük harften bağımsız: yeni kodlar
            // büyük harfe çevriliyor ama sistemde daha önce küçük harfle
            // açılmış depolar var. Doğrudan eşitlik, "DPA" ile "dpa"yı
            // farklı sayıp aynı kodda iki depo açılmasına izin verirdi.
            var duplicate = await db.Warehouses
                .AnyAsync(x => x.CompanyId == companyId &&
                               x.Code.ToUpper() == normalized,
                    cancellationToken);

            if (duplicate)
                return Conflict(new { message = "Bu depo kodu şirket içinde zaten kullanılıyor." });
        }

        return null;
    }
}
