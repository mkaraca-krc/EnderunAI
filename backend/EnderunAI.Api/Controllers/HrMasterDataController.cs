using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models.HumanResources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/hr/payroll/salary-definitions")]
public sealed class HrSalaryDefinitionsController(
    HrDbContext hrDb,
    AppDbContext appDb) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? personnelId,
        [FromQuery] DateTime? effectiveDate,
        CancellationToken cancellationToken)
    {
        var query = hrDb.SalaryDefinitions.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (personnelId.HasValue)
            query = query.Where(x => x.PersonnelId == personnelId.Value);

        if (effectiveDate.HasValue)
        {
            var date = UtcDate(effectiveDate.Value);
            query = query.Where(x =>
                x.EffectiveStartDate <= date &&
                (!x.EffectiveEndDate.HasValue || x.EffectiveEndDate.Value >= date));
        }

        var items = await query
            .OrderByDescending(x => x.EffectiveStartDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        SaveSalaryDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(
            request.CompanyId,
            request.PersonnelId,
            request.EffectiveStartDate,
            request.EffectiveEndDate,
            null,
            cancellationToken);

        if (validation is not null)
            return validation;

        var item = new HrSalaryDefinition
        {
            CompanyId = request.CompanyId,
            PersonnelId = request.PersonnelId,
            EffectiveStartDate = UtcDate(request.EffectiveStartDate),
            EffectiveEndDate = UtcDate(request.EffectiveEndDate),
            GrossSalary = request.GrossSalary,
            NetSalary = request.NetSalary,
            DailyRate = request.DailyRate,
            HourlyRate = request.HourlyRate,
            OvertimeMultiplier = request.OvertimeMultiplier,
            SundayMultiplier = request.SundayMultiplier,
            PublicHolidayMultiplier = request.PublicHolidayMultiplier,
            CurrencyCode = NormalizeCurrency(request.CurrencyCode),
            Description = NormalizeText(request.Description)
        };

        hrDb.SalaryDefinitions.Add(item);
        await hrDb.SaveChangesAsync(cancellationToken);

        return Ok(item);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateSalaryDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var item = await hrDb.SalaryDefinitions
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Maaş kartı bulunamadı." });

        var validation = await ValidateAsync(
            item.CompanyId,
            item.PersonnelId,
            request.EffectiveStartDate,
            request.EffectiveEndDate,
            id,
            cancellationToken);

        if (validation is not null)
            return validation;

        item.EffectiveStartDate = UtcDate(request.EffectiveStartDate);
        item.EffectiveEndDate = UtcDate(request.EffectiveEndDate);
        item.GrossSalary = request.GrossSalary;
        item.NetSalary = request.NetSalary;
        item.DailyRate = request.DailyRate;
        item.HourlyRate = request.HourlyRate;
        item.OvertimeMultiplier = request.OvertimeMultiplier;
        item.SundayMultiplier = request.SundayMultiplier;
        item.PublicHolidayMultiplier = request.PublicHolidayMultiplier;
        item.CurrencyCode = NormalizeCurrency(request.CurrencyCode);
        item.Description = NormalizeText(request.Description);
        item.UpdatedAtUtc = DateTime.UtcNow;

        await hrDb.SaveChangesAsync(cancellationToken);
        return Ok(item);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await hrDb.SalaryDefinitions
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Maaş kartı bulunamadı." });

        item.IsActive = false;
        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;
        await hrDb.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<IActionResult?> ValidateAsync(
        Guid companyId,
        Guid personnelId,
        DateTime effectiveStartDate,
        DateTime? effectiveEndDate,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        if (effectiveEndDate.HasValue &&
            UtcDate(effectiveEndDate.Value) < UtcDate(effectiveStartDate))
        {
            return BadRequest(new
            {
                message = "Bitiş tarihi başlangıç tarihinden önce olamaz."
            });
        }

        var personnelExists = await appDb.Personnel.AnyAsync(
            x => x.Id == personnelId &&
                 x.CompanyId == companyId &&
                 x.IsActive,
            cancellationToken);

        if (!personnelExists)
        {
            return BadRequest(new
            {
                message = "Seçilen şirkete ait aktif personel bulunamadı."
            });
        }

        var start = UtcDate(effectiveStartDate);
        var end = UtcDate(effectiveEndDate);
        var overlaps = await hrDb.SalaryDefinitions.AnyAsync(
            x => x.PersonnelId == personnelId &&
                 (!excludedId.HasValue || x.Id != excludedId.Value) &&
                 (!x.EffectiveEndDate.HasValue || x.EffectiveEndDate.Value >= start) &&
                 (!end.HasValue || x.EffectiveStartDate <= end.Value),
            cancellationToken);

        return overlaps
            ? Conflict(new
            {
                message = "Bu personelin tarih aralığıyla çakışan başka bir maaş kartı var."
            })
            : null;
    }

    private static string NormalizeCurrency(string? value)
    {
        var currency = string.IsNullOrWhiteSpace(value)
            ? "TRY"
            : value.Trim().ToUpperInvariant();
        return currency.Length == 3 ? currency : "TRY";
    }

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime UtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private static DateTime? UtcDate(DateTime? value) =>
        value.HasValue ? UtcDate(value.Value) : null;
}

[ApiController]
[Authorize]
[Route("api/hr/departments")]
public sealed class HrDepartmentsController(
    HrDbContext hrDb,
    AppDbContext appDb) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        CancellationToken cancellationToken)
    {
        var query = hrDb.Departments.AsNoTracking();
        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        var items = await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var companyIds = items.Select(x => x.CompanyId).Distinct().ToArray();
        var managerIds = items
            .Where(x => x.ManagerPersonnelId.HasValue)
            .Select(x => x.ManagerPersonnelId!.Value)
            .Distinct()
            .ToArray();
        var ids = items.Select(x => x.Id).ToArray();

        var companyNames = await appDb.Companies.AsNoTracking()
            .Where(x => companyIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var managerNames = await appDb.Personnel.AsNoTracking()
            .Where(x => managerIds.Contains(x.Id))
            .ToDictionaryAsync(
                x => x.Id,
                x => x.FirstName + " " + x.LastName,
                cancellationToken);
        var positionCounts = await hrDb.Positions.AsNoTracking()
            .Where(x => ids.Contains(x.DepartmentId))
            .GroupBy(x => x.DepartmentId)
            .Select(group => new { DepartmentId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count, cancellationToken);
        var departmentNames = items.ToDictionary(x => x.Id, x => x.Name);

        return Ok(items.Select(x => new
        {
            x.Id,
            x.CompanyId,
            CompanyName = companyNames.GetValueOrDefault(x.CompanyId),
            x.Code,
            x.Name,
            x.ParentDepartmentId,
            ParentDepartmentName = x.ParentDepartmentId.HasValue
                ? departmentNames.GetValueOrDefault(x.ParentDepartmentId.Value)
                : null,
            x.ManagerPersonnelId,
            ManagerPersonnelName = x.ManagerPersonnelId.HasValue
                ? managerNames.GetValueOrDefault(x.ManagerPersonnelId.Value)
                : null,
            x.IsActive,
            PositionCount = positionCounts.GetValueOrDefault(x.Id),
            x.CreatedAtUtc
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await hrDb.Departments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return item is null
            ? NotFound(new { message = "Departman bulunamadı." })
            : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        SaveDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(
            request.CompanyId,
            request.Code,
            request.ParentDepartmentId,
            request.ManagerPersonnelId,
            null,
            cancellationToken);
        if (validation is not null)
            return validation;

        var item = new HrDepartment
        {
            CompanyId = request.CompanyId,
            Code = request.Code.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            ParentDepartmentId = request.ParentDepartmentId,
            ManagerPersonnelId = request.ManagerPersonnelId
        };

        hrDb.Departments.Add(item);
        await hrDb.SaveChangesAsync(cancellationToken);
        return Ok(item);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var item = await hrDb.Departments
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Departman bulunamadı." });

        if (request.ParentDepartmentId == id)
            return BadRequest(new { message = "Departman kendisine bağlanamaz." });

        var validation = await ValidateAsync(
            item.CompanyId,
            request.Code,
            request.ParentDepartmentId,
            request.ManagerPersonnelId,
            id,
            cancellationToken);
        if (validation is not null)
            return validation;

        item.Code = request.Code.Trim().ToUpperInvariant();
        item.Name = request.Name.Trim();
        item.ParentDepartmentId = request.ParentDepartmentId;
        item.ManagerPersonnelId = request.ManagerPersonnelId;
        item.IsActive = request.IsActive;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await hrDb.SaveChangesAsync(cancellationToken);
        return Ok(item);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await hrDb.Departments
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Departman bulunamadı." });

        var hasChildren = await hrDb.Departments.AnyAsync(
            x => x.ParentDepartmentId == id, cancellationToken);
        var hasPositions = await hrDb.Positions.AnyAsync(
            x => x.DepartmentId == id, cancellationToken);
        if (hasChildren || hasPositions)
        {
            return Conflict(new
            {
                message = "Alt birimi veya pozisyonu bulunan departman silinemez."
            });
        }

        item.IsActive = false;
        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;
        await hrDb.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<IActionResult?> ValidateAsync(
        Guid companyId,
        string code,
        Guid? parentDepartmentId,
        Guid? managerPersonnelId,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
            return BadRequest(new { message = "Departman kodu zorunludur." });

        var companyExists = await appDb.Companies.AnyAsync(
            x => x.Id == companyId && x.IsActive, cancellationToken);
        if (!companyExists)
            return BadRequest(new { message = "Aktif şirket bulunamadı." });

        var normalizedCode = code.Trim().ToUpperInvariant();
        if (await hrDb.Departments.AnyAsync(
                x => x.CompanyId == companyId &&
                     x.Code == normalizedCode &&
                     (!excludedId.HasValue || x.Id != excludedId.Value),
                cancellationToken))
        {
            return Conflict(new { message = "Departman kodu zaten kullanılıyor." });
        }

        if (parentDepartmentId.HasValue)
        {
            var parentExists = await hrDb.Departments.AnyAsync(
                x => x.Id == parentDepartmentId.Value &&
                     x.CompanyId == companyId,
                cancellationToken);
            if (!parentExists)
                return BadRequest(new { message = "Geçerli üst departman bulunamadı." });
        }

        if (managerPersonnelId.HasValue)
        {
            var managerExists = await appDb.Personnel.AnyAsync(
                x => x.Id == managerPersonnelId.Value &&
                     x.CompanyId == companyId &&
                     x.IsActive,
                cancellationToken);
            if (!managerExists)
                return BadRequest(new { message = "Geçerli yönetici personel bulunamadı." });
        }

        return null;
    }
}

[ApiController]
[Authorize]
[Route("api/hr/positions")]
public sealed class HrPositionsController(HrDbContext hrDb) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? departmentId,
        CancellationToken cancellationToken)
    {
        var query = hrDb.Positions.AsNoTracking()
            .Join(
                hrDb.Departments.AsNoTracking(),
                position => position.DepartmentId,
                department => department.Id,
                (position, department) => new { position, department });

        if (companyId.HasValue)
            query = query.Where(x => x.department.CompanyId == companyId.Value);
        if (departmentId.HasValue)
            query = query.Where(x => x.position.DepartmentId == departmentId.Value);

        var items = await query
            .OrderBy(x => x.position.Title)
            .Select(x => new
            {
                x.position.Id,
                x.department.CompanyId,
                x.position.DepartmentId,
                DepartmentName = x.department.Name,
                x.position.Code,
                x.position.Title,
                Name = x.position.Title,
                x.position.Description,
                x.position.Level,
                x.position.IsManagerial,
                x.position.IsActive,
                x.position.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await hrDb.Positions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return item is null
            ? NotFound(new { message = "Pozisyon bulunamadı." })
            : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        SavePositionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(
            request.DepartmentId, request.Code, null, cancellationToken);
        if (validation is not null)
            return validation;

        var companyId = await hrDb.Departments.AsNoTracking()
            .Where(x => x.Id == request.DepartmentId)
            .Select(x => x.CompanyId)
            .SingleAsync(cancellationToken);

        var item = new HrPosition
        {
            CompanyId = companyId,
            DepartmentId = request.DepartmentId,
            Code = request.Code.Trim().ToUpperInvariant(),
            Title = request.Title.Trim(),
            Description = NormalizeText(request.Description),
            Level = Math.Max(0, request.Level),
            IsManagerial = request.IsManagerial
        };

        hrDb.Positions.Add(item);
        await hrDb.SaveChangesAsync(cancellationToken);
        return Ok(item);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        UpdatePositionRequest request,
        CancellationToken cancellationToken)
    {
        var item = await hrDb.Positions
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Pozisyon bulunamadı." });

        var validation = await ValidateAsync(
            request.DepartmentId, request.Code, id, cancellationToken);
        if (validation is not null)
            return validation;

        var companyId = await hrDb.Departments.AsNoTracking()
            .Where(x => x.Id == request.DepartmentId)
            .Select(x => x.CompanyId)
            .SingleAsync(cancellationToken);

        item.CompanyId = companyId;
        item.DepartmentId = request.DepartmentId;
        item.Code = request.Code.Trim().ToUpperInvariant();
        item.Title = request.Title.Trim();
        item.Description = NormalizeText(request.Description);
        item.Level = Math.Max(0, request.Level);
        item.IsManagerial = request.IsManagerial;
        item.IsActive = request.IsActive;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await hrDb.SaveChangesAsync(cancellationToken);
        return Ok(item);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await hrDb.Positions
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Pozisyon bulunamadı." });

        item.IsActive = false;
        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;
        await hrDb.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<IActionResult?> ValidateAsync(
        Guid departmentId,
        string code,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
            return BadRequest(new { message = "Pozisyon kodu zorunludur." });

        var departmentExists = await hrDb.Departments.AnyAsync(
            x => x.Id == departmentId && x.IsActive, cancellationToken);
        if (!departmentExists)
            return BadRequest(new { message = "Aktif departman bulunamadı." });

        var normalizedCode = code.Trim().ToUpperInvariant();
        var codeExists = await hrDb.Positions.AnyAsync(
            x => x.DepartmentId == departmentId &&
                 x.Code == normalizedCode &&
                 (!excludedId.HasValue || x.Id != excludedId.Value),
            cancellationToken);
        return codeExists
            ? Conflict(new { message = "Pozisyon kodu bu departmanda zaten kullanılıyor." })
            : null;
    }

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record SaveSalaryDefinitionRequest(
    Guid CompanyId,
    Guid PersonnelId,
    DateTime EffectiveStartDate,
    DateTime? EffectiveEndDate,
    decimal GrossSalary,
    decimal NetSalary,
    decimal DailyRate,
    decimal HourlyRate,
    decimal OvertimeMultiplier,
    decimal SundayMultiplier,
    decimal PublicHolidayMultiplier,
    string CurrencyCode,
    string? Description);

public record UpdateSalaryDefinitionRequest(
    DateTime EffectiveStartDate,
    DateTime? EffectiveEndDate,
    decimal GrossSalary,
    decimal NetSalary,
    decimal DailyRate,
    decimal HourlyRate,
    decimal OvertimeMultiplier,
    decimal SundayMultiplier,
    decimal PublicHolidayMultiplier,
    string CurrencyCode,
    string? Description);

public record SaveDepartmentRequest(
    Guid CompanyId,
    string Code,
    string Name,
    Guid? ParentDepartmentId,
    Guid? ManagerPersonnelId);

public record UpdateDepartmentRequest(
    string Code,
    string Name,
    Guid? ParentDepartmentId,
    Guid? ManagerPersonnelId,
    bool IsActive);

public record SavePositionRequest(
    Guid DepartmentId,
    string Code,
    string Title,
    string? Description,
    bool IsManagerial,
    int Level = 0);

public record UpdatePositionRequest(
    Guid DepartmentId,
    string Code,
    string Title,
    string? Description,
    bool IsManagerial,
    bool IsActive,
    int Level = 0);
