using Microsoft.AspNetCore.Http;

namespace EnderunAI.Api.Services.Upload;

public sealed class UploadService : IUploadService
{
    private const long MaxFileSize = 50 * 1024 * 1024;

    private readonly string _uploadRoot =
        "/var/www/enderun-ai/uploads";

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf",
            ".xlsx",
            ".xls",
            ".csv",
            ".doc",
            ".docx"
        };

    public async Task<UploadedFileResult> SaveAsync(
        IFormFile file,
        string category,
        CancellationToken cancellationToken = default
    )
    {
        if (file is null || file.Length == 0)
        {
            throw new InvalidOperationException(
                "Dosya seçilmedi."
            );
        }

        if (file.Length > MaxFileSize)
        {
            throw new InvalidOperationException(
                "Dosya boyutu 50 MB sınırını aşamaz."
            );
        }

        var extension = Path
            .GetExtension(file.FileName)
            .ToLowerInvariant();

        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException(
                "Bu dosya türüne izin verilmiyor."
            );
        }

        var safeCategory = SanitizeCategory(category);

        var categoryPath = Path.Combine(
            _uploadRoot,
            safeCategory
        );

        Directory.CreateDirectory(categoryPath);

        var originalName =
            Path.GetFileName(file.FileName);

        var storedName =
            $"{DateTime.UtcNow:yyyyMMddHHmmss}_" +
            $"{Guid.NewGuid():N}{extension}";

        var fullPath = Path.Combine(
            categoryPath,
            storedName
        );

        await using var stream = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None
        );

        await file.CopyToAsync(
            stream,
            cancellationToken
        );

        var fileInfo = new FileInfo(fullPath);

        return new UploadedFileResult(
            OriginalName: originalName,
            StoredName: storedName,
            Extension: extension,
            ContentType: ResolveContentType(extension),
            Size: fileInfo.Length,
            UploadedAtUtc: fileInfo.CreationTimeUtc
        );
    }

    public IReadOnlyList<UploadedFileResult> GetFiles(
        string category
    )
    {
        var categoryPath = GetCategoryPath(category);

        if (!Directory.Exists(categoryPath))
        {
            return Array.Empty<UploadedFileResult>();
        }

        return Directory
            .EnumerateFiles(categoryPath)
            .Select(path =>
            {
                var info = new FileInfo(path);

                return new UploadedFileResult(
                    OriginalName: info.Name,
                    StoredName: info.Name,
                    Extension: info.Extension,
                    ContentType:
                        ResolveContentType(info.Extension),
                    Size: info.Length,
                    UploadedAtUtc: info.CreationTimeUtc
                );
            })
            .OrderByDescending(x => x.UploadedAtUtc)
            .ToArray();
    }

    public FileDownloadResult? GetFile(
        string category,
        string storedName
    )
    {
        var safeFileName =
            Path.GetFileName(storedName);

        var fullPath = Path.Combine(
            GetCategoryPath(category),
            safeFileName
        );

        if (!File.Exists(fullPath))
        {
            return null;
        }

        return new FileDownloadResult(
            FullPath: fullPath,
            StoredName: safeFileName,
            ContentType: ResolveContentType(
                Path.GetExtension(fullPath)
            )
        );
    }

    public bool DeleteFile(
        string category,
        string storedName
    )
    {
        var safeFileName =
            Path.GetFileName(storedName);

        var fullPath = Path.Combine(
            GetCategoryPath(category),
            safeFileName
        );

        if (!File.Exists(fullPath))
        {
            return false;
        }

        File.Delete(fullPath);
        return true;
    }

    private string GetCategoryPath(string category)
    {
        return Path.Combine(
            _uploadRoot,
            SanitizeCategory(category)
        );
    }

    private static string SanitizeCategory(
        string category
    )
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return "genel";
        }

        var invalidCharacters =
            Path.GetInvalidFileNameChars();

        var cleaned = new string(
            category
                .Trim()
                .ToLowerInvariant()
                .Where(character =>
                    !invalidCharacters.Contains(character)
                )
                .ToArray()
        );

        return string.IsNullOrWhiteSpace(cleaned)
            ? "genel"
            : cleaned;
    }

    private static string ResolveContentType(
        string extension
    )
    {
        return extension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",

            ".xlsx" =>
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",

            ".xls" =>
                "application/vnd.ms-excel",

            ".csv" =>
                "text/csv",

            ".doc" =>
                "application/msword",

            ".docx" =>
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",

            _ =>
                "application/octet-stream"
        };
    }
}
