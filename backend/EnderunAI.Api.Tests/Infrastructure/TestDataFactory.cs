using EnderunAI.Api.Data;
using EnderunAI.Api.Models;

namespace EnderunAI.Api.Tests.Infrastructure;

public static class TestDataFactory
{
    public static async Task<(Company Company, Branch Branch, CurrentAccount Account)> CreateCompanyStackAsync(
        AppDbContext db,
        string suffix)
    {
        var company = new Company
        {
            Code = $"CMP-{suffix}",
            Name = $"Test Şirket {suffix}"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var branch = new Branch
        {
            CompanyId = company.Id,
            Code = $"BR-{suffix}",
            Name = $"Test Şube {suffix}",
            IsHeadOffice = true
        };
        db.Branches.Add(branch);

        var account = new CurrentAccount
        {
            CompanyId = company.Id,
            Code = $"CARI-{suffix}",
            Title = $"Test İşveren {suffix}",
            Roles = CurrentAccountRoles.Customer,
            Status = CurrentAccountStatus.Approved
        };
        db.CurrentAccounts.Add(account);

        await db.SaveChangesAsync();

        return (company, branch, account);
    }

    public static async Task<Project> CreateProjectAsync(AppDbContext db, string suffix)
    {
        var (company, branch, account) = await CreateCompanyStackAsync(db, suffix);

        var project = new Project
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            EmployerCurrentAccountId = account.Id,
            Code = $"PRJ-{suffix}",
            Name = $"Test Proje {suffix}",
            CurrencyCode = "TRY",
            Status = ProjectStatus.Active
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        return project;
    }

    public static async Task<Personnel> CreatePersonnelAsync(AppDbContext db, Guid companyId, string suffix)
    {
        var personnel = new Personnel
        {
            CompanyId = companyId,
            EmployeeNumber = $"PRS-{suffix}",
            FirstName = "Test",
            LastName = $"Personel {suffix}",
            Status = PersonnelStatus.Active
        };
        db.Personnel.Add(personnel);
        await db.SaveChangesAsync();

        return personnel;
    }
}
