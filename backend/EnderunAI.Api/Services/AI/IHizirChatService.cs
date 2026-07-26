using EnderunAI.Api.Contracts;

namespace EnderunAI.Api.Services.AI;

public interface IHizirChatService
{
    Task<HizirChatResponse> ReplyAsync(HizirChatRequest request, CancellationToken cancellationToken = default);
}
