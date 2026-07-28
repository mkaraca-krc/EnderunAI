namespace EnderunAI.Api.Models;

public sealed class EngineeringRecipe : BaseEntity
{
    public Guid EngineeringPositionId { get; set; }
    public EngineeringPosition EngineeringPosition { get; set; } = null!;

    public int Version { get; set; } = 1;
    public string? Description { get; set; }
    public bool IsDefault { get; set; } = true;

    public ICollection<EngineeringRecipeMaterial> Materials { get; set; }
        = new List<EngineeringRecipeMaterial>();

    public ICollection<EngineeringRecipeLabor> Labors { get; set; }
        = new List<EngineeringRecipeLabor>();

    public ICollection<EngineeringRecipeMachine> Machines { get; set; }
        = new List<EngineeringRecipeMachine>();
}

public sealed class EngineeringRecipeMaterial : BaseEntity
{
    public Guid EngineeringRecipeId { get; set; }
    public EngineeringRecipe EngineeringRecipe { get; set; } = null!;

    public Guid? InventoryItemId { get; set; }

    public string MaterialCode { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;

    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal WastePercent { get; set; }

    public string? Notes { get; set; }
}

public enum EngineeringLaborType
{
    Master = 0,
    Helper = 1,
    Technician = 2,
    Engineer = 3,
    Foreman = 4,
    TestEngineer = 5,
    Other = 99
}

public sealed class EngineeringRecipeLabor : BaseEntity
{
    public Guid EngineeringRecipeId { get; set; }
    public EngineeringRecipe EngineeringRecipe { get; set; } = null!;

    public EngineeringLaborType LaborType { get; set; }

    public decimal PersonCount { get; set; } = 1;
    public decimal Hours { get; set; }

    public string? Notes { get; set; }
}

public sealed class EngineeringRecipeMachine : BaseEntity
{
    public Guid EngineeringRecipeId { get; set; }
    public EngineeringRecipe EngineeringRecipe { get; set; } = null!;

    public string MachineName { get; set; } = string.Empty;

    public decimal Quantity { get; set; } = 1;
    public decimal Hours { get; set; }

    public string? Notes { get; set; }
}
