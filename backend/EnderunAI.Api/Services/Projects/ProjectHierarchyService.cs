using EnderunAI.Api.Contracts.Projects;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Projects;

public sealed class ProjectHierarchyService(AppDbContext db)
    : IProjectHierarchyService
{
    public async Task<ProjectHierarchyTreeDto> GetTreeAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new { x.Id, x.Code, x.Name })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound("Proje bulunamadı.");

        var levels = await db.ProjectHierarchyLevels
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new ProjectHierarchyLevelDto(
                x.Id,
                x.Code,
                x.Name,
                x.SortOrder,
                x.IsRequired,
                x.Nodes.Count))
            .ToListAsync(cancellationToken);

        var nodes = await db.ProjectHierarchyNodes
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.Level.SortOrder)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new NodeRow(
                x.Id,
                x.LevelId,
                x.Level.Name,
                x.Level.SortOrder,
                x.ParentNodeId,
                x.Code,
                x.Name,
                x.Description,
                x.SortOrder))
            .ToListAsync(cancellationToken);

        var scopeCounts = await db.ProjectModuleScopes
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .GroupBy(x => new
            {
                x.ProjectHierarchyNodeId,
                x.ModuleType
            })
            .Select(group => new ScopeCountRow(
                group.Key.ProjectHierarchyNodeId,
                group.Key.ModuleType,
                group.Count()))
            .ToListAsync(cancellationToken);

        return new ProjectHierarchyTreeDto(
            project.Id,
            project.Code,
            project.Name,
            levels,
            BuildTree(nodes, scopeCounts));
    }

    public async Task<ProjectHierarchyLevelDto> CreateLevelAsync(
        Guid projectId,
        CreateProjectHierarchyLevelRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureProjectAsync(projectId, cancellationToken);

        var code = NormalizeCode(request.Code, 40);
        var name = RequireText(request.Name, "Seviye adı", 100);

        if (request.SortOrder < 0)
            throw BadRequest("Seviye sırası sıfırdan küçük olamaz.");

        if (await db.ProjectHierarchyLevels.AnyAsync(
                x => x.ProjectId == projectId &&
                     (x.Code == code || x.SortOrder == request.SortOrder),
                cancellationToken))
        {
            throw Conflict(
                "Aynı kodda veya sırada başka bir hiyerarşi seviyesi bulunuyor.");
        }

        var level = new ProjectHierarchyLevel
        {
            ProjectId = projectId,
            Code = code,
            Name = name,
            SortOrder = request.SortOrder,
            IsRequired = request.IsRequired
        };

        db.ProjectHierarchyLevels.Add(level);
        await db.SaveChangesAsync(cancellationToken);

        return ToLevelDto(level, 0);
    }

    public async Task<ProjectHierarchyLevelDto> UpdateLevelAsync(
        Guid projectId,
        Guid levelId,
        UpdateProjectHierarchyLevelRequest request,
        CancellationToken cancellationToken)
    {
        var level = await db.ProjectHierarchyLevels
            .SingleOrDefaultAsync(
                x => x.Id == levelId && x.ProjectId == projectId,
                cancellationToken)
            ?? throw NotFound("Hiyerarşi seviyesi bulunamadı.");

        var code = NormalizeCode(request.Code, 40);
        var name = RequireText(request.Name, "Seviye adı", 100);

        if (request.SortOrder < 0)
            throw BadRequest("Seviye sırası sıfırdan küçük olamaz.");

        if (await db.ProjectHierarchyLevels.AnyAsync(
                x => x.ProjectId == projectId &&
                     x.Id != levelId &&
                     (x.Code == code || x.SortOrder == request.SortOrder),
                cancellationToken))
        {
            throw Conflict(
                "Aynı kodda veya sırada başka bir hiyerarşi seviyesi bulunuyor.");
        }

        if (level.SortOrder != request.SortOrder &&
            await db.ProjectHierarchyNodes.AnyAsync(
                x => x.LevelId == levelId,
                cancellationToken))
        {
            throw Conflict(
                "Düğüm içeren bir seviyenin sırası değiştirilemez.");
        }

        level.Code = code;
        level.Name = name;
        level.SortOrder = request.SortOrder;
        level.IsRequired = request.IsRequired;
        level.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        var nodeCount = await db.ProjectHierarchyNodes.CountAsync(
            x => x.LevelId == levelId,
            cancellationToken);

        return ToLevelDto(level, nodeCount);
    }

    public async Task<bool> DeleteLevelAsync(
        Guid projectId,
        Guid levelId,
        CancellationToken cancellationToken)
    {
        var level = await db.ProjectHierarchyLevels
            .SingleOrDefaultAsync(
                x => x.Id == levelId && x.ProjectId == projectId,
                cancellationToken)
            ?? throw NotFound("Hiyerarşi seviyesi bulunamadı.");

        if (await db.ProjectHierarchyNodes.AnyAsync(
                x => x.LevelId == levelId,
                cancellationToken))
        {
            throw Conflict(
                "Bu seviyede düğüm bulunduğu için seviye silinemez.");
        }

        db.ProjectHierarchyLevels.Remove(level);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ProjectHierarchyNodeDto> CreateNodeAsync(
        Guid projectId,
        CreateProjectHierarchyNodeRequest request,
        CancellationToken cancellationToken)
    {
        var level = await GetLevelAsync(
            projectId,
            request.LevelId,
            cancellationToken);

        var parent = await GetAndValidateParentAsync(
            projectId,
            request.ParentNodeId,
            level.SortOrder,
            null,
            cancellationToken);

        await EnsureNodeCodeIsUniqueAsync(
            projectId,
            request.Code,
            null,
            cancellationToken);

        var node = new ProjectHierarchyNode
        {
            ProjectId = projectId,
            LevelId = level.Id,
            ParentNodeId = parent?.Id,
            Code = NormalizeCode(request.Code, 60),
            Name = RequireText(request.Name, "Kırılım adı", 200),
            Description = TrimOptional(request.Description, 1000),
            SortOrder = EnsureSortOrder(request.SortOrder)
        };

        db.ProjectHierarchyNodes.Add(node);
        await db.SaveChangesAsync(cancellationToken);

        return await GetNodeDtoAsync(
            projectId,
            node.Id,
            cancellationToken);
    }

    public async Task<ProjectHierarchyNodeDto> UpdateNodeAsync(
        Guid projectId,
        Guid nodeId,
        UpdateProjectHierarchyNodeRequest request,
        CancellationToken cancellationToken)
    {
        var node = await db.ProjectHierarchyNodes
            .SingleOrDefaultAsync(
                x => x.Id == nodeId && x.ProjectId == projectId,
                cancellationToken)
            ?? throw NotFound("Proje kırılımı bulunamadı.");

        var level = await GetLevelAsync(
            projectId,
            request.LevelId,
            cancellationToken);

        var parent = await GetAndValidateParentAsync(
            projectId,
            request.ParentNodeId,
            level.SortOrder,
            nodeId,
            cancellationToken);

        var childLevelOrders = await db.ProjectHierarchyNodes
            .AsNoTracking()
            .Where(x => x.ParentNodeId == nodeId)
            .Select(x => x.Level.SortOrder)
            .ToListAsync(cancellationToken);

        if (childLevelOrders.Any(order => order <= level.SortOrder))
        {
            throw Conflict(
                "Seçilen seviye mevcut alt kırılımların seviyesinden önce olmalıdır.");
        }

        await EnsureNodeCodeIsUniqueAsync(
            projectId,
            request.Code,
            nodeId,
            cancellationToken);

        node.LevelId = level.Id;
        node.ParentNodeId = parent?.Id;
        node.Code = NormalizeCode(request.Code, 60);
        node.Name = RequireText(request.Name, "Kırılım adı", 200);
        node.Description = TrimOptional(request.Description, 1000);
        node.SortOrder = EnsureSortOrder(request.SortOrder);
        node.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        return await GetNodeDtoAsync(
            projectId,
            node.Id,
            cancellationToken);
    }

    public async Task<bool> DeleteNodeAsync(
        Guid projectId,
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        var node = await db.ProjectHierarchyNodes
            .SingleOrDefaultAsync(
                x => x.Id == nodeId && x.ProjectId == projectId,
                cancellationToken)
            ?? throw NotFound("Proje kırılımı bulunamadı.");

        if (await db.ProjectHierarchyNodes.AnyAsync(
                x => x.ParentNodeId == nodeId,
                cancellationToken))
        {
            throw Conflict(
                "Alt kırılımları bulunan bir düğüm silinemez.");
        }

        if (await db.ProjectModuleScopes.AnyAsync(
                x => x.ProjectHierarchyNodeId == nodeId,
                cancellationToken))
        {
            throw Conflict(
                "Modül kayıtları bağlı olan bir düğüm silinemez.");
        }

        db.ProjectHierarchyNodes.Remove(node);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ProjectModuleScopeDto> AssignModuleScopeAsync(
        Guid projectId,
        Guid nodeId,
        AssignProjectModuleScopeRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.ModuleType))
            throw BadRequest("Geçersiz modül türü.");

        if (request.RecordId == Guid.Empty)
            throw BadRequest("Bağlanacak kayıt seçilmelidir.");

        var nodeExists = await db.ProjectHierarchyNodes
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == nodeId && x.ProjectId == projectId,
                cancellationToken);

        if (!nodeExists)
            throw NotFound("Proje kırılımı bulunamadı.");

        await EnsureModuleRecordAsync(
            projectId,
            request.ModuleType,
            request.RecordId,
            cancellationToken);

        var scope = await db.ProjectModuleScopes
            .SingleOrDefaultAsync(
                x => x.ProjectId == projectId &&
                     x.ModuleType == request.ModuleType &&
                     x.RecordId == request.RecordId,
                cancellationToken);

        if (scope is null)
        {
            scope = new ProjectModuleScope
            {
                ProjectId = projectId,
                ProjectHierarchyNodeId = nodeId,
                ModuleType = request.ModuleType,
                RecordId = request.RecordId
            };
            db.ProjectModuleScopes.Add(scope);
        }
        else
        {
            scope.ProjectHierarchyNodeId = nodeId;
            scope.IsActive = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetScopeDtoAsync(scope.Id, cancellationToken);
    }

    public async Task<bool> RemoveModuleScopeAsync(
        Guid projectId,
        ProjectModuleType moduleType,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        var scope = await db.ProjectModuleScopes
            .SingleOrDefaultAsync(
                x => x.ProjectId == projectId &&
                     x.ModuleType == moduleType &&
                     x.RecordId == recordId,
                cancellationToken)
            ?? throw NotFound("Modül kırılım bağlantısı bulunamadı.");

        db.ProjectModuleScopes.Remove(scope);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ApplyHierarchyTemplateResult> ApplyMkeTemplateAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await EnsureProjectAsync(projectId, cancellationToken);

        if (await db.ProjectHierarchyLevels.AnyAsync(
                x => x.ProjectId == projectId,
                cancellationToken) ||
            await db.ProjectHierarchyNodes.AnyAsync(
                x => x.ProjectId == projectId,
                cancellationToken))
        {
            throw Conflict(
                "MKE şablonu yalnızca boş bir proje hiyerarşisine uygulanabilir.");
        }

        var cityLevel = new ProjectHierarchyLevel
        {
            ProjectId = projectId,
            Code = "SEHIR",
            Name = "Şehir",
            SortOrder = 10,
            IsRequired = true
        };
        var facilityLevel = new ProjectHierarchyLevel
        {
            ProjectId = projectId,
            Code = "FABRIKA",
            Name = "Fabrika / Yerleşke",
            SortOrder = 20,
            IsRequired = true
        };

        db.ProjectHierarchyLevels.AddRange(cityLevel, facilityLevel);

        var kirikkale = Node(
            projectId,
            cityLevel,
            null,
            "KIRIKKALE",
            "Kırıkkale",
            10);
        var ankara = Node(
            projectId,
            cityLevel,
            null,
            "ANKARA",
            "Ankara",
            20);
        var cankiri = Node(
            projectId,
            cityLevel,
            null,
            "CANKIRI",
            "Çankırı",
            30);

        db.ProjectHierarchyNodes.AddRange(kirikkale, ankara, cankiri);
        db.ProjectHierarchyNodes.AddRange(
            Node(projectId, facilityLevel, kirikkale, "BARUT", "Barut", 10),
            Node(projectId, facilityLevel, kirikkale, "MUHIMMAT", "Mühimmat", 20),
            Node(projectId, facilityLevel, kirikkale, "ARGE", "Ar-Ge", 30),
            Node(projectId, facilityLevel, ankara, "GAZI-FISEK", "Gazi Fişek", 10),
            Node(projectId, facilityLevel, cankiri, "FABRIKA", "Fabrika", 10));

        await db.SaveChangesAsync(cancellationToken);

        return new ApplyHierarchyTemplateResult(
            2,
            8,
            await GetTreeAsync(projectId, cancellationToken));
    }

    private async Task EnsureProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!await db.Projects.AsNoTracking().AnyAsync(
                x => x.Id == projectId,
                cancellationToken))
        {
            throw NotFound("Proje bulunamadı.");
        }
    }

    private async Task<ProjectHierarchyLevel> GetLevelAsync(
        Guid projectId,
        Guid levelId,
        CancellationToken cancellationToken)
    {
        return await db.ProjectHierarchyLevels
            .SingleOrDefaultAsync(
                x => x.Id == levelId &&
                     x.ProjectId == projectId &&
                     x.IsActive,
                cancellationToken)
            ?? throw BadRequest(
                "Seçilen hiyerarşi seviyesi bulunamadı veya pasif.");
    }

    private async Task<ProjectHierarchyNode?> GetAndValidateParentAsync(
        Guid projectId,
        Guid? parentNodeId,
        int childLevelOrder,
        Guid? currentNodeId,
        CancellationToken cancellationToken)
    {
        if (!parentNodeId.HasValue)
        {
            var firstLevelOrder = await db.ProjectHierarchyLevels
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId && x.IsActive)
                .MinAsync(x => (int?)x.SortOrder, cancellationToken);

            if (firstLevelOrder.HasValue &&
                firstLevelOrder.Value != childLevelOrder)
            {
                throw BadRequest(
                    "Kök düğümler projenin ilk hiyerarşi seviyesinde olmalıdır.");
            }

            return null;
        }

        if (parentNodeId == currentNodeId)
            throw BadRequest("Bir kırılım kendisinin üst kırılımı olamaz.");

        var parent = await db.ProjectHierarchyNodes
            .Include(x => x.Level)
            .SingleOrDefaultAsync(
                x => x.Id == parentNodeId &&
                     x.ProjectId == projectId &&
                     x.IsActive,
                cancellationToken)
            ?? throw BadRequest(
                "Seçilen üst kırılım bulunamadı veya pasif.");

        if (parent.Level.SortOrder >= childLevelOrder)
        {
            throw BadRequest(
                "Üst kırılımın seviyesi alt kırılım seviyesinden önce olmalıdır.");
        }

        if (currentNodeId.HasValue)
        {
            var cursor = parent;
            while (cursor.ParentNodeId.HasValue)
            {
                if (cursor.ParentNodeId == currentNodeId)
                    throw Conflict("Bu değişiklik hiyerarşide döngü oluşturur.");

                cursor = await db.ProjectHierarchyNodes
                    .AsNoTracking()
                    .SingleAsync(
                        x => x.Id == cursor.ParentNodeId.Value,
                        cancellationToken);
            }
        }

        return parent;
    }

    private async Task EnsureNodeCodeIsUniqueAsync(
        Guid projectId,
        string requestedCode,
        Guid? excludedNodeId,
        CancellationToken cancellationToken)
    {
        var code = NormalizeCode(requestedCode, 60);
        if (await db.ProjectHierarchyNodes.AnyAsync(
                x => x.ProjectId == projectId &&
                     x.Code == code &&
                     x.Id != excludedNodeId,
                cancellationToken))
        {
            throw Conflict("Bu kırılım kodu projede zaten kullanılıyor.");
        }
    }

    private async Task EnsureModuleRecordAsync(
        Guid projectId,
        ProjectModuleType moduleType,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        var exists = moduleType switch
        {
            ProjectModuleType.Hakedis => true,
            ProjectModuleType.Personnel =>
                await db.PersonnelAssignments.AsNoTracking().AnyAsync(
                    x => x.Id == recordId && x.ProjectId == projectId,
                    cancellationToken),
            ProjectModuleType.Warehouse =>
                await db.Warehouses.AsNoTracking().AnyAsync(
                    x => x.Id == recordId && x.ProjectId == projectId,
                    cancellationToken),
            ProjectModuleType.Purchasing =>
                await db.PurchaseRequests.AsNoTracking().AnyAsync(
                    x => x.Id == recordId && x.ProjectId == projectId,
                    cancellationToken),
            ProjectModuleType.Finance =>
                await db.AccountingVoucherLines.AsNoTracking().AnyAsync(
                    x => x.Id == recordId && x.ProjectId == projectId,
                    cancellationToken),
            _ => false
        };

        if (!exists)
        {
            throw BadRequest(
                "Bağlanacak modül kaydı bulunamadı veya bu projeye ait değil.");
        }
    }

    private async Task<ProjectHierarchyNodeDto> GetNodeDtoAsync(
        Guid projectId,
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        var tree = await GetTreeAsync(projectId, cancellationToken);
        return FindNode(tree.Nodes, nodeId)
            ?? throw NotFound("Proje kırılımı bulunamadı.");
    }

    private async Task<ProjectModuleScopeDto> GetScopeDtoAsync(
        Guid scopeId,
        CancellationToken cancellationToken)
    {
        var scope = await db.ProjectModuleScopes
            .AsNoTracking()
            .Where(x => x.Id == scopeId)
            .Select(x => new
            {
                x.Id,
                x.ProjectId,
                x.ProjectHierarchyNodeId,
                x.ModuleType,
                x.RecordId
            })
            .SingleAsync(cancellationToken);

        var tree = await GetTreeAsync(scope.ProjectId, cancellationToken);
        var node = FindNode(tree.Nodes, scope.ProjectHierarchyNodeId)
            ?? throw NotFound("Proje kırılımı bulunamadı.");

        return new ProjectModuleScopeDto(
            scope.Id,
            scope.ProjectId,
            scope.ProjectHierarchyNodeId,
            node.Path,
            scope.ModuleType,
            scope.RecordId);
    }

    private static IReadOnlyList<ProjectHierarchyNodeDto> BuildTree(
        IReadOnlyList<NodeRow> nodes,
        IReadOnlyList<ScopeCountRow> scopeCounts)
    {
        var childrenByParent = nodes
            .Where(x => x.ParentNodeId.HasValue)
            .GroupBy(x => x.ParentNodeId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray());

        var scopesByNode = scopeCounts
            .GroupBy(x => x.NodeId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ProjectModuleScopeCountDto>)group
                    .OrderBy(x => x.ModuleType)
                    .Select(x => new ProjectModuleScopeCountDto(
                        x.ModuleType,
                        x.Count))
                    .ToArray());

        ProjectHierarchyNodeDto Map(NodeRow row, string parentPath)
        {
            var path = string.IsNullOrWhiteSpace(parentPath)
                ? row.Name
                : $"{parentPath} / {row.Name}";

            var children = childrenByParent
                .GetValueOrDefault(row.Id, [])
                .Select(child => Map(child, path))
                .ToArray();

            return new ProjectHierarchyNodeDto(
                row.Id,
                row.LevelId,
                row.LevelName,
                row.LevelSortOrder,
                row.ParentNodeId,
                row.Code,
                row.Name,
                row.Description,
                row.SortOrder,
                path,
                scopesByNode.GetValueOrDefault(row.Id, []),
                children);
        }

        return nodes
            .Where(x => !x.ParentNodeId.HasValue)
            .Select(row => Map(row, string.Empty))
            .ToArray();
    }

    private static ProjectHierarchyNodeDto? FindNode(
        IEnumerable<ProjectHierarchyNodeDto> nodes,
        Guid nodeId)
    {
        foreach (var node in nodes)
        {
            if (node.Id == nodeId)
                return node;

            var child = FindNode(node.Children, nodeId);
            if (child is not null)
                return child;
        }

        return null;
    }

    private static ProjectHierarchyLevelDto ToLevelDto(
        ProjectHierarchyLevel level,
        int nodeCount)
    {
        return new ProjectHierarchyLevelDto(
            level.Id,
            level.Code,
            level.Name,
            level.SortOrder,
            level.IsRequired,
            nodeCount);
    }

    private static ProjectHierarchyNode Node(
        Guid projectId,
        ProjectHierarchyLevel level,
        ProjectHierarchyNode? parent,
        string code,
        string name,
        int sortOrder)
    {
        return new ProjectHierarchyNode
        {
            ProjectId = projectId,
            Level = level,
            ParentNode = parent,
            Code = code,
            Name = name,
            SortOrder = sortOrder
        };
    }

    private static string NormalizeCode(string value, int maxLength)
    {
        return RequireText(value, "Kod", maxLength)
            .ToUpperInvariant()
            .Replace(' ', '-');
    }

    private static string RequireText(
        string? value,
        string fieldName,
        int maxLength)
    {
        var result = value?.Trim();
        if (string.IsNullOrWhiteSpace(result))
            throw BadRequest($"{fieldName} zorunludur.");
        if (result.Length > maxLength)
            throw BadRequest(
                $"{fieldName} en fazla {maxLength} karakter olabilir.");
        return result;
    }

    private static string? TrimOptional(string? value, int maxLength)
    {
        var result = value?.Trim();
        if (string.IsNullOrWhiteSpace(result))
            return null;
        if (result.Length > maxLength)
            throw BadRequest(
                $"Açıklama en fazla {maxLength} karakter olabilir.");
        return result;
    }

    private static int EnsureSortOrder(int sortOrder)
    {
        return sortOrder >= 0
            ? sortOrder
            : throw BadRequest("Sıra değeri sıfırdan küçük olamaz.");
    }

    private static ProjectHierarchyException BadRequest(string message) =>
        new(400, message);

    private static ProjectHierarchyException NotFound(string message) =>
        new(404, message);

    private static ProjectHierarchyException Conflict(string message) =>
        new(409, message);

    private sealed record NodeRow(
        Guid Id,
        Guid LevelId,
        string LevelName,
        int LevelSortOrder,
        Guid? ParentNodeId,
        string Code,
        string Name,
        string? Description,
        int SortOrder);

    private sealed record ScopeCountRow(
        Guid NodeId,
        ProjectModuleType ModuleType,
        int Count);
}
