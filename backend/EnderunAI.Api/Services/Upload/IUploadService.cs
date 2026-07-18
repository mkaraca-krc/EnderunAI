using Microsoft.AspNetCore.Http;

namespace EnderunAI.Api.Services.Upload;

public interface IUploadService
{
    Task<UploadedFileResult> SaveAsync(
        IFormFile file,
        string category,
        CancellationToken cancellationToken = default
    );

    IReadOnlyList<UploadedFileResult> GetFiles(string category);

    FileDownloadResult? GetFile(
        string category,
        string storedName
    );

    bool DeleteFile(
        string category,
        string storedName
    );
}

public sealed record UploadedFileResult(
    string OriginalName,
    string StoredName,
    string Extension,
    string ContentType,
    long Size,
    DateTime UploadedAtUtc
);

public sealed record FileDownloadResult(
    string FullPath,
    string StoredName,
    string ContentType
);