using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Proje silme güvenliği.
///
/// Buradaki asıl güvence, silmenin geri alınamaz olması: kesinleşmiş bir
/// muhasebe/hakediş kaydı olan projenin kalıcı silinmesi defteri sessizce
/// bozar. O yüzden testler yalnızca "silinebiliyor mu" değil, "silinmemesi
/// gerekende gerçekten durduruluyor mu" sorusunu doğruluyor.
/// </summary>
[Collection("Integration")]
public sealed class ProjectDeletionTests(DatabaseFixture fixture)
{
    private async Task<HttpClient> CreateClientForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "ProjectDelete!2026";
        var username = $"test-del-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = $"Test {roleName}",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var role = await db.Roles.SingleAsync(x => x.Name == roleName);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        db.UserDataScopes.Add(new UserDataScope
        {
            UserId = user.Id,
            ScopeType = DataScopeType.All
        });
        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static async Task<HttpResponseMessage> DeleteAsync(
        HttpClient client, Guid projectId, string confirmationCode)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/projects/{projectId}")
        {
            Content = JsonContent.Create(new { confirmationCode })
        };

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task DeletionImpact_TeknikOfis_Forbidden()
    {
        // projects.delete artık yalnızca Genel Müdür ve Admin'de.
        var client = await CreateClientForRoleAsync("Teknik Ofis");

        Project project;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            project = await TestDataFactory.CreateProjectAsync(
                db, Guid.NewGuid().ToString("N")[..8]);
        }

        var impact = await client.GetAsync($"/api/projects/{project.Id}/deletion-impact");
        Assert.Equal(HttpStatusCode.Forbidden, impact.StatusCode);

        var delete = await DeleteAsync(client, project.Id, project.Code);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task Delete_EmptyProject_RemovedWithAuditTrail()
    {
        var client = await CreateClientForRoleAsync("Genel Müdür");

        Project project;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            project = await TestDataFactory.CreateProjectAsync(
                db, Guid.NewGuid().ToString("N")[..8]);
        }

        var impactResponse = await client.GetAsync($"/api/projects/{project.Id}/deletion-impact");
        impactResponse.EnsureSuccessStatusCode();

        var impact = await impactResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(impact.GetProperty("canHardDelete").GetBoolean());
        Assert.Empty(impact.GetProperty("blockers").EnumerateArray());

        var response = await DeleteAsync(client, project.Id, project.Code);
        response.EnsureSuccessStatusCode();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var exists = await db.Projects
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Id == project.Id);
            Assert.False(exists);

            // Denetim kaydı: kim, ne zaman, hangi proje.
            var audit = await db.SecurityAuditEvents
                .SingleAsync(x => x.EntityId == project.Id && x.Action == "Project.HardDelete");

            Assert.NotNull(audit.ActorUserId);
            Assert.Contains(project.Code, audit.DetailsJson);
        }
    }

    [Fact]
    public async Task Delete_WrongConfirmationCode_Rejected()
    {
        var client = await CreateClientForRoleAsync("Genel Müdür");

        Project project;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            project = await TestDataFactory.CreateProjectAsync(
                db, Guid.NewGuid().ToString("N")[..8]);
        }

        var response = await DeleteAsync(client, project.Id, "YANLIS-KOD");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.Projects.AnyAsync(x => x.Id == project.Id));
        }
    }

    [Fact]
    public async Task Delete_ProjectWithPostedVoucher_BlockedAndArchivedInstead()
    {
        var client = await CreateClientForRoleAsync("Genel Müdür");

        Project project;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            project = await TestDataFactory.CreateProjectAsync(
                db, Guid.NewGuid().ToString("N")[..8]);

            var account = new AccountingAccount
            {
                CompanyId = project.CompanyId,
                Code = "740",
                Name = "Hizmet Üretim Maliyeti",
                Nature = AccountingAccountNature.Debit,
                Level = 3,
                IsPostingAllowed = true
            };
            db.AccountingAccounts.Add(account);

            var voucher = new AccountingVoucher
            {
                CompanyId = project.CompanyId,
                VoucherNumber = $"TST-{Guid.NewGuid():N}"[..20],
                VoucherDate = DateTime.UtcNow,
                Status = AccountingVoucherStatus.Posted,
                Description = "Kesinleşmiş fiş"
            };
            db.AccountingVouchers.Add(voucher);
            await db.SaveChangesAsync();

            db.AccountingVoucherLines.AddRange(
                new AccountingVoucherLine
                {
                    AccountingVoucherId = voucher.Id,
                    AccountingAccountId = account.Id,
                    ProjectId = project.Id,
                    LineNumber = 1,
                    DebitAmount = 1000,
                    DebitAmountLocal = 1000
                },
                new AccountingVoucherLine
                {
                    AccountingVoucherId = voucher.Id,
                    AccountingAccountId = account.Id,
                    ProjectId = project.Id,
                    LineNumber = 2,
                    CreditAmount = 1000,
                    CreditAmountLocal = 1000
                });

            await db.SaveChangesAsync();
        }

        var impactResponse = await client.GetAsync($"/api/projects/{project.Id}/deletion-impact");
        impactResponse.EnsureSuccessStatusCode();

        var impact = await impactResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(impact.GetProperty("canHardDelete").GetBoolean());

        var blockers = impact.GetProperty("blockers").EnumerateArray().ToList();
        Assert.Contains(blockers, x => x.GetProperty("key").GetString() == "postedVoucherLines");

        // Doğru kod yazılsa bile kalıcı silme reddedilir.
        var delete = await DeleteAsync(client, project.Id, project.Code);
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.Projects.AnyAsync(x => x.Id == project.Id));
        }

        // Tek güvenli yol arşiv: veri durur, aktif listeden düşer.
        var archive = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/archive", new { reason = "Test projesi" });
        archive.EnsureSuccessStatusCode();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Projects.SingleAsync(x => x.Id == project.Id);

            Assert.True(stored.IsArchived);
            Assert.NotNull(stored.ArchivedAtUtc);
            Assert.Equal(ProjectStatus.Cancelled, stored.Status);

            // Fiş satırı yerinde duruyor — arşiv mali kaydı silmez.
            Assert.Equal(
                2,
                await db.AccountingVoucherLines.CountAsync(x => x.ProjectId == project.Id));

            Assert.True(await db.SecurityAuditEvents
                .AnyAsync(x => x.EntityId == project.Id && x.Action == "Project.Archive"));
        }

        // Arşivli proje aktif listeden düşer, includeArchived ile geri gelir.
        var activeList = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects?companyId={project.CompanyId}");
        Assert.DoesNotContain(
            activeList.EnumerateArray(),
            x => x.GetProperty("id").GetGuid() == project.Id);

        var fullList = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects?companyId={project.CompanyId}&includeArchived=true");
        Assert.Contains(
            fullList.EnumerateArray(),
            x => x.GetProperty("id").GetGuid() == project.Id);
    }

    [Fact]
    public async Task Delete_ProjectWithCheque_Blocked()
    {
        // Çek her durumda engelleyici: çek defteri kaydı projeyle silinemez.
        var client = await CreateClientForRoleAsync("Genel Müdür");

        Project project;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            project = await TestDataFactory.CreateProjectAsync(
                db, Guid.NewGuid().ToString("N")[..8]);

            db.Cheques.Add(new Cheque
            {
                CompanyId = project.CompanyId,
                ProjectId = project.Id,
                Direction = ChequeDirection.Issued,
                ChequeNumber = $"T{Guid.NewGuid():N}"[..12],
                BankName = "Test Bank",
                Amount = 1000,
                CurrencyCode = "TRY",
                IssueDate = DateTime.UtcNow,
                DueDate = DateTime.UtcNow.AddMonths(1),
                Status = ChequeStatus.Issued
            });

            await db.SaveChangesAsync();
        }

        var impact = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{project.Id}/deletion-impact");

        Assert.False(impact.GetProperty("canHardDelete").GetBoolean());
        Assert.Contains(
            impact.GetProperty("blockers").EnumerateArray(),
            x => x.GetProperty("key").GetString() == "cheques");

        var delete = await DeleteAsync(client, project.Id, project.Code);
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
    }

    [Fact]
    public async Task Delete_ProjectWithDraftRecords_RemovesDependentsToo()
    {
        // Taslak keşif, şantiye ve depo kesinleşmiş sayılmaz: projeyle
        // birlikte gitmeli, arkada yetim kayıt bırakmamalı.
        var client = await CreateClientForRoleAsync("Genel Müdür");

        Project project;
        Guid siteId;
        Guid warehouseId;
        Guid boqId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            project = await TestDataFactory.CreateProjectAsync(
                db, Guid.NewGuid().ToString("N")[..8]);

            var site = new ProjectSite
            {
                ProjectId = project.Id,
                Code = $"SNT-{Guid.NewGuid():N}"[..12],
                Name = "Test Şantiye"
            };
            db.ProjectSites.Add(site);

            var warehouse = new Warehouse
            {
                CompanyId = project.CompanyId,
                BranchId = project.BranchId,
                ProjectId = project.Id,
                Code = $"DP-{Guid.NewGuid():N}"[..12],
                Name = "Test Depo"
            };
            db.Warehouses.Add(warehouse);

            var boq = new ProjectBoq
            {
                CompanyId = project.CompanyId,
                ProjectId = project.Id,
                BoqNumber = $"KSF-{Guid.NewGuid():N}"[..12],
                Name = "Test Keşif"
            };
            db.ProjectBoqs.Add(boq);

            await db.SaveChangesAsync();

            siteId = site.Id;
            warehouseId = warehouse.Id;
            boqId = boq.Id;
        }

        var impact = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{project.Id}/deletion-impact");

        Assert.True(impact.GetProperty("canHardDelete").GetBoolean());
        Assert.True(impact.GetProperty("totalDependentRecords").GetInt32() >= 3);

        var response = await DeleteAsync(client, project.Id, project.Code);
        response.EnsureSuccessStatusCode();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Assert.False(await db.Projects.IgnoreQueryFilters()
                .AnyAsync(x => x.Id == project.Id));
            Assert.False(await db.ProjectSites.IgnoreQueryFilters()
                .AnyAsync(x => x.Id == siteId));
            Assert.False(await db.Warehouses.IgnoreQueryFilters()
                .AnyAsync(x => x.Id == warehouseId));
            Assert.False(await db.ProjectBoqs.IgnoreQueryFilters()
                .AnyAsync(x => x.Id == boqId));
        }
    }

    [Fact]
    public async Task Unarchive_RestoresProjectToActiveList()
    {
        var client = await CreateClientForRoleAsync("Genel Müdür");

        Project project;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            project = await TestDataFactory.CreateProjectAsync(
                db, Guid.NewGuid().ToString("N")[..8]);
        }

        (await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/archive", new { reason = "Yanlışlıkla" }))
            .EnsureSuccessStatusCode();

        (await client.PostAsync($"/api/projects/{project.Id}/unarchive", null))
            .EnsureSuccessStatusCode();

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await verifyDb.Projects.SingleAsync(x => x.Id == project.Id);

        Assert.False(stored.IsArchived);
        Assert.Null(stored.ArchivedAtUtc);
        Assert.Equal(ProjectStatus.Active, stored.Status);
    }
}
