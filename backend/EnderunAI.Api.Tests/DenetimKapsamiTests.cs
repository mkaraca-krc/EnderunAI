using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// DENETIM/2 — DENETİM KAPSAMI DIŞLAMAYLA TANIMLANIR.
///
/// ═══ ÖLÇÜLEN KUSUR (2026-09-13) ═══
///
/// `AuditSaveChangesInterceptor` 18 türlük bir İZİN listesi taşıyordu.
/// Listede OLMAYANLAR: `Cheque`, `CurrentAccount`, `StockMovement`,
/// `GoodsReceipt`, `InventoryItem`, `AccountingAccount` — yani
/// **paranın yaşadığı yer.** Bir çek değiştirildiğinde, bir cari
/// bakiyesi elle düzeltildiğinde, bir hesap bayrağı çevrildiğinde
/// sistemde HİÇBİR İZ KALMIYORDU.
///
/// İzin listesinin kusuru yapısaldır: yarın eklenen varlık KORUMASIZ
/// doğar ve kimse fark etmez. Dışlama listesinde korumalı doğar.
///
/// ═══ BU SONDA NE ÖLÇER ═══
///
/// Eskiden kapsam dışı olan türler artık denetim satırı üretiyor mu, ve
/// eskiden kapsamda olan tür üretmeye DEVAM ediyor mu (pozitif kontrol).
/// </summary>
[Collection("Integration")]
public sealed class DenetimKapsamiTests(DatabaseFixture fixture)
{
    private async Task<int> DenetimSayisiAsync(AppDbContext db) =>
        await db.Set<SecurityAuditEvent>().CountAsync();

