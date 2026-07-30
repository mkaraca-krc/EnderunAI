namespace EnderunAI.Api.Contracts.ProjectBoqs;

public sealed record ProjectBoqListItemDto(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string BoqNumber,
    string Name,
    int RevisionNumber,
    int Status,
    bool IsCurrentRevision,
    string CurrencyCode,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    bool IsActive,
    int ItemCount
);

public sealed record ProjectBoqItemDto(
    Guid Id,
    Guid ProjectBoqId,
    Guid? EngineeringPositionId,
    int LineNumber,
    string PositionCode,
    string Description,
    string Unit,
    decimal ContractQuantity,
    decimal UnitPrice,
    decimal TotalAmount,
    int ItemType,
    string? Category,
    string? Notes,
    bool IsActive
);

public sealed record ProjectBoqDetailDto(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string BoqNumber,
    string Name,
    int RevisionNumber,
    int Status,
    bool IsCurrentRevision,
    string CurrencyCode,
    decimal TotalAmount,
    DateTime? ApprovedAtUtc,
    Guid? ApprovedByUserId,
    string? Description,
    string? Notes,
    DateTime CreatedAtUtc,
    bool IsActive,
    IReadOnlyList<ProjectBoqItemDto> Items
);
