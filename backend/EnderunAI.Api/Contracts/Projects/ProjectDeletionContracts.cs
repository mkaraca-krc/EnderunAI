namespace EnderunAI.Api.Contracts.Projects;

/// <summary>
/// Kalıcı silmeyi engelleyen "kesinleşmiş" kayıt kalemi. Bir tane bile
/// varsa proje kalıcı silinemez, yalnızca arşive alınabilir.
/// </summary>
public sealed record ProjectDeletionBlocker(
    string Key,
    string Label,
    int Count,
    string Reason);

/// <summary>
/// Projeye bağlı ama kesinleşmiş sayılmayan kayıt kalemi. Kalıcı silmede
/// projeyle birlikte gider; kullanıcıya onay öncesi sayı olarak gösterilir.
/// </summary>
public sealed record ProjectDeletionDependency(
    string Key,
    string Label,
    int Count);

/// <summary>Silme öncesi etki özeti — hem arşiv hem kalıcı silme kararı bunun üstünden verilir.</summary>
public sealed record ProjectDeletionImpact(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    bool IsArchived,
    bool CanHardDelete,
    IReadOnlyList<ProjectDeletionBlocker> Blockers,
    IReadOnlyList<ProjectDeletionDependency> Dependencies,
    int TotalBlockingRecords,
    int TotalDependentRecords,
    int DocumentCount,
    long DocumentSizeBytes);

public sealed record ArchiveProjectRequest(string? Reason);

/// <summary>
/// Kalıcı silme isteği. <paramref name="ConfirmationCode"/> projenin
/// kodudur; kullanıcının elle yazması ikinci onay kademesidir.
/// </summary>
public sealed record DeleteProjectRequest(string ConfirmationCode);
