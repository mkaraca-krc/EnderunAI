using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// DAMGA MUHAFIZI — `UpdatedAtUtc` HER DEĞİŞİKLİKTE İLERLER.
///
/// ───────────────────────────────────────────────────────────────
/// RİSK NEREDE — ÖNCE BUNU OKU
/// ───────────────────────────────────────────────────────────────
///
/// Damga sorumluluğu SERVİSLERDE DEĞİL, `AuditSaveChangesInterceptor`
/// içindedir: `ChangeTracker.Entries&lt;BaseEntity&gt;()` üzerinden
/// `Modified` olan HER varlığa `UpdatedAtUtc = now` yazar.
///
/// Dolayısıyla risk **"bir yol unutur"** DEĞİL. Yeni bir servis
/// yazma yolu eklendiğinde hiçbir şey yapmasına gerek yok; damga
/// otomatik basılır.
///
/// GERÇEK RİSKLER İKİ TANE:
///   1. Araya girici KALDIRILIR ya da kapsamı DARALTILIR
///      (`Entries&lt;BaseEntity&gt;()` → `Entries&lt;X&gt;()`)
///   2. Bir yazma İZLEYİCİYİ ATLAR (`ExecuteUpdateAsync`, ham SQL)
///
/// Bu test 1'i tutuyor. 2 için dar tarama yapıldı (2026-08-30):
/// `accounting_accounts` tablosuna `ExecuteUpdate`/`ExecuteDelete`/
/// `ExecuteSqlRaw` ile yazan YER YOK. Eklenirse damga atlanır ve bu
/// test bunu GÖREMEZ — kapalı arama uzayı olduğu için taramayla
/// izlenir, testle değil.
///
/// ───────────────────────────────────────────────────────────────
/// BU KIRMIZIYA DÖNERSE
/// ───────────────────────────────────────────────────────────────
///
/// Eşzamanlılık koruması TAMAMEN çalışmaz hâle gelir: `KayitSurumu`
/// damgayı karşılaştırıyor, damga ilerlemezse iki kullanıcı aynı
/// kaydı sırayla ezer ve ikisi de başarı mesajı alır. Görünmez
/// kayıp güncelleme.
///
/// NEDEN METİN TARAMASI DEĞİL: "yazan yol" kaç biçimde yazılabilir
/// sorusu bu oturumda DÖRT KEZ yanlış cevaplandı (Kural 65).
/// Davranış testi o soruyu hiç sormuyor.
/// </summary>
[Collection("Integration")]
public sealed class DamgaMuhafiziTests(DatabaseFixture fixture)
{
    /// <summary>
    /// HESAP PLANI — bu paketin eşzamanlılık koruması buna dayanıyor.
    /// </summary>
    [Fact]
    public async Task AccountingAccount_DegistiginceDamgaIlerler()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, "dmg1");

        var hesap = new AccountingAccount
        {
            CompanyId = company.Id,
            Code = "900",
            Name = "DAMGA TESTİ",
            Nature = AccountingAccountNature.Debit
        };
        db.AccountingAccounts.Add(hesap);
        await db.SaveChangesAsync();

        var oncekiDamga = hesap.UpdatedAtUtc;

        hesap.Name = "DAMGA TESTİ — DEĞİŞTİ";
        await db.SaveChangesAsync();

        Assert.NotNull(hesap.UpdatedAtUtc);
        Assert.NotEqual(oncekiDamga, hesap.UpdatedAtUtc);
    }

    /// <summary>
    /// BAŞKA BİR `BaseEntity` — kapsamın TÜR BAZLI DARALTILMADIĞINI
    /// gösterir.
    ///
    /// Yalnız hesap planı sınansaydı, araya giricinin kapsamı
    /// `Entries&lt;AccountingAccount&gt;()`e daraltıldığında test YEŞİL
    /// kalırdı ve diğer tüm varlıklar sessizce damgasız kalırdı.
    /// </summary>
    [Fact]
    public async Task BaskaBirVarlik_DegistiginceDamgaIlerler()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, "dmg2");

        var oncekiDamga = company.UpdatedAtUtc;

        company.Name = $"{company.Name} — değişti";
        await db.SaveChangesAsync();

        Assert.NotNull(company.UpdatedAtUtc);
        Assert.NotEqual(oncekiDamga, company.UpdatedAtUtc);
    }

    /// <summary>
    /// YENİ KAYITTA DAMGA YOK AMA OLUŞTURMA DAMGASI VAR.
    ///
    /// `KayitSurumu.Oku` hiç güncellenmemiş kayıtta `CreatedAtUtc`
    /// kullanıyor; o damga basılmazsa yeni açılan kayıt HİÇ
    /// düzenlenemez.
    /// </summary>
    [Fact]
    public async Task YeniKayit_OlusturmaDamgasiniAlir()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, "dmg3");

        var hesap = new AccountingAccount
        {
            CompanyId = company.Id,
            Code = "901",
            Name = "YENİ KAYIT",
            Nature = AccountingAccountNature.Debit
        };
        db.AccountingAccounts.Add(hesap);
        await db.SaveChangesAsync();

        Assert.NotEqual(default, hesap.CreatedAtUtc);
    }
}
