namespace EnderunAI.Api.Contracts;

public sealed record HizirChatHistoryItem(string Role, string Content);
public sealed record HizirChatRequest(string Message, IReadOnlyList<HizirChatHistoryItem>? History);
public sealed record HizirChatResponse(string Reply, string AssistantName, DateTime CreatedAtUtc);
