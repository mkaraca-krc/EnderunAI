namespace EnderunAI.Api.Contracts;

public sealed record HizirChatRequest(
    string Message,
    IReadOnlyList<HizirChatMessage>? History = null
);

public sealed record HizirChatMessage(
    string Role,
    string Content
);

public sealed record HizirChatResponse(
    string Reply,
    string AssistantName,
    DateTime CreatedAtUtc
);
