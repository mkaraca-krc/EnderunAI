using EnderunAI.Api.Contracts.Engineering;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/engineering-recipes")]
public sealed class EngineeringRecipesController(
    AppDbContext db) : ControllerBase
{
    [HttpGet("position/{positionId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringView)]
    public async Task<IActionResult> GetByPosition(
        Guid positionId,
        CancellationToken cancellationToken)
    {
        var items = await db.EngineeringRecipes
            .AsNoTracking()
            .Where(x => x.EngineeringPositionId == positionId)
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.Version)
            .Select(x => new
            {
                x.Id,
                x.EngineeringPositionId,
                PositionCode = x.EngineeringPosition.Code,
                PositionName = x.EngineeringPosition.Name,
                x.Version,
                x.Description,
                x.IsDefault,
                MaterialCount = x.Materials.Count,
                LaborCount = x.Labors.Count,
                MachineCount = x.Machines.Count,
                TotalLaborHours = x.Labors.Sum(
                    y => y.PersonCount * y.Hours),
                x.CreatedAtUtc,
                x.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringView)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await db.EngineeringRecipes
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.EngineeringPositionId,
                PositionCode = x.EngineeringPosition.Code,
                PositionName = x.EngineeringPosition.Name,
                x.Version,
                x.Description,
                x.IsDefault,

                Materials = x.Materials
                    .OrderBy(y => y.MaterialName)
                    .Select(y => new
                    {
                        y.Id,
                        y.InventoryItemId,
                        y.MaterialCode,
                        y.MaterialName,
                        y.Quantity,
                        y.Unit,
                        y.WastePercent,
                        EffectiveQuantity =
                            y.Quantity *
                            (1 + y.WastePercent / 100),
                        y.Notes
                    }),

                Labors = x.Labors
                    .OrderBy(y => y.LaborType)
                    .Select(y => new
                    {
                        y.Id,
                        y.LaborType,
                        y.PersonCount,
                        y.Hours,
                        TotalHours =
                            y.PersonCount * y.Hours,
                        y.Notes
                    }),

                Machines = x.Machines
                    .OrderBy(y => y.MachineName)
                    .Select(y => new
                    {
                        y.Id,
                        y.MachineName,
                        y.Quantity,
                        y.Hours,
                        TotalHours =
                            y.Quantity * y.Hours,
                        y.Notes
                    })
            })
            .SingleOrDefaultAsync(cancellationToken);

        return item is null
            ? NotFound(new { message = "Reçete bulunamadı." })
            : Ok(item);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    public async Task<IActionResult> Create(
        CreateEngineeringRecipeRequest request,
        CancellationToken cancellationToken)
    {
        var positionExists =
            await db.EngineeringPositions.AnyAsync(
                x => x.Id == request.EngineeringPositionId,
                cancellationToken);

        if (!positionExists)
        {
            return BadRequest(new
            {
                message = "Geçerli bir mühendislik pozu seçilmelidir."
            });
        }

        var validationError = ValidateRequest(
            request.Materials,
            request.Labors,
            request.Machines);

        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        var lastVersion = await db.EngineeringRecipes
            .Where(x =>
                x.EngineeringPositionId ==
                request.EngineeringPositionId)
            .Select(x => (int?)x.Version)
            .MaxAsync(cancellationToken) ?? 0;

        if (request.IsDefault)
        {
            var existingDefaults = await db.EngineeringRecipes
                .Where(x =>
                    x.EngineeringPositionId ==
                    request.EngineeringPositionId &&
                    x.IsDefault)
                .ToListAsync(cancellationToken);

            foreach (var existing in existingDefaults)
            {
                existing.IsDefault = false;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        var recipe = new EngineeringRecipe
        {
            EngineeringPositionId =
                request.EngineeringPositionId,
            Version = lastVersion + 1,
            Description = request.Description?.Trim(),
            IsDefault = request.IsDefault,

            Materials = request.Materials
                .Select(x => new EngineeringRecipeMaterial
                {
                    InventoryItemId = x.InventoryItemId,
                    MaterialCode = x.MaterialCode.Trim(),
                    MaterialName = x.MaterialName.Trim(),
                    Quantity = x.Quantity,
                    Unit = x.Unit.Trim(),
                    WastePercent = x.WastePercent,
                    Notes = x.Notes?.Trim()
                })
                .ToList(),

            Labors = request.Labors
                .Select(x => new EngineeringRecipeLabor
                {
                    LaborType =
                        (EngineeringLaborType)x.LaborType,
                    PersonCount = x.PersonCount,
                    Hours = x.Hours,
                    Notes = x.Notes?.Trim()
                })
                .ToList(),

            Machines = request.Machines
                .Select(x => new EngineeringRecipeMachine
                {
                    MachineName = x.MachineName.Trim(),
                    Quantity = x.Quantity,
                    Hours = x.Hours,
                    Notes = x.Notes?.Trim()
                })
                .ToList()
        };

        db.EngineeringRecipes.Add(recipe);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Reçete oluşturuldu.",
            recipe.Id,
            recipe.EngineeringPositionId,
            recipe.Version,
            recipe.IsDefault
        });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateEngineeringRecipeRequest request,
        CancellationToken cancellationToken)
    {
        var recipe = await db.EngineeringRecipes
            .Include(x => x.Materials)
            .Include(x => x.Labors)
            .Include(x => x.Machines)
            .SingleOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);

        if (recipe is null)
        {
            return NotFound(new
            {
                message = "Reçete bulunamadı."
            });
        }

        var validationError = ValidateRequest(
            request.Materials,
            request.Labors,
            request.Machines);

        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        if (request.IsDefault)
        {
            var defaults = await db.EngineeringRecipes
                .Where(x =>
                    x.EngineeringPositionId ==
                    recipe.EngineeringPositionId &&
                    x.Id != recipe.Id &&
                    x.IsDefault)
                .ToListAsync(cancellationToken);

            foreach (var existing in defaults)
            {
                existing.IsDefault = false;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        db.EngineeringRecipeMaterials
            .RemoveRange(recipe.Materials);

        db.EngineeringRecipeLabors
            .RemoveRange(recipe.Labors);

        db.EngineeringRecipeMachines
            .RemoveRange(recipe.Machines);

        recipe.Description = request.Description?.Trim();
        recipe.IsDefault = request.IsDefault;
        recipe.UpdatedAtUtc = DateTime.UtcNow;

        recipe.Materials = request.Materials
            .Select(x => new EngineeringRecipeMaterial
            {
                InventoryItemId = x.InventoryItemId,
                MaterialCode = x.MaterialCode.Trim(),
                MaterialName = x.MaterialName.Trim(),
                Quantity = x.Quantity,
                Unit = x.Unit.Trim(),
                WastePercent = x.WastePercent,
                Notes = x.Notes?.Trim()
            })
            .ToList();

        recipe.Labors = request.Labors
            .Select(x => new EngineeringRecipeLabor
            {
                LaborType =
                    (EngineeringLaborType)x.LaborType,
                PersonCount = x.PersonCount,
                Hours = x.Hours,
                Notes = x.Notes?.Trim()
            })
            .ToList();

        recipe.Machines = request.Machines
            .Select(x => new EngineeringRecipeMachine
            {
                MachineName = x.MachineName.Trim(),
                Quantity = x.Quantity,
                Hours = x.Hours,
                Notes = x.Notes?.Trim()
            })
            .ToList();

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Reçete güncellendi.",
            recipe.Id
        });
    }

    private static string? ValidateRequest(
        IReadOnlyCollection<RecipeMaterialRequest> materials,
        IReadOnlyCollection<RecipeLaborRequest> labors,
        IReadOnlyCollection<RecipeMachineRequest> machines)
    {
        if (materials.Any(x =>
                string.IsNullOrWhiteSpace(x.MaterialName) ||
                string.IsNullOrWhiteSpace(x.Unit) ||
                x.Quantity <= 0 ||
                x.WastePercent < 0))
        {
            return "Malzeme reçetesi bilgileri geçersiz.";
        }

        if (labors.Any(x =>
                !Enum.IsDefined(
                    typeof(EngineeringLaborType),
                    x.LaborType) ||
                x.PersonCount <= 0 ||
                x.Hours <= 0))
        {
            return "İşçilik reçetesi bilgileri geçersiz.";
        }

        if (machines.Any(x =>
                string.IsNullOrWhiteSpace(x.MachineName) ||
                x.Quantity <= 0 ||
                x.Hours <= 0))
        {
            return "Makine reçetesi bilgileri geçersiz.";
        }

        return null;
    }
}
