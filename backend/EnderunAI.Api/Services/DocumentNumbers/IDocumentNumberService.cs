namespace EnderunAI.Api.Services.DocumentNumbers;

public interface IDocumentNumberService
{
    Task<string> GenerateAsync(
        Guid companyId,
        string documentType,
        string prefix,
        CancellationToken cancellationToken = default);
}
