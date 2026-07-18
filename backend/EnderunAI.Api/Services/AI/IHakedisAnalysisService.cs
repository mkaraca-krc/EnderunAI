using EnderunAI.Api.Contracts;

namespace EnderunAI.Api.Services.AI;

public interface IHakedisAnalysisService
{
    Task<HakedisAnalysisResult> AnalyzeAsync(
        string fullPath,
        string originalFileName,
        CancellationToken cancellationToken = default
    );
}