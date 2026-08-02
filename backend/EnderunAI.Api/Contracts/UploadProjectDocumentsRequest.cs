using Microsoft.AspNetCore.Http;

namespace EnderunAI.Api.Contracts;

public sealed class UploadProjectDocumentsRequest
{
    public List<IFormFile> Files { get; set; } = [];
    public string Folder { get; set; } = string.Empty;
    public Guid? ProjectSiteId { get; set; }
    public string? Description { get; set; }
}

public sealed class UpdateProjectDocumentRequest
{
    public string? Description { get; set; }
}
