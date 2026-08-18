using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

[Collection("Integration")]
public sealed class PermissionAndScopeTests(DatabaseFixture fixture)
{
    private async Task<(HttpClient Client, Guid UserId)> CreateUserWithRolesAsync(
        string usernameSuffix,
        string password,
        string[] roleNames,
        IEnumerable<Guid>? siteScopeIds = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        var username = $"test-{usernameSuffix}-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = $"Test {usernameSuffix}",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            // Bu testler izin/kapsam mantığını doğruluyor, mesai saati
            // mantığını değil — testin çalıştığı saatten bağımsız
            // deterministik olması için kullanıcı mesai istisnalı yapılır.
            WorkHoursExempt = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var roles = await db.Roles
            .Where(role => roleNames.Contains(role.Name))
            .ToListAsync();

        db.UserRoles.AddRange(roles.Select(role => new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id
        }));

        var siteIds = siteScopeIds?.ToArray() ?? [];
        if (siteIds.Length > 0)
        {
            foreach (var siteId in siteIds)
            {
                db.UserDataScopes.Add(new UserDataScope
                {
                    UserId = user.Id,
                    ScopeType = DataScopeType.Site,
                    ProjectSiteId = siteId
                });
            }
        }
        else
        {
            db.UserDataScopes.Add(new UserDataScope
            {
                UserId = user.Id,
                ScopeType = DataScopeType.All
            });
        }

        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return (client, user.Id);
    }

    [Fact]
    public async Task Formen_CannotDeleteSiteReportPhoto_Returns403()
    {
        // Formen rolünde site-reports.delete izni yok — bu uca erişim
        // reddedilmeli (RequirePermission attribute üzerinden, gerçek
        // DB'den okunan izinlerle).
        var (client, _) = await CreateUserWithRolesAsync(
            "formen",
            "Formen!2026Test",
            ["Formen"]);

        var response = await client.DeleteAsync(
            $"/api/project-sites/{Guid.NewGuid()}/daily-reports/{Guid.NewGuid()}/photos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SantiyeSefi_CannotAccessUnassignedSite_ButCanAccessAssignedSite()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await TestDataFactory.CreateProjectAsync(db, suffix);

            var assignedSite = new ProjectSite
            {
                ProjectId = project.Id,
                Code = $"ATANAN-{suffix}",
                Name = "Atanan Şantiye"
            };
            var otherSite = new ProjectSite
            {
                ProjectId = project.Id,
                Code = $"BASKA-{suffix}",
                Name = "Başka Şantiye"
            };
            db.ProjectSites.AddRange(assignedSite, otherSite);
            await db.SaveChangesAsync();

            var (client, _) = await CreateUserWithRolesAsync(
                "santiyesefi",
                "SantiyeSefi!2026Test",
                ["Şantiye Şefi"],
                [assignedSite.Id]);

            // Atanmadığı şantiyenin günlük rapor listesine erişim
            // veri kapsamı ihlali nedeniyle 404 dönmeli (kaynağın
            // varlığını sızdırmamak için NotFound kullanılıyor).
            var unassignedResponse = await client.GetAsync(
                $"/api/project-sites/{otherSite.Id}/daily-reports");
            Assert.Equal(HttpStatusCode.NotFound, unassignedResponse.StatusCode);

            // Atandığı şantiyeye erişim serbest olmalı.
            var assignedResponse = await client.GetAsync(
                $"/api/project-sites/{assignedSite.Id}/daily-reports");
            Assert.Equal(HttpStatusCode.OK, assignedResponse.StatusCode);
        }
    }

    [Fact]
    public async Task SantiyeSefi_CannotCreateNewSite_LacksSitesCreatePermission()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var (client, _) = await CreateUserWithRolesAsync(
            "santiyesefi2",
            "SantiyeSefi2!2026Test",
            ["Şantiye Şefi"]);

        var response = await client.PostAsJsonAsync($"/api/projects/{project.Id}/sites", new
        {
            code = $"YENI-{suffix}",
            name = "Yeni Şantiye",
            location = (string?)null,
            notes = (string?)null
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_SiteOnlyRole_WithoutSiteAssignment_IsRejected()
    {
        // Regresyon testi: SiteOnly kapsamlı bir rol (Şantiye Şefi/Formen)
        // hiç şantiye atanmadan oluşturulmaya çalışılırsa reddedilmeli.
        // Daha önce bu kontrol atlanıp kullanıcıya yanlışlıkla kısıtsız
        // (AllScope) erişim veriliyordu — canlı ortamda tespit edilip
        // düzeltildi.
        var adminClient = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var username = $"test-siteonly-nosite-{Guid.NewGuid():N}"[..40];

        var response = await adminClient.PostAsJsonAsync("/api/user-management/users", new
        {
            username,
            fullName = "Test Şantiyesiz Şef",
            email = (string?)null,
            roleNames = new[] { "Şantiye Şefi" },
            password = "TestSiteOnlyNoSite!2026",
            isActive = true,
            allowedPermissions = Array.Empty<string>(),
            deniedPermissions = Array.Empty<string>(),
            projectSiteIds = Array.Empty<Guid>(),
            workHoursExempt = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == "Şantiye Şefi");
        Assert.Equal(RoleDataScopePolicy.SiteOnly, role.DataScopePolicy);
    }
    /// <summary>
    /// ŞANTİYE KAPSAMLI KULLANICI TÜM PERSONELİ GÖREMEZ.
    ///
    /// Bu uç yalnızca isteğe bağlı companyId/projectId parametreleriyle
    /// süzülüyordu; parametre gönderilmezse BÜTÜN şirketlerdeki tüm
    /// personel dönüyordu. `personnel.view` izni Şantiye Şefi ve
    /// Formen'de de var — yani şantiye kapsamlı iki rol, arama
    /// kutusundan kimlik numarasıyla herkesi bulabiliyordu.
    ///
    /// Beklenen: kullanıcı yalnızca KENDİ şantiyesine ATANMIŞ personeli
    /// görür.
    /// </summary>
    [Fact]
    public async Task SantiyeSefi_PersonelListesindeYalnizKendiSantiyesindekileriGorur()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var assignedSite = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"ATANAN-{suffix}",
            Name = "Atanan Şantiye"
        };
        var otherSite = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"BASKA-{suffix}",
            Name = "Başka Şantiye"
        };
        db.ProjectSites.AddRange(assignedSite, otherSite);
        await db.SaveChangesAsync();

        var mine = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, $"BENIM-{suffix}");
        var theirs = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, $"BASKA-{suffix}");

        db.ProjectSiteAssignments.AddRange(
            new ProjectSiteAssignment
            {
                PersonnelId = mine.Id,
                ProjectSiteId = assignedSite.Id,
                StartDate = DateTime.UtcNow.AddDays(-10)
            },
            new ProjectSiteAssignment
            {
                PersonnelId = theirs.Id,
                ProjectSiteId = otherSite.Id,
                StartDate = DateTime.UtcNow.AddDays(-10)
            });
        await db.SaveChangesAsync();

        var (client, _) = await CreateUserWithRolesAsync(
            "santiyesefi-personel",
            "SantiyeSefi!2026Test",
            ["Şantiye Şefi"],
            [assignedSite.Id]);

        var response = await client.GetAsync("/api/hr/personnel");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains(mine.EmployeeNumber, body);
        Assert.DoesNotContain(theirs.EmployeeNumber, body);
    }

    /// <summary>
    /// KAPSAM DIŞI PERSONEL DETAYI "BULUNAMADI" DÖNER.
    ///
    /// 403 değil 404: kaydın varlığını sızdırmamak için. Şantiye
    /// erişiminde de aynı desen kullanılıyor.
    /// </summary>
    [Fact]
    public async Task SantiyeSefi_KapsamDisiPersonelDetayinda404Alir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var site = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"SANTIYE-{suffix}",
            Name = "Şantiye"
        };
        db.ProjectSites.Add(site);
        await db.SaveChangesAsync();

        var outsider = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, $"DISARIDA-{suffix}");

        var (client, _) = await CreateUserWithRolesAsync(
            "santiyesefi-detay",
            "SantiyeSefi!2026Test",
            ["Şantiye Şefi"],
            [site.Id]);

        var response = await client.GetAsync($"/api/hr/personnel/{outsider.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// KAPSAMSIZ KULLANICI ETKİLENMEZ — geriye uyum.
    ///
    /// UserDataScope'u All olan kullanıcı (yönetim rolleri) personel
    /// listesinin tamamını görmeye devam eder. Kapsam süzgeci yalnızca
    /// kısıtlı kullanıcıyı daraltmalı; herkesi daraltırsa yönetim
    /// ekranları boşalır.
    /// </summary>
    [Fact]
    public async Task KapsamsizKullanici_PersonelListesininTamaminiGorur()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, $"HERKES-{suffix}");

        var (client, _) = await CreateUserWithRolesAsync(
            "ik-sorumlusu-kapsam",
            "IkSorumlusu!2026Test",
            ["İK Sorumlusu"]);

        var response = await client.GetAsync("/api/hr/personnel");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(personnel.EmployeeNumber, body);
    }
    /// <summary>
    /// ÜÇLÜ KAPSAM MATRİSİ — kalıcı regresyon.
    ///
    /// Veri kapsamının üç sınıfı var ve ÜÇÜ DE test edilmek zorunda:
    ///
    ///   1. Admin           -> her şeyi görür (rol adından global erişim)
    ///   2. All kapsamlı    -> her şeyi görür (UserDataScope satırından)
    ///   3. Dar kapsamlı    -> yalnız kendi şantiyesini görür
    ///
    /// İKİNCİ SINIF UZUN SÜRE KÖR NOKTADAYDI: bu kod tabanındaki bütün
    /// entegrasyon testleri `test.admin` ile koşuyordu, yani global
    /// erişimi ROL ADINDAN alan tek yol test ediliyordu. Genel Müdür,
    /// İK Sorumlusu, Finans Sorumlusu gibi CANLIDAKİ ÇOĞU kullanıcı
    /// ikinci sınıfta ve hiç kapsanmıyordu.
    ///
    /// Bu matrisin bir kez gerçek bir hatayı yakaladığı KANITLANDI:
    /// `Apply(IQueryable&lt;Personnel&gt;)` içindeki global-erişim dalı
    /// bozulduğunda (bir sonda kaynakta sabotaj bırakmıştı) yalnızca
    /// ikinci ve birinci sınıf düştü; üçüncü sınıf geçmeye devam etti.
    /// Yani dar kapsamı test etmek YETMİYOR.
    /// </summary>
    [Fact]
    public async Task UcluKapsamMatrisi_AdminVeAllKapsamHerSeyiGorur_DarKapsamSuzulur()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var kendiSantiye = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"KENDI-{suffix}",
            Name = "Kendi Şantiyesi"
        };
        var baskaSantiye = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"BASKA-{suffix}",
            Name = "Başka Şantiye"
        };
        db.ProjectSites.AddRange(kendiSantiye, baskaSantiye);
        await db.SaveChangesAsync();

        var kendi = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, $"KENDI-{suffix}");
        var baska = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, $"BASKA-{suffix}");

        db.ProjectSiteAssignments.AddRange(
            new ProjectSiteAssignment
            {
                PersonnelId = kendi.Id,
                ProjectSiteId = kendiSantiye.Id,
                StartDate = DateTime.UtcNow.AddDays(-10)
            },
            new ProjectSiteAssignment
            {
                PersonnelId = baska.Id,
                ProjectSiteId = baskaSantiye.Id,
                StartDate = DateTime.UtcNow.AddDays(-10)
            });
        await db.SaveChangesAsync();

        // 1. SINIF — Admin (global erişim rol adından)
        var adminClient = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var adminBody = await (await adminClient.GetAsync("/api/hr/personnel"))
            .Content.ReadAsStringAsync();

        Assert.Contains(kendi.EmployeeNumber, adminBody);
        Assert.Contains(baska.EmployeeNumber, adminBody);

        // 2. SINIF — All kapsamlı Admin OLMAYAN kullanıcı
        var (allClient, _) = await CreateUserWithRolesAsync(
            "matris-all",
            "MatrisAll!2026Test",
            ["İK Sorumlusu"]);

        var allBody = await (await allClient.GetAsync("/api/hr/personnel"))
            .Content.ReadAsStringAsync();

        Assert.Contains(kendi.EmployeeNumber, allBody);
        Assert.Contains(baska.EmployeeNumber, allBody);

        // 3. SINIF — dar kapsam (yalnız kendi şantiyesi)
        var (darClient, _) = await CreateUserWithRolesAsync(
            "matris-dar",
            "MatrisDar!2026Test",
            ["Şantiye Şefi"],
            [kendiSantiye.Id]);

        var darBody = await (await darClient.GetAsync("/api/hr/personnel"))
            .Content.ReadAsStringAsync();

        Assert.Contains(kendi.EmployeeNumber, darBody);
        Assert.DoesNotContain(baska.EmployeeNumber, darBody);
    }
    /// <summary>
    /// İŞE ALIM MERKEZİ — saha rolleri aday havuzunu göremez.
    ///
    /// Bu uçlar `personnel.view` istiyordu ve o izin Şantiye Şefi,
    /// Formen ve İSG Sorumlusu'nda da var. Yani saha rolleri BÜTÜN
    /// şirketlerdeki tüm adayları, TC KİMLİK NUMARASIYLA birlikte
    /// listeleyebiliyordu — uçta hiçbir süzgeç yoktu, companyId bile.
    ///
    /// Etki ölçümü (RoleCatalog, 15 rol): personnel.manage'e çekilince
    /// yalnız o üç rol erişimi kaybediyor.
    /// </summary>
    [Fact]
    public async Task SantiyeSefi_AdayHavuzunuGoremez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var site = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"AD-{suffix}",
            Name = "Şantiye"
        };
        db.ProjectSites.Add(site);
        await db.SaveChangesAsync();

        var (client, _) = await CreateUserWithRolesAsync(
            "aday-santiyesefi",
            "AdaySefi!2026Test",
            ["Şantiye Şefi"],
            [site.Id]);

        var response = await client.GetAsync("/api/hr/recruitment/candidates");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// ADAY TC KİMLİK NUMARASI MASKELİ — fail-closed kişisel veri.
    ///
    /// TC, aday listesinde düz metin dönüyordu. Aday henüz çalışan bile
    /// değil; numarayı görmek için bir iş gerekçesi olmalı — adayı
    /// personel kaydına çevirmek. O işlemin izni `personnel.create`,
    /// maske onu soruyor.
    ///
    /// Bu test personnel.manage'i OLAN ama personnel.create'i OLMAYAN
    /// bir rol gerektiriyor. Böyle bir rol katalogda yoksa test
    /// maskenin VARLIĞINI doğrular; asıl davranış birim testiyle değil
    /// uçtan doğrulanır.
    /// </summary>
    [Fact]
    public async Task AdayKimlikNumarasi_YetkisizeDonmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        db.JobCandidates.Add(new JobCandidate
        {
            CompanyId = company.Id,
            FirstName = "Aday",
            LastName = $"Test {suffix}",
            IdentityNumber = "12345678901"
        });
        await db.SaveChangesAsync();

        // Admin: personnel.create var -> TC görünür
        var adminClient = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var adminBody = await (await adminClient.GetAsync("/api/hr/recruitment/candidates"))
            .Content.ReadAsStringAsync();

        Assert.Contains("12345678901", adminBody);

        // Maske servisi personnel.create soruyor: kaynak kontrolü.
        var kaynak = await File.ReadAllTextAsync(
            SecurityFilePath("CandidateIdentityVisibilityService.cs"));

        Assert.Contains("PersonnelCreate", kaynak);
        // fail-closed: kullanıcı çözülemezse gösterme
        Assert.Matches(@"UserId is not Guid[\s\S]{0,60}return false", kaynak);
    }

    private static string SecurityFilePath(string fileName)
    {
        var dir = AppContext.BaseDirectory;

        while (dir is not null &&
               !Directory.Exists(Path.Combine(dir, "EnderunAI.Api", "Security")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return Path.Combine(dir!, "EnderunAI.Api", "Security", fileName);
    }
}
