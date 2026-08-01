using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EnderunAI.Api.Security;

public sealed record UserAuthorizationSnapshot(
    Guid UserId,
    bool IsActive,
    string SecurityStamp,
    IReadOnlyCollection<string> RoleNames,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<UserDataScopeGrant> DataScopes);

public sealed record UserDataScopeGrant(
    int ScopeType,
    Guid? CompanyId,
    Guid? BranchId,
    Guid? ProjectId);

public interface IUserAuthorizationService
{
    Task<UserAuthorizationSnapshot?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed class UserAuthorizationService(
    AppDbContext db) : IUserAuthorizationService
{
    private const int AllScope = 0;

    public async Task<UserAuthorizationSnapshot?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .Where(item => item.Id == userId)
            .Select(item => new
            {
                item.Id,
                item.IsActive,
                RoleNames = item.UserRoles
                    .Select(link => link.Role.Name)
                    .ToArray()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
            return null;

        var roleNames = user.RoleNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item)
            .ToArray();
        var schema = await DetectAuthorizationSchemaAsync(
            db,
            cancellationToken);

        IReadOnlyCollection<string> permissions;
        IReadOnlyCollection<UserDataScopeGrant> dataScopes;

        if (schema.IsComplete)
        {
            permissions = await LoadPermissionKeysAsync(
                db,
                userId,
                cancellationToken);
            dataScopes = await LoadDataScopesAsync(
                db,
                userId,
                cancellationToken);
        }
        else if (schema.IsLegacy)
        {
            permissions = PermissionCatalog
                .Resolve(roleNames)
                .OrderBy(item => item)
                .ToArray();

            // Legacy production has no persisted company/project scope grants.
            // Preserve its previous visibility only for a recognized role that
            // resolves to at least one permission. Permission middleware still
            // enforces the resolved module permissions.
            dataScopes = permissions.Count == 0
                ? []
                : [new UserDataScopeGrant(AllScope, null, null, null)];
        }
        else
        {
            throw new InvalidOperationException(
                "Yetkilendirme şeması kısmi durumda. permissions, " +
                "role_permissions, user_permission_overrides ve " +
                "user_data_scopes tablolarının tümü birlikte bulunmalıdır.");
        }

        return new UserAuthorizationSnapshot(
            user.Id,
            user.IsActive,
            string.Empty,
            roleNames,
            permissions,
            dataScopes);
    }

    private static async Task<AuthorizationSchemaState>
        DetectAuthorizationSchemaAsync(
            AppDbContext db,
            CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    to_regclass('public.permissions') IS NOT NULL,
                    to_regclass('public.role_permissions') IS NOT NULL,
                    to_regclass('public.user_permission_overrides') IS NOT NULL,
                    to_regclass('public.user_data_scopes') IS NOT NULL;
                """;

            await using var reader = await command.ExecuteReaderAsync(
                cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "Yetkilendirme şeması denetlenemedi.");
            }

            return new AuthorizationSchemaState(
                reader.GetBoolean(0),
                reader.GetBoolean(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3));
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }

    private static async Task<IReadOnlyCollection<UserDataScopeGrant>>
        LoadDataScopesAsync(
            AppDbContext db,
            Guid userId,
            CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    "ScopeType",
                    "CompanyId",
                    "BranchId",
                    "ProjectId"
                FROM user_data_scopes
                WHERE "UserId" = @userId
                  AND "IsActive" = TRUE
                  AND "IsDeleted" = FALSE
                ORDER BY "ScopeType", "CompanyId", "BranchId", "ProjectId";
                """;

            AddUserIdParameter(command, userId);

            var dataScopes = new List<UserDataScopeGrant>();

            await using var reader = await command.ExecuteReaderAsync(
                cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                dataScopes.Add(new UserDataScopeGrant(
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3)));
            }

            return dataScopes;
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }

    private static async Task<IReadOnlyCollection<string>>
        LoadPermissionKeysAsync(
            AppDbContext db,
            Guid userId,
            CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    p."Key",
                    CASE
                        WHEN upo."Effect" = 2 THEN FALSE
                        ELSE TRUE
                    END AS "IsAllowed"
                FROM permissions AS p
                LEFT JOIN user_permission_overrides AS upo
                    ON upo."PermissionId" = p."Id"
                   AND upo."UserId" = @userId
                WHERE upo."PermissionId" IS NOT NULL
                   OR EXISTS (
                        SELECT 1
                        FROM user_roles AS ur
                        INNER JOIN role_permissions AS rp
                            ON rp."RoleId" = ur."RoleId"
                        WHERE ur."UserId" = @userId
                          AND rp."PermissionId" = p."Id"
                   )
                ORDER BY p."Key";
                """;

            AddUserIdParameter(command, userId);

            var permissions = new List<string>();

            await using var reader = await command.ExecuteReaderAsync(
                cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetBoolean(1))
                    permissions.Add(reader.GetString(0));
            }

            return permissions;
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }

    private static void AddUserIdParameter(
        IDbCommand command,
        Guid userId)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@userId";
        parameter.DbType = DbType.Guid;
        parameter.Value = userId;
        command.Parameters.Add(parameter);
    }

    private sealed record AuthorizationSchemaState(
        bool HasPermissions,
        bool HasRolePermissions,
        bool HasUserPermissionOverrides,
        bool HasUserDataScopes)
    {
        public bool IsComplete =>
            HasPermissions &&
            HasRolePermissions &&
            HasUserPermissionOverrides &&
            HasUserDataScopes;

        public bool IsLegacy =>
            !HasPermissions &&
            !HasRolePermissions &&
            !HasUserPermissionOverrides &&
            !HasUserDataScopes;
    }
}
