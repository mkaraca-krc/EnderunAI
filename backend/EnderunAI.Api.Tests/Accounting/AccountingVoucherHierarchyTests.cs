using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Accounting;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnderunAI.Api.Tests.Accounting;

public sealed class AccountingVoucherHierarchyTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Create_UsesSharedFinanceHierarchyScope()
    {
        await using var db = CreateDbContext();
        var fixture = AddFixture(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.CreateAsync(
            CreateRequest(fixture),
            CancellationToken.None);

        var scopes = await db.ProjectModuleScopes
            .Where(scope =>
                scope.ModuleType == ProjectModuleType.Finance)
            .ToListAsync();

        Assert.Equal(2, scopes.Count);
        Assert.All(scopes, scope =>
        {
            Assert.Equal(fixture.Project.Id, scope.ProjectId);
            Assert.Equal(fixture.Node.Id,
                scope.ProjectHierarchyNodeId);
        });
        Assert.All(result.Lines, line =>
        {
            Assert.Equal(fixture.Node.Id,
                line.ProjectHierarchyNodeId);
            Assert.Equal("ANK", line.ProjectHierarchyNodeCode);
        });
    }

    [Fact]
    public async Task Create_RejectsNodeFromAnotherProject()
    {
        await using var db = CreateDbContext();
        var fixture = AddFixture(db);
        var otherProject = new Project
        {
            CompanyId = fixture.Company.Id,
            BranchId = fixture.Branch.Id,
            EmployerCurrentAccountId =
                fixture.CurrentAccount.Id,
            Code = "PRJ-002",
            Name = "Başka Proje"
        };
        var otherLevel = new ProjectHierarchyLevel
        {
            Project = otherProject,
            Code = "CITY",
            Name = "Şehir",
            SortOrder = 10
        };
        var otherNode = new ProjectHierarchyNode
        {
            Project = otherProject,
            Level = otherLevel,
            Code = "IST",
            Name = "İstanbul",
            SortOrder = 10
        };
        db.AddRange(otherProject, otherLevel, otherNode);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var request = CreateRequest(fixture);
        request = request with
        {
            Lines = request.Lines
                .Select(line => line with
                {
                    ProjectHierarchyNodeId = otherNode.Id
                })
                .ToArray()
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(
                request,
                CancellationToken.None));

        Assert.Contains(
            "seçilen projeye ait değil",
            exception.Message);
    }

    [Fact]
    public async Task Post_StampsCurrentUser()
    {
        await using var db = CreateDbContext();
        var fixture = AddFixture(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var voucher = await service.CreateAsync(
            CreateRequest(fixture),
            CancellationToken.None);

        await service.PostAsync(
            voucher.Id,
            CancellationToken.None);

        var posted = await db.AccountingVouchers
            .SingleAsync(item => item.Id == voucher.Id);
        Assert.Equal(AccountingVoucherStatus.Posted, posted.Status);
        Assert.Equal(UserId, posted.PostedByUserId);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(
                $"accounting-hierarchy-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options, new FakeCurrentUser());
    }

    private static AccountingVoucherService CreateService(
        AppDbContext db) =>
        new(db, new FakeDocumentNumberService(),
            new FakeCurrentUser());

    private static Fixture AddFixture(AppDbContext db)
    {
        var company = new Company
        {
            Code = "END",
            Name = "Enderun"
        };
        var branch = new Branch
        {
            Company = company,
            Code = "MRK",
            Name = "Merkez",
            IsHeadOffice = true
        };
        var currentAccount = new CurrentAccount
        {
            Company = company,
            Code = "C-001",
            Title = "İşveren",
            Roles = CurrentAccountRoles.Customer,
            Status = CurrentAccountStatus.Approved
        };
        var project = new Project
        {
            Company = company,
            Branch = branch,
            EmployerCurrentAccount = currentAccount,
            Code = "PRJ-001",
            Name = "MKE Projesi",
            ContractAmount = 1_000_000m,
            Status = ProjectStatus.Active
        };
        var level = new ProjectHierarchyLevel
        {
            Project = project,
            Code = "CITY",
            Name = "Şehir",
            SortOrder = 10
        };
        var node = new ProjectHierarchyNode
        {
            Project = project,
            Level = level,
            Code = "ANK",
            Name = "Ankara",
            SortOrder = 10
        };
        var debitAccount = new AccountingAccount
        {
            Company = company,
            Code = "120.01",
            Name = "Alıcılar",
            Nature = AccountingAccountNature.Debit,
            IsPostingAllowed = true,
            RequiresProject = true
        };
        var creditAccount = new AccountingAccount
        {
            Company = company,
            Code = "600.01",
            Name = "Yurtiçi Satışlar",
            Nature = AccountingAccountNature.Credit,
            IsPostingAllowed = true,
            RequiresProject = true
        };

        db.AddRange(
            company,
            branch,
            currentAccount,
            project,
            level,
            node,
            debitAccount,
            creditAccount);

        return new Fixture(
            company,
            branch,
            currentAccount,
            project,
            node,
            debitAccount,
            creditAccount);
    }

    private static CreateAccountingVoucherRequest CreateRequest(
        Fixture fixture) =>
        new(
            fixture.Company.Id,
            (int)AccountingVoucherType.Journal,
            new DateTime(2026, 7, 28),
            "TRY",
            1m,
            "Hiyerarşi entegrasyon testi",
            "TEST-001",
            "hakedis",
            Guid.NewGuid(),
            [
                new AccountingVoucherLineRequest(
                    fixture.DebitAccount.Id,
                    "Borç",
                    100m,
                    0m,
                    "TRY",
                    1m,
                    fixture.CurrentAccount.Id,
                    fixture.Project.Id,
                    fixture.Node.Id,
                    null,
                    null,
                    null,
                    null),
                new AccountingVoucherLineRequest(
                    fixture.CreditAccount.Id,
                    "Alacak",
                    0m,
                    100m,
                    "TRY",
                    1m,
                    fixture.CurrentAccount.Id,
                    fixture.Project.Id,
                    fixture.Node.Id,
                    null,
                    null,
                    null,
                    null)
            ]);

    private sealed record Fixture(
        Company Company,
        Branch Branch,
        CurrentAccount CurrentAccount,
        Project Project,
        ProjectHierarchyNode Node,
        AccountingAccount DebitAccount,
        AccountingAccount CreditAccount);

    private sealed class FakeDocumentNumberService
        : IDocumentNumberService
    {
        public Task<string> GenerateAsync(
            Guid companyId,
            string documentType,
            string prefix,
            CancellationToken cancellationToken = default) =>
            Task.FromResult($"{prefix}-2026-000001");
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => AccountingVoucherHierarchyTests.UserId;
        public string? Username => "test.user";
        public string? FullName => "Test User";
        public IReadOnlyCollection<string> Roles => ["Admin"];
        public IReadOnlyCollection<string> Permissions => ["*"];
        public bool IsInRole(string role) => role == "Admin";
        public bool HasPermission(string permission) => true;
    }
}
