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
        await SeedRoleWorkHourWindowsAsync(db);
        await SeedCompanyFinanceSettingsAsync(db);
        await SeedCompanyPayrollSettingsAsync(db);
    }

    /// <summary>
    /// Bordro parametrelerinin başlangıç değerleri. DİKKAT: bu değerler
    /// yalnızca ilk kurulum kolaylığı içindir, resmi kaynak değildir.
    /// Kayıt <c>VerifiedAtUtc = null</c> olarak oluşur ve bu haliyle
    /// bordronun kesinleştirilmesini engeller; yetkili kullanıcı Şirket
    /// Ayarları → Bordro Parametreleri ekranında yürürlükteki SGK/GİB
    /// tebliğiyle karşılaştırıp onaylamak zorundadır.
    ///
    /// Oranlar (SGK %14/%1/%20,75/%2, damga ‰7,59, dilim oranları
    /// %15/%20/%27/%35/%40) yıllar içinde değişmeyen yasal oranlardır.
    /// Tutarlar (asgari ücret, SGK tavanı, dilim sınırları) her yıl
    /// değişir — doğrulanması gereken asıl alanlar bunlardır.
    /// </summary>
    private static async Task SeedCompanyPayrollSettingsAsync(AppDbContext db)
    {
        const int year = 2026;

        // Brüt asgari ücret; net = brüt × 0,85 (asgari ücrette gelir ve
        // damga vergisi istisnası tam uygulandığı için yalnızca %14 SGK
        // ve %1 işsizlik primi kesilir).
        const decimal minimumWageGross = 33_030.00m;
        const decimal minimumWageNet = 28_075.50m;

        var companyIds = await db.Companies.Select(company => company.Id).ToListAsync();

        var existing = await db.CompanyPayrollSettings
            .Where(x => x.Year == year)
            .Select(x => x.CompanyId)
            .ToListAsync();

        var missing = companyIds.Except(existing).ToList();
        if (missing.Count == 0)
            return;

        foreach (var companyId in missing)
        {
            var settings = new CompanyPayrollSettings
            {
                CompanyId = companyId,
                Year = year,
                MinimumWageGross = minimumWageGross,
                MinimumWageNet = minimumWageNet,
                SgkBaseFloor = minimumWageGross,
                // SGK tavanı tabanın 7,5 katıdır.
                SgkBaseCeiling = minimumWageGross * 7.5m,
                SgkEmployeeRate = 14m,
                UnemploymentEmployeeRate = 1m,
                SgkEmployerRate = 20.75m,
                UnemploymentEmployerRate = 2m,
                SgkEmployerDiscountEnabled = false,
                SgkEmployerDiscountPoints = 5m,
                StampTaxPerMille = 7.59m,
                MinimumWageIncomeTaxExemptionEnabled = true,
                MinimumWageStampTaxExemptionEnabled = true,
                VerifiedAtUtc = null
            };

            settings.TaxBrackets = new List<PayrollTaxBracket>
            {
                new() { Order = 1, LowerBound = 0m, UpperBound = 200_000m, Rate = 15m },
                new() { Order = 2, LowerBound = 200_000m, UpperBound = 420_000m, Rate = 20m },
                new() { Order = 3, LowerBound = 420_000m, UpperBound = 1_000_000m, Rate = 27m },
                new() { Order = 4, LowerBound = 1_000_000m, UpperBound = 5_400_000m, Rate = 35m },
                new() { Order = 5, LowerBound = 5_400_000m, UpperBound = null, Rate = 40m }
            };

            db.CompanyPayrollSettings.Add(settings);
        }

        await db.SaveChangesAsync();
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
        var existingRoles = await db.Roles.ToListAsync();
        var existingByName = existingRoles
            .ToDictionary(role => role.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var definition in RoleCatalog.Roles)
        {
            if (existingByName.TryGetValue(definition.Name, out var role))
            {
                // Description hiçbir ekrandan (Yetki Matrisi/Kullanıcı
                // Yönetimi) düzenlenemiyor — bu yüzden kod tanımıyla
                // senkron tutmak güvenli, add-only olduğu için daha önce
                // 5 rolde (Genel Müdür, Formen, Şantiye Şefi, Teknik
                // Koordinatör, Sekreterya) eski/tutarsız açıklamalar
                // kalmıştı. DataScopePolicy'ye kasıtlı dokunulmuyor —
                // admin Yetki Matrisi'nden bilinçli değiştirebiliyor
                // (PermissionMatrixController.UpdateScopePolicy), bu
                // seçimi geri almamak için add-only kalıyor.
                if (role.Description != definition.Description)
                    role.Description = definition.Description;

                continue;
            }

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

    /// <summary>
    /// Ofis rolleri hafta içi 09:00-18:00, Cumartesi 09:00-14:00, Pazar
    /// kapalı. Şantiye Şefi/Formen her gün 07:00-22:00. Admin ve Genel
    /// Müdür için hiç satır eklenmez (WorkHourAccessService içinde her
    /// zaman istisnasız izinlidir). Rol bazında en az bir satır varsa o rol
    /// atlanır — admin daha sonra Şirket Ayarları'ndan pencereyi
    /// değiştirdiyse bu adım üzerine yazmaz.
    /// </summary>
    private static readonly string[] OfficeWorkHourRoles =
    [
        "Finans Sorumlusu", "Satın Alma Sorumlusu", "İK Sorumlusu", "Ön Muhasebe",
        "Teknik Ofis", "Teknik Koordinatör", "Sekreterya", "Araç Sorumlusu", "Depo Sorumlusu"
    ];

    private static readonly string[] SiteWorkHourRoles = ["Şantiye Şefi", "Formen"];

    private static async Task SeedRoleWorkHourWindowsAsync(AppDbContext db)
    {
        var roleIdsByName = await db.Roles
            .ToDictionaryAsync(role => role.Name, role => role.Id, StringComparer.OrdinalIgnoreCase);
        var rolesWithWindows = (await db.RoleWorkHourWindows
                .Select(item => item.RoleId)
                .ToListAsync())
            .ToHashSet();

        foreach (var roleName in OfficeWorkHourRoles)
        {
            if (!roleIdsByName.TryGetValue(roleName, out var roleId) || rolesWithWindows.Contains(roleId))
                continue;

            for (var day = 1; day <= 5; day++)
            {
                db.RoleWorkHourWindows.Add(new RoleWorkHourWindow
                {
                    RoleId = roleId,
                    DayOfWeek = day,
                    StartTime = new TimeOnly(9, 0),
                    EndTime = new TimeOnly(18, 0)
                });
            }

            db.RoleWorkHourWindows.Add(new RoleWorkHourWindow
            {
                RoleId = roleId,
                DayOfWeek = 6,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(14, 0)
            });
        }

        foreach (var roleName in SiteWorkHourRoles)
        {
            if (!roleIdsByName.TryGetValue(roleName, out var roleId) || rolesWithWindows.Contains(roleId))
                continue;

            for (var day = 0; day <= 6; day++)
            {
                db.RoleWorkHourWindows.Add(new RoleWorkHourWindow
                {
                    RoleId = roleId,
                    DayOfWeek = day,
                    StartTime = new TimeOnly(7, 0),
                    EndTime = new TimeOnly(22, 0)
                });
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Her şirket için tek satırlık finans/muhasebe entegrasyon ayarı
    /// oluşturur ve varsayılan hesapları hesap planındaki kodlardan
    /// (191/391/600/740/320/120/780) eşler. Satır zaten varsa hiç
    /// dokunulmaz — admin Şirket Ayarları'ndan seçtiği hesapları
    /// kaybetmez: yalnızca null (hiç seçilmemiş) alanlar doldurulur.
    /// Hesap planı henüz yüklenmemişse eşleşmeyen alanlar null kalır ve
    /// sonraki boot'ta yeniden denenir; admin ekrandan da seçebilir.
    /// </summary>
    private static async Task SeedCompanyFinanceSettingsAsync(AppDbContext db)
    {
        var companyIds = await db.Companies.Select(company => company.Id).ToListAsync();
        var existingByCompany = await db.CompanyFinanceSettings
            .ToDictionaryAsync(settings => settings.CompanyId);

        var changed = false;

        foreach (var companyId in companyIds)
        {
            if (!existingByCompany.TryGetValue(companyId, out var settings))
            {
                settings = new CompanyFinanceSettings { CompanyId = companyId };
                db.CompanyFinanceSettings.Add(settings);
                changed = true;
            }

            // Yalnızca HİÇ doldurulmamış (null) alanlar tamamlanır —
            // admin'in Şirket Ayarları'ndan seçtiği hesap asla ezilmez.
            // Bu sayede sonradan eklenen yeni bir ayar alanı (ör. Faz B'de
            // gelen kesinti hesabı) mevcut şirketlerde de otomatik dolar;
            // saf add-only olsaydı boş kalır ve akış hata verirdi.
            changed |= await FillIfMissingAsync(db, companyId, settings,
                s => s.VatInAccountId, (s, v) => s.VatInAccountId = v, "191.01.03", "191");
            changed |= await FillIfMissingAsync(db, companyId, settings,
                s => s.VatOutAccountId, (s, v) => s.VatOutAccountId = v, "391.09", "391");
            changed |= await FillIfMissingAsync(db, companyId, settings,
                s => s.SalesAccountId, (s, v) => s.SalesAccountId = v, "600.03", "600");
            changed |= await FillIfMissingAsync(db, companyId, settings,
                s => s.ExpenseAccountId, (s, v) => s.ExpenseAccountId = v, "740");
            changed |= await FillIfMissingAsync(db, companyId, settings,
                s => s.PayablesAccountId, (s, v) => s.PayablesAccountId = v, "320");
            changed |= await FillIfMissingAsync(db, companyId, settings,
                s => s.ReceivablesAccountId, (s, v) => s.ReceivablesAccountId = v, "120");
            changed |= await FillIfMissingAsync(db, companyId, settings,
                s => s.FactoringExpenseAccountId, (s, v) => s.FactoringExpenseAccountId = v, "780.01.01", "780");
            changed |= await FillIfMissingAsync(db, companyId, settings,
                s => s.DeductionAccountId, (s, v) => s.DeductionAccountId = v, "126");
        }

        if (changed)
            await db.SaveChangesAsync();
    }

    private static async Task<bool> FillIfMissingAsync(
        AppDbContext db,
        Guid companyId,
        CompanyFinanceSettings settings,
        Func<CompanyFinanceSettings, Guid?> read,
        Action<CompanyFinanceSettings, Guid?> write,
        params string[] codeCandidates)
    {
        if (read(settings) is not null)
            return false;

        var resolved = await ResolveAccountAsync(db, companyId, codeCandidates);
        if (resolved is null)
            return false;

        write(settings, resolved);
        return true;
    }

    private static async Task<Guid?> ResolveAccountAsync(
        AppDbContext db,
        Guid companyId,
        params string[] codeCandidates)
    {
        foreach (var code in codeCandidates)
        {
            var id = await db.AccountingAccounts
                .Where(x =>
                    x.CompanyId == companyId &&
                    x.Code == code &&
                    x.IsActive &&
                    x.IsPostingAllowed)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync();

            if (id is not null)
                return id;
        }

        return null;
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