    /// <summary>
    /// KIRMIZIYA DÖNERSE: hesap planı bayrağı değişir, kim değiştirdiği
    /// hiçbir yerde durmaz. E4 bayrak değişikliğinin iz bırakmamasının
    /// sebebi tam olarak buydu.
    /// </summary>
    [Fact]
    public async Task MuhasebeHesabi_DenetimIziBirakir()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ek = Guid.NewGuid().ToString("N")[..8];
        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, ek);

        var once = await DenetimSayisiAsync(db);

        var hesap = new AccountingAccount
        {
            CompanyId = company.Id,
            Code = $"150.{ek[..3]}",
            Name = "Denetim sondası",
            Level = 3,
            Nature = AccountingAccountNature.Debit,
            IsPostingAllowed = true,
        };
        db.AccountingAccounts.Add(hesap);
        await db.SaveChangesAsync();

        Assert.True(await DenetimSayisiAsync(db) > once,
            "Muhasebe hesabı OLUŞTURMA denetim izi bırakmadı.");

        var ikinci = await DenetimSayisiAsync(db);
        hesap.RequiresProject = !hesap.RequiresProject;
        await db.SaveChangesAsync();

        Assert.True(await DenetimSayisiAsync(db) > ikinci,
            "Hesap bayrağı DEĞİŞTİRME denetim izi bırakmadı — E4'ün ta kendisi.");
    }

    /// <summary>
    /// KIRMIZIYA DÖNERSE: çek kaydı değiştirilir ve kim değiştirdiği
    /// bilinmez. Paranın yaşadığı yer.
    /// </summary>
    [Fact]
    public async Task Cek_DenetimIziBirakir()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ek = Guid.NewGuid().ToString("N")[..8];
        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, ek);

        var once = await DenetimSayisiAsync(db);

        var cek = new Cheque
        {
            CompanyId = company.Id,
            InternalNumber = $"CEK-{ek}",
            ChequeNumber = $"000{ek[..5]}",
            BankName = "Sonda Bankası",
            Amount = 1000m,
            CurrencyCode = "TRY",
            ExchangeRate = 1m,
            AmountTry = 1000m,
        };
        db.Cheques.Add(cek);
        await db.SaveChangesAsync();

        Assert.True(await DenetimSayisiAsync(db) > once,
            "Çek OLUŞTURMA denetim izi bırakmadı.");

        var ikinci = await DenetimSayisiAsync(db);
        cek.Amount = 2000m;
        cek.AmountTry = 2000m;
        await db.SaveChangesAsync();

        Assert.True(await DenetimSayisiAsync(db) > ikinci,
            "Çek TUTARI değiştirme denetim izi bırakmadı.");
    }

    /// <summary>KIRMIZIYA DÖNERSE: cari kaydı izsiz değişir.</summary>
    [Fact]
    public async Task Cari_DenetimIziBirakir()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ek = Guid.NewGuid().ToString("N")[..8];
        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, ek);

        var once = await DenetimSayisiAsync(db);

        var cari = new CurrentAccount
        {
            CompanyId = company.Id,
            Code = $"DNT-{ek}-{Guid.NewGuid():N}"[..24],
            Title = "Denetim sondası cari",
            Roles = CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved,
        };
        db.CurrentAccounts.Add(cari);
        await db.SaveChangesAsync();

        Assert.True(await DenetimSayisiAsync(db) > once,
            "Cari OLUŞTURMA denetim izi bırakmadı.");
    }

    /// <summary>KIRMIZIYA DÖNERSE: stok kartı izsiz değişir.</summary>
    [Fact]
    public async Task StokKarti_DenetimIziBirakir()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ek = Guid.NewGuid().ToString("N")[..8];
        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, ek);

        var once = await DenetimSayisiAsync(db);

        db.InventoryItems.Add(new InventoryItem
        {
            CompanyId = company.Id,
            Code = $"MLZ-{ek}",
            Name = "Denetim sondası malzeme",
            Unit = "adet",
        });
        await db.SaveChangesAsync();

        Assert.True(await DenetimSayisiAsync(db) > once,
            "Stok kartı OLUŞTURMA denetim izi bırakmadı.");
    }

    /// <summary>
    /// (e) YAPISAL: ESKİ İZİN LİSTESİNDE HİÇ OLMAYAN bir tür de
    /// denetleniyor. Varsayılan "denetlenir" olduğunun kanıtı —
    /// yarın eklenecek varlık da korumalı doğar.
    /// </summary>
    [Fact]
    public async Task IzinListesindeHicOlmayanTur_DeDenetleniyor()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ek = Guid.NewGuid().ToString("N")[..8];
        var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(db, ek);

        var once = await DenetimSayisiAsync(db);

        db.Warehouses.Add(new Warehouse
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            Code = $"DEPO-{ek}",
            Name = "Denetim sondası depo",
            Type = WarehouseType.Central,
        });
        await db.SaveChangesAsync();

        Assert.True(await DenetimSayisiAsync(db) > once,
            "Depo (eski izin listesinde HİÇ yoktu) denetim izi bırakmadı — " +
            "varsayılan 'denetlenir' değil demektir.");
    }

    /// <summary>
    /// POZİTİF KONTROL: eski izin listesinde OLAN tür üretmeye DEVAM
    /// ediyor. Bu olmadan yukarıdaki yeşiller, kapsamın genişlediğini
    /// değil sayacın her şeyi saydığını gösteriyor olabilirdi.
    /// </summary>
    [Fact]
    public async Task EskidenDeKapsamdaOlanTur_UretmeyeDevamEdiyor()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ek = Guid.NewGuid().ToString("N")[..8];

        var once = await DenetimSayisiAsync(db);
        await TestDataFactory.CreateProjectAsync(db, ek);   // Project: eski listede VARDI

        Assert.True(await DenetimSayisiAsync(db) > once,
            "Proje (eski listede VARDI) artık iz bırakmıyor — gerileme.");
    }

    /// <summary>
    /// DIŞLANAN TÜR GERÇEKTEN DIŞARIDA — ölçüm sağlığı.
    /// Her şey denetleniyorsa yukarıdaki iddialar hiçbir ayrım
    /// yapmıyor demektir.
    /// </summary>
    [Fact]
    public async Task DislananTur_IzBirakmiyor()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var once = await DenetimSayisiAsync(db);

        db.Set<EnderunAI.Api.Models.Market.ExchangeRate>().Add(
            new EnderunAI.Api.Models.Market.ExchangeRate
            {
                CurrencyCode = "XAU",
                RateDate = DateTime.UtcNow.Date.AddYears(-5),
                ForexBuying = 1m,
                ForexSelling = 1m,
            });
        await db.SaveChangesAsync();

        Assert.Equal(once, await DenetimSayisiAsync(db));
    }
}
