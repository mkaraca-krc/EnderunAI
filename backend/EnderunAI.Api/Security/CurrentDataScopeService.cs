using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;

namespace EnderunAI.Api.Security;

public sealed record CurrentDataScopeSnapshot(
    bool HasGlobalAccess,
    IReadOnlySet<Guid> CompanyIds,
    IReadOnlySet<Guid> BranchIds,
    IReadOnlySet<Guid> ProjectIds,
    IReadOnlySet<Guid> VisibleCompanyIds,
    IReadOnlySet<Guid> VisibleBranchIds,
    IReadOnlySet<Guid> SiteIds)
{
    public IQueryable<Company> Apply(IQueryable<Company> query) =>
        HasGlobalAccess
            ? query
            : query.Where(item => VisibleCompanyIds.Contains(item.Id));

    public IQueryable<Branch> Apply(IQueryable<Branch> query) =>
        HasGlobalAccess
            ? query
            : query.Where(item =>
                CompanyIds.Contains(item.CompanyId) ||
                VisibleBranchIds.Contains(item.Id));

    public IQueryable<Project> Apply(IQueryable<Project> query) =>
        HasGlobalAccess
            ? query
            : query.Where(item =>
                CompanyIds.Contains(item.CompanyId) ||
                BranchIds.Contains(item.BranchId) ||
                ProjectIds.Contains(item.Id));

    /// <summary>
    /// Personel listesini kapsama daraltır.
    ///
    /// NEDEN AYRI BİR AŞIRI YÜKLEME: personel şantiyeye doğrudan değil,
    /// <see cref="ProjectSiteAssignment"/> üzerinden bağlı. Süzgeci her
    /// kontrolcüde ayrı yazmak, "aktif atama" tanımının (EndDate boş VE
    /// IsActive) yerlere göre kaymasına yol açardı. Şirket/şube kapsamı
    /// da burada, tek yerde.
    ///
    /// ŞANTİYE KAPSAMLI KULLANICI (Şantiye Şefi, Formen) yalnızca KENDİ
    /// şantiyelerine ATANMIŞ personeli görür. Şirket kapsamı olan
    /// kullanıcı o şirketin tümünü görür.
    /// </summary>
    public IQueryable<Personnel> Apply(IQueryable<Personnel> query) =>
        HasGlobalAccess
            ? query
            : query.Where(item =>
                CompanyIds.Contains(item.CompanyId) ||
                (item.BranchId.HasValue &&
                    BranchIds.Contains(item.BranchId.Value)) ||
                item.SiteAssignments.Any(assignment =>
                    assignment.IsActive &&
                    !assignment.IsDeleted &&
                    assignment.EndDate == null &&
                    SiteIds.Contains(assignment.ProjectSiteId)));

    public bool CanAccessCompany(Guid companyId) =>
        HasGlobalAccess || CompanyIds.Contains(companyId);

    public bool CanAccessBranch(Guid companyId, Guid branchId) =>
        HasGlobalAccess ||
        CompanyIds.Contains(companyId) ||
        BranchIds.Contains(branchId);

    public bool CanAccessProject(
        Guid companyId,
        Guid branchId,
        Guid projectId) =>
        HasGlobalAccess ||
        CompanyIds.Contains(companyId) ||
        BranchIds.Contains(branchId) ||
        ProjectIds.Contains(projectId);

    public bool CanAccessSite(
        Guid companyId,
        Guid branchId,
        Guid projectId,
        Guid projectSiteId) =>
        HasGlobalAccess ||
        CompanyIds.Contains(companyId) ||
        BranchIds.Contains(branchId) ||
        ProjectIds.Contains(projectId) ||
        SiteIds.Contains(projectSiteId);

    /// <summary>
    /// Proje Dosya Merkezi için: normal proje-kapsamlı roller
    /// CanAccessProject ile geçer; Site-kapsamlı roller (Şantiye
    /// Şefi/Formen) CanAccessProject'te hiç yer almaz (SiteIds orada
    /// kontrol edilmiyor) — bu yüzden projenin atandıkları herhangi bir
    /// şantiyesi varsa da erişim veriliyor. Böylece Şantiye Şefi/Formen,
    /// atandığı şantiyenin bulunduğu projenin genel VE şantiyeye özel
    /// tüm dosyalarını görebilir.
    /// </summary>
    public bool CanAccessProjectDocuments(
        Guid companyId,
        Guid branchId,
        Guid projectId,
        IReadOnlyCollection<Guid> projectSiteIds) =>
        CanAccessProject(companyId, branchId, projectId) ||
        projectSiteIds.Any(SiteIds.Contains);
}

public interface ICurrentDataScopeService
{
    Task<CurrentDataScopeSnapshot?> GetAsync(
        CancellationToken cancellationToken = default);
}

public sealed class CurrentDataScopeService(
    ICurrentUserService currentUser,
    IUserAuthorizationService authorizationService)
    : ICurrentDataScopeService
{
    private const int AllScope = 0;
    private const int CompanyScope = 1;
    private const int BranchScope = 2;
    private const int ProjectScope = 3;
    private const int SiteScope = 4;

    public async Task<CurrentDataScopeSnapshot?> GetAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not Guid userId)
            return null;

        var authorization = await authorizationService.GetAsync(
            userId,
            cancellationToken);
        if (authorization is null || !authorization.IsActive)
            return null;

        var hasGlobalAccess =
            authorization.RoleNames.Contains(
                "Admin",
                StringComparer.OrdinalIgnoreCase) ||
            authorization.DataScopes.Any(item =>
                item.ScopeType == AllScope);

        return new CurrentDataScopeSnapshot(
            hasGlobalAccess,
            authorization.DataScopes
                .Where(item =>
                    item.ScopeType == CompanyScope &&
                    item.CompanyId.HasValue)
                .Select(item => item.CompanyId!.Value)
                .ToHashSet(),
            authorization.DataScopes
                .Where(item =>
                    item.ScopeType == BranchScope &&
                    item.BranchId.HasValue)
                .Select(item => item.BranchId!.Value)
                .ToHashSet(),
            authorization.DataScopes
                .Where(item =>
                    item.ScopeType == ProjectScope &&
                    item.ProjectId.HasValue)
                .Select(item => item.ProjectId!.Value)
                .ToHashSet(),
            authorization.DataScopes
                .Where(item => item.CompanyId.HasValue)
                .Select(item => item.CompanyId!.Value)
                .ToHashSet(),
            authorization.DataScopes
                .Where(item => item.BranchId.HasValue)
                .Select(item => item.BranchId!.Value)
                .ToHashSet(),
            authorization.DataScopes
                .Where(item =>
                    item.ScopeType == SiteScope &&
                    item.ProjectSiteId.HasValue)
                .Select(item => item.ProjectSiteId!.Value)
                .ToHashSet());
    }
}
