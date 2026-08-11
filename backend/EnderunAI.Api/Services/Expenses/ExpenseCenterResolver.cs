using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Expenses;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Expenses;

/// <summary>Bir gider merkezinin kimliği ve görünen adı.</summary>
public sealed record ExpenseCenterRef(
    ExpenseCenterType Type,
    Guid Id,
    string Name,
    /// <summary>Şantiyenin bağlı olduğu proje; merkez/proje için boş.</summary>
    Guid? ParentProjectId = null,
    bool IsHeadOffice = false);

/// <summary>
/// Gider merkezlerini kaynaklarından türetir ve doğrular.
///
/// Merkez listesi ŞUBE + PROJE + ŞANTİYE birleşimidir; ayrı bir tanım
/// tablosu yok (bkz. <see cref="ExpenseCenterType"/>). Doğrulama tek
/// yerde toplandı: gider kaydı, tekrarlayan şablon ve rapor aynı
/// kurala uyar, biri "kapalı projeye gider yazılabilir" derken
/// diğerinin aksini söylemesi mümkün olmaz.
/// </summary>
public sealed class ExpenseCenterResolver(AppDbContext db)
{
    /// <summary>
    /// Şirketin bütün gider merkezleri. Sıra: önce merkez ofis,
    /// sonra diğer şubeler, sonra projeler ve şantiyeleri.
    /// </summary>
    public async Task<List<ExpenseCenterRef>> ListAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        var branches = await db.Branches
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.IsHeadOffice).ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.IsHeadOffice })
            .ToListAsync(cancellationToken);

        var projects = await db.Projects
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        var projectIds = projects.Select(x => x.Id).ToList();

        var sites = await db.ProjectSites
            .AsNoTracking()
            .Where(x => projectIds.Contains(x.ProjectId))
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.ProjectId })
            .ToListAsync(cancellationToken);

        var result = branches
            .Select(x => new ExpenseCenterRef(
                ExpenseCenterType.Branch, x.Id,
                x.IsHeadOffice ? $"{x.Name} (Merkez)" : x.Name,
                IsHeadOffice: x.IsHeadOffice))
            .ToList();

        foreach (var project in projects)
        {
            result.Add(new ExpenseCenterRef(
                ExpenseCenterType.Project, project.Id, project.Name));

            result.AddRange(sites
                .Where(x => x.ProjectId == project.Id)
                .Select(x => new ExpenseCenterRef(
                    ExpenseCenterType.ProjectSite, x.Id,
                    $"{project.Name} — {x.Name}", project.Id)));
        }

        return result;
    }

    /// <summary>
    /// Merkezi doğrular ve adını çözer. Bulunamazsa null döner —
    /// çağıran 400 verir. Başka şirketin projesine gider yazılmasını
    /// da bu engelliyor: sorgu her zaman companyId ile daraltılıyor.
    /// </summary>
    public async Task<ExpenseCenterRef?> ResolveAsync(
        Guid companyId, ExpenseCenterType type, Guid centerId,
        CancellationToken cancellationToken)
    {
        switch (type)
        {
            case ExpenseCenterType.Branch:
                var branch = await db.Branches
                    .AsNoTracking()
                    .Where(x => x.Id == centerId && x.CompanyId == companyId)
                    .Select(x => new { x.Name, x.IsHeadOffice })
                    .SingleOrDefaultAsync(cancellationToken);

                return branch is null
                    ? null
                    : new ExpenseCenterRef(
                        type, centerId,
                        branch.IsHeadOffice ? $"{branch.Name} (Merkez)" : branch.Name,
                        IsHeadOffice: branch.IsHeadOffice);

            case ExpenseCenterType.Project:
                var project = await db.Projects
                    .AsNoTracking()
                    .Where(x => x.Id == centerId && x.CompanyId == companyId)
                    .Select(x => x.Name)
                    .SingleOrDefaultAsync(cancellationToken);

                return project is null
                    ? null
                    : new ExpenseCenterRef(type, centerId, project);

            case ExpenseCenterType.ProjectSite:
                var site = await db.ProjectSites
                    .AsNoTracking()
                    .Where(x => x.Id == centerId && x.Project.CompanyId == companyId)
                    .Select(x => new { x.Name, ProjectName = x.Project.Name, x.ProjectId })
                    .SingleOrDefaultAsync(cancellationToken);

                return site is null
                    ? null
                    : new ExpenseCenterRef(
                        type, centerId, $"{site.ProjectName} — {site.Name}",
                        site.ProjectId);

            default:
                return null;
        }
    }
}
