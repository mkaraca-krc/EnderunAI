using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// STOK MUHASEBESİ İÇİN ASGARİ HESAP PLANI.
    ///
    /// S6b'den sonra mal kabul MUHASEBE FİŞİ KESMEDEN kesinleşmiyor:
    /// stok hesabı (150/153) ve 379.01 "faturası gelmemiş mal
    /// alımları" yoksa kabul durur. Bu bilinçli — hesabı olmayan
    /// şirkette stok sessizce muhasebesiz girmesin.
    ///
    /// Fabrikaya GÖMÜLMEDİ, çağrı yerinde kuruluyor: birçok test
    /// kendi hesap planını kendi kuruyor ve gömseydik aynı kod iki kez
    /// eklenirdi. Zaten var olan hesap tekrar eklenmez.
    /// </summary>
    public static async Task EnsureStockAccountsAsync(AppDbContext db, Guid companyId)
    {
        var existing = await db.AccountingAccounts
            .Where(x => x.CompanyId == companyId)
            .Select(x => x.Code)
            .ToListAsync();

        void Ensure(string code, string name, bool posting = true, int nature = 0)
        {
            if (existing.Contains(code)) return;

            db.AccountingAccounts.Add(new AccountingAccount
            {
                CompanyId = companyId,
                Code = code,
                Name = name,
                Level = 3,
                Nature = (AccountingAccountNature)nature,
                IsPostingAllowed = posting
            });
        }

        Ensure("150", "İlk Madde ve Malzeme");
        Ensure("153", "Ticari Mallar");

        // Satılan malın maliyeti — stoklu satış bu hesaba yazıyor.
        // Sarf da ticari mal da satılınca 621'e gider; 740 projede
        // TÜKETİLEN malzemenin hesabıdır, satışın değil.
        Ensure("621", "Satılan Ticari Mallar Maliyeti");

        // Canlıdaki gerçek durum: 379 ana hesabına fiş kesilemiyor,
        // bu yüzden alt hesap şart.
        Ensure("379", "DİĞER BORÇ VE GİDER KARŞILIKLARI", posting: false, nature: 1);

        // S6c — depodan çıkış ve sayım farkı hesapları.
        //
        // 740.03.09 canlı hesap planında var (KULLANILAN MALZEMELER);
        // çözümleyici önce onu, yoksa 740'ı deniyor. Test ikisini de
        // kuruyor ki tercih sırası gerçekten sınansın.
        Ensure("740", "Hizmet Üretim Maliyeti");
        Ensure("740.03.09", "KULLANILAN MALZEMELER");
        Ensure("770", "Genel Yönetim Giderleri");

        // Canlıdaki gerçek durum: 689 ana hesabına da fiş kesilemiyor.
        Ensure("689", "DİĞER OLAĞANDIŞI GİDER VE ZARARLAR (-)", posting: false);
        Ensure("649", "Diğer Olağan Gelir ve Kârlar", nature: 1);

        await db.SaveChangesAsync();

        // Alt hesapları ÜRETİMDEKİ tohumların kendisi açsın ki testler
        // tohumla aynı yoldan geçsin.
        await GoodsReceivedNotInvoicedAccountSeed.SeedAsync(db);
        await StockVarianceAccountSeed.SeedAsync(db);
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
