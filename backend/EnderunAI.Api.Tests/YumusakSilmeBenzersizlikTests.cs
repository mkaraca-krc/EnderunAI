using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// YUMUŞAK SİLİNEN KAYDIN KODU REHİN KALMAZ (KURULUM/1 · Kural 49).
///
/// Süzgeçsiz benzersizlik, silinmiş bir departmanın kodunu tutuyordu:
/// kullanıcı departmanı siliyor, aynı kodla yenisini AÇAMIYORDU.
/// Yumuşak silmenin anlamı kaydın kullanıcı için yok olmasıdır.
///
/// Bu, aynı oturumdaki çek hatasının kardeşi: "iptal et ve aynı
/// numarayla yeniden gir" yolu kapalıydı ve tek çare veriyi
/// bozmaktı.
///
/// KISIT GEVŞEMİYOR: iki AKTİF kayıt hâlâ aynı kodu alamaz. Test
/// bunu da doğruluyor — yoksa "süzgeç ekledik" diye benzersizliği
/// tümden kaldırmış olabilirdik ve kimse fark etmezdi.
/// </summary>
[Collection("Integration")]
public sealed class YumusakSilmeBenzersizlikTests(DatabaseFixture fixture)
{
    /// <summary>
    /// SİLİNMİŞ DEPARTMANIN KODU YENİDEN KULLANILABİLİR.
    /// </summary>
    [Fact]
    public async Task SilinmisDepartmaninKodu_YenidenKullanilabilir()
    {
        var ek = Guid.NewGuid().ToString("N")[..8];
        var sirketId = Guid.NewGuid();
        var kod = $"DEP-{ek}";

        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var ilk = new HrDepartment
        {
            CompanyId = sirketId, Code = kod, Name = "Muhasebe"
        };
        hrDb.Departments.Add(ilk);
        await hrDb.SaveChangesAsync();

        // YUMUŞAK SİLME: kayıt duruyor, kullanıcı için yok.
        ilk.IsDeleted = true;
        await hrDb.SaveChangesAsync();

        // AYNI KOD, YENİ KAYIT — asıl sınama.
        hrDb.Departments.Add(new HrDepartment
        {
            CompanyId = sirketId, Code = kod, Name = "Muhasebe (yeni)"
        });

        var hata = await Record.ExceptionAsync(() => hrDb.SaveChangesAsync());

        Assert.True(hata is null,
            "Silinmiş departmanın kodu yeniden kullanılamadı: " + hata?.Message);
    }

    /// <summary>
    /// İKİ AKTİF DEPARTMAN AYNI KODU ALAMAZ — kısıt gevşemedi.
    /// </summary>
    [Fact]
    public async Task IkiAktifDepartman_AyniKoduAlamaz()
    {
        var ek = Guid.NewGuid().ToString("N")[..8];
        var sirketId = Guid.NewGuid();
        var kod = $"DEP-{ek}";

        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        hrDb.Departments.Add(new HrDepartment
        {
            CompanyId = sirketId, Code = kod, Name = "Birinci"
        });
        await hrDb.SaveChangesAsync();

        hrDb.Departments.Add(new HrDepartment
        {
            CompanyId = sirketId, Code = kod, Name = "İkinci"
        });

        await Assert.ThrowsAnyAsync<DbUpdateException>(
            () => hrDb.SaveChangesAsync());
    }

    /// <summary>
    /// SİLİNMİŞ POZİSYONUN KODU YENİDEN KULLANILABİLİR.
    /// </summary>
    [Fact]
    public async Task SilinmisPozisyonunKodu_YenidenKullanilabilir()
    {
        var ek = Guid.NewGuid().ToString("N")[..8];
        var sirketId = Guid.NewGuid();
        var departmanId = Guid.NewGuid();
        var kod = $"POZ-{ek}";

        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var ilk = new HrPosition
        {
            CompanyId = sirketId, DepartmentId = departmanId,
            Code = kod, Title = "Şef"
        };
        hrDb.Positions.Add(ilk);
        await hrDb.SaveChangesAsync();

        ilk.IsDeleted = true;
        await hrDb.SaveChangesAsync();

        hrDb.Positions.Add(new HrPosition
        {
            CompanyId = sirketId, DepartmentId = departmanId,
            Code = kod, Title = "Şef (yeni)"
        });

        var hata = await Record.ExceptionAsync(() => hrDb.SaveChangesAsync());

        Assert.True(hata is null,
            "Silinmiş pozisyonun kodu yeniden kullanılamadı: " + hata?.Message);
    }

    /// <summary>
    /// İKİ AKTİF POZİSYON AYNI DEPARTMANDA AYNI KODU ALAMAZ.
    /// </summary>
    [Fact]
    public async Task IkiAktifPozisyon_AyniKoduAlamaz()
    {
        var ek = Guid.NewGuid().ToString("N")[..8];
        var sirketId = Guid.NewGuid();
        var departmanId = Guid.NewGuid();
        var kod = $"POZ-{ek}";

        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        hrDb.Positions.Add(new HrPosition
        {
            CompanyId = sirketId, DepartmentId = departmanId,
            Code = kod, Title = "Birinci"
        });
        await hrDb.SaveChangesAsync();

        hrDb.Positions.Add(new HrPosition
        {
            CompanyId = sirketId, DepartmentId = departmanId,
            Code = kod, Title = "İkinci"
        });

        await Assert.ThrowsAnyAsync<DbUpdateException>(
            () => hrDb.SaveChangesAsync());
    }
}
