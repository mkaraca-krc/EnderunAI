using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

public static class DatabaseSeeder
{
    /// <summary>
    /// Canlıda eski ALLOW:/DENY: sahte-rol hack'iyle taşınan kullanıcı bazlı
    /// izin genişletmeleri, yeni RBAC şemasına geçerken gerçek
    /// UserPermissionOverride satırlarına çevrilir. Kullanıcı adı → yeni
    /// rol(ler) eşlemesi, yetki paketi planında onaylanan tabloyla birebir
    /// aynıdır.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> CutoverRoleAssignments =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["mehmet"] = ["Admin"],
            ["asakcak"] = ["Sekreterya"],
            ["cboran"] = ["Teknik Ofis"],
            ["dyildirici"] = ["Finans Sorumlusu", "İK Sorumlusu"],
            ["hkutlu"] = ["Teknik Koordinatör"],
            ["ioktem"] = ["Teknik Ofis"],
            ["iyavuzkanat"] = ["Teknik Ofis"],
            ["oturkmen"] = ["Finans Sorumlusu", "Satın Alma Sorumlusu", "İK Sorumlusu"],
            ["ralici"] = ["Teknik Koordinatör"],
            ["smemis"] = ["Teknik Ofis"],
            ["vtepe"] = ["Depo Sorumlusu"]
        };

    /// <summary>
    /// dyildirici bugün ALLOW:system.users.manage override'ına sahip
    /// (kullanıcı yönetebiliyor); yeni 11 rolde bu yalnızca Admin/Genel
    /// Müdür'de var. Erişimini kaybetmemesi için Kullanıcı Yönetimi
    /// izinleri ayrıca override olarak korunuyor.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> CutoverExtraAllowOverrides =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["dyildirici"] =
            [
                PermissionCatalog.Keys.SystemUsersManage,
                PermissionCatalog.Keys.UserManagementView,
                PermissionCatalog.Keys.UserManagementCreate,
                PermissionCatalog.Keys.UserManagementEdit,
                PermissionCatalog.Keys.UserManagementDelete
            ]
        };

    public static async Task SeedAsync(
        AppDbContext db,
        PasswordService passwordService,
        IConfiguration configuration)
    {
        await SeedPermissionsAsync(db);
        await SeedRolesAsync(db);
        await SeedRolePermissionsAsync(db);
        await SeedAdminUserAsync(db, passwordService, configuration);
        await RunLegacyRoleCutoverAsync(db);
        await SeedDefaultDataScopesAsync(db);
        await SeedCompanyDefaultsAsync(db);
    }

    /// <summary>
    /// Şirketin gerçek kurumsal bilgileri — sadece hâlâ eski placeholder
    /// vergi no'suna ("0000000000") sahipse bir kereliğine doldurulur;
    /// admin Şirket Ayarları ekranından değiştirdikten sonra bu koşul
    /// artık sağlanmayacağı için tekrar üzerine yazılmaz.
    /// </summary>
    private const string PlaceholderTaxNumber = "0000000000";

    private static async Task SeedCompanyDefaultsAsync(AppDbContext db)
    {
        var company = await db.Companies
            .Include(x => x.BankAccounts)
            .OrderBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (company is null)
            return;

        if (string.IsNullOrWhiteSpace(company.TaxNumber) ||
            company.TaxNumber == PlaceholderTaxNumber)
        {
            company.Name = "Enderun Elektrik Üretim Enerji A.Ş.";
            company.TaxOffice = "İvedik";
            company.TaxNumber = "3341211200";
            company.Phone = "0312 241 72 59";
            company.Email = "bilgi@enderunenerji.com.tr";
            company.Address =
                "İvedik OSB, 1122. Cd. Maxivedik Ticaret Merkezi No:20/81, 06810 Yenimahalle/Ankara";
            company.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync();
        }

        if (!company.BankAccounts.Any())
        {
            db.CompanyBankAccounts.Add(new CompanyBankAccount
            {
                CompanyId = company.Id,
                BankName = "Garanti BBVA",
                Iban = "TR170006200018100006282394",
                AccountHolder = company.Name,
                CurrencyCode = "TRY"
            });

            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedPermissionsAsync(AppDbContext db)
    {
        var existingKeys = await db.Permissions
            .Select(item => item.Key)
            .ToListAsync();
        var existingKeySet = existingKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in PermissionCatalog.Permissions)
        {
            if (existingKeySet.Contains(definition.Key))
                continue;

            db.Permissions.Add(new Permission
            {
                Key = definition.Key,
                Module = definition.Module,
                Name = definition.Name,
                Description = definition.Description
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedRolesAsync(AppDbContext db)
    {
        var existingNames = (await db.Roles
                .Select(item => item.Name)
                .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in RoleCatalog.Roles)
        {
            if (existingNames.Contains(definition.Name))
                continue;

            db.Roles.Add(new AppRole
            {
                Name = definition.Name,
                Description = definition.Description,
                DataScopePolicy = definition.DataScopePolicy
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedRolePermissionsAsync(AppDbContext db)
    {
        var roleIdsByName = await db.Roles
            .ToDictionaryAsync(role => role.Name, role => role.Id, StringComparer.OrdinalIgnoreCase);
        var permissionIdsByKey = await db.Permissions
            .ToDictionaryAsync(permission => permission.Key, permission => permission.Id, StringComparer.OrdinalIgnoreCase);

        var existingGrants = (await db.RolePermissions
                .Select(item => new { item.RoleId, item.PermissionId })
                .ToListAsync())
            .Select(item => (item.RoleId, item.PermissionId))
            .ToHashSet();

        foreach (var definition in RoleCatalog.Roles)
        {
            if (!roleIdsByName.TryGetValue(definition.Name, out var roleId))
                continue;

            foreach (var key in definition.PermissionKeys.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!permissionIdsByKey.TryGetValue(key, out var permissionId))
                    continue;

                if (existingGrants.Contains((roleId, permissionId)))
                    continue;

                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleId,
                    PermissionId = permissionId
                });
                existingGrants.Add((roleId, permissionId));
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedAdminUserAsync(
        AppDbContext db,
        PasswordService passwordService,
        IConfiguration configuration)
    {
        var adminRole = await db.Roles.SingleAsync(role => role.Name == "Admin");

        var username = Environment.GetEnvironmentVariable("SEED_ADMIN_USERNAME");
        var password = Environment.GetEnvironmentVariable("SEED_ADMIN_PASSWORD");
        var fullName =
            Environment.GetEnvironmentVariable("SEED_ADMIN_FULLNAME") ??
            "Mehmet Karacabey";

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        username = username.Trim().ToLowerInvariant();
        var user = await db.Users
            .Include(item => item.UserRoles)
            .SingleOrDefaultAsync(item => item.Username == username);

        if (user is null)
        {
            var result = passwordService.Hash(password);
            user = new AppUser
            {
                Username = username,
                FullName = fullName,
                PasswordHash = result.Hash,
                PasswordSalt = result.Salt,
                IsActive = true
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        if (!user.UserRoles.Any(userRole => userRole.RoleId == adminRole.Id))
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = adminRole.Id
            });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Bire bir tetikleyici: eski ALLOW:/DENY: sahte-rol satırları hâlâ
    /// varsa (ilk RBAC migration'ından sonraki ilk boot), canlı
    /// kullanıcıları yeni rollere taşır, eski hack'i ve artık kullanılmayan
    /// preset rolleri temizler. Sonraki her boot'ta bu satırlar
    /// bulunamayacağı için no-op olur — admin daha sonra rol atamalarını
    /// Kullanıcı Yönetimi ekranından değiştirebilir, bu adım onu geri
    /// almaz.
    /// </summary>
    private static async Task RunLegacyRoleCutoverAsync(AppDbContext db)
    {
        var legacyOverrideRoles = await db.Roles
            .Where(role =>
                role.Name.StartsWith(LegacyAllowPrefix) ||
                role.Name.StartsWith(LegacyDenyPrefix))
            .ToListAsync();

        if (legacyOverrideRoles.Count == 0)
            return;

        var roleIdByName = await db.Roles
            .ToDictionaryAsync(role => role.Name, role => role.Id, StringComparer.OrdinalIgnoreCase);
        var permissionIdsByKey = await db.Permissions
            .ToDictionaryAsync(permission => permission.Key, permission => permission.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var (username, newRoleNames) in CutoverRoleAssignments)
        {
            var user = await db.Users
                .Include(item => item.UserRoles)
                .SingleOrDefaultAsync(item => item.Username == username);

            if (user is null)
                continue;

            db.UserRoles.RemoveRange(user.UserRoles);

            foreach (var roleName in newRoleNames)
            {
                if (!roleIdByName.TryGetValue(roleName, out var roleId))
                    continue;

                db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
            }

            if (CutoverExtraAllowOverrides.TryGetValue(username, out var extraKeys))
            {
                foreach (var key in extraKeys)
                {
                    if (!permissionIdsByKey.TryGetValue(key, out var permissionId))
                        continue;

                    var alreadyExists = await db.UserPermissionOverrides.AnyAsync(
                        item => item.UserId == user.Id && item.PermissionId == permissionId);

                    if (alreadyExists)
                        continue;

                    db.UserPermissionOverrides.Add(new UserPermissionOverride
                    {
                        UserId = user.Id,
                        PermissionId = permissionId,
                        Effect = PermissionOverrideEffect.Allow
                    });
                }
            }
        }

        await db.SaveChangesAsync();

        // Eski ALLOW:/DENY: sahte-rol satırlarını temizle.
        var legacyRoleIds = legacyOverrideRoles.Select(role => role.Id).ToArray();
        await db.UserRoles
            .Where(userRole => legacyRoleIds.Contains(userRole.RoleId))
            .ExecuteDeleteAsync();
        db.Roles.RemoveRange(legacyOverrideRoles);

        // Yeni 11'li listede olmayan ve canlıda kullanıcısı kalmayan
        // preset rolleri temizle.
        var retiredRoles = await db.Roles
            .Where(role => RoleCatalog.RetiredRoleNames.Contains(role.Name))
            .ToListAsync();

        foreach (var role in retiredRoles)
        {
            var hasUsers = await db.UserRoles.AnyAsync(userRole => userRole.RoleId == role.Id);
            if (hasUsers)
                continue;

            db.RolePermissions.RemoveRange(
                db.RolePermissions.Where(item => item.RoleId == role.Id));
            db.Roles.Remove(role);
        }

        await db.SaveChangesAsync();
    }

    private const string LegacyAllowPrefix = "ALLOW:";
    private const string LegacyDenyPrefix = "DENY:";

    /// <summary>
    /// Hiç veri kapsamı satırı olmayan aktif kullanıcılara AllScope
    /// (kısıtsız) varsayılanı verir — hem taze kurulan Admin kullanıcısı
    /// hem de cutover'dan geçen mevcut 11 kullanıcı için geçerlidir.
    /// Şantiye bazlı kısıtlama, admin ileride Kullanıcı Yönetimi
    /// ekranından bilinçli olarak Site scope ataması yaptığında devreye
    /// girer.
    /// </summary>
    private static async Task SeedDefaultDataScopesAsync(AppDbContext db)
    {
        var usersWithoutScope = await db.Users
            .Where(user =>
                user.IsActive &&
                !db.UserDataScopes.Any(scope => scope.UserId == user.Id))
            .Select(user => user.Id)
            .ToListAsync();

        foreach (var userId in usersWithoutScope)
        {
            db.UserDataScopes.Add(new UserDataScope
            {
                UserId = userId,
                ScopeType = DataScopeType.All
            });
        }

        if (usersWithoutScope.Count > 0)
            await db.SaveChangesAsync();
    }
}
