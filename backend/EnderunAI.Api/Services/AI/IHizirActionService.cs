using EnderunAI.Api.Contracts;

namespace EnderunAI.Api.Services.AI;

public interface IHizirActionService
{
    Task<HizirActionPreview> PreviewAsync(
        HizirActionRequest request,
        CancellationToken cancellationToken = default);

    Task<HizirActionResult> ExecuteAsync(
        HizirActionRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default);
}