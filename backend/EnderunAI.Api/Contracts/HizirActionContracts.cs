namespace EnderunAI.Api.Contracts;

public enum HizirActionType
{
    RefreshDashboard = 0,
    CreatePurchaseRequest = 1
}

public sealed record HizirActionRequest(
    HizirActionType ActionType,
    bool Confirmed,
    Guid? CompanyId,
    Guid? ProjectId,
    string? Description,
    DateTime? NeededByDate,
    string? RequestedByName
);

public sealed record HizirActionPreview(
    HizirActionType ActionType,
    bool RequiresConfirmation,
    string Summary,
    IReadOnlyList<string> Warnings
);

public sealed record HizirActionResult(
    HizirActionType ActionType,
    bool Executed,
    string Message,
    Guid? CreatedRecordId,
    object? Data,
    DateTime ExecutedAtUtc
);