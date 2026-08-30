using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Accounting;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// HESAP PLANI YÖNETİMİ (HP/1 · K1–K9).
///
/// Aktarımdan (`HesapPlaniAktarimTests`) AYRI: orası dosyadan toplu
/// üretimi, burası tek tek yönetimi sınıyor. İki yol aynı kuralları
/// uygulamak zorunda ama ayrı kapılardan geçiyor.
/// </summary>
[Collection("Integration")]
public sealed class HesapPlaniYonetimiTests(DatabaseFixture fixture)
{
    private static int _sayac;

    private static string Ek() => $"hp{Interlocked.Increment(ref _sayac):D3}";

    private async Task<Guid> SirketKurAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, Ek());
        return company.Id;
    }

    private static IAccountingAccountService Servis(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IAccountingAccountService>();

    private static CreateAccountingAccountRequest Yeni(
        Guid companyId, string kod, string ad, Guid? ust = null) =>
        new(companyId, ust, kod, ad, null, (int)AccountingAccountNature.Debit,
            true, false, false, null);

    private static UpdateAccountingAccountRequest Guncelle(
        string ad, DateTime? surum, Guid? ust = null) =>
        new(ust, ad, surum, null, (int)AccountingAccountNature.Debit,
            true, false, false, null);

    // ═══════════════════════════════════════════════════════════════
    // K1 — KOD DEĞİŞMEZ
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// GÜNCELLEME SÖZLEŞMESİNDE KOD ALANI YOK.
    ///
    /// Bu, K1'in TEK gerçek kanıtı: alan sözleşmede yoksa istemci
    /// gönderemez, servis yazamaz. "Servis kodu yazmıyor" demek
    /// yetmez — alan dururken bir gün biri yazar.
    /// </summary>
    [Fact]
    public void K1_GuncellemeSozlesmesinde_KodAlaniYok()
    {
        var alanlar = typeof(UpdateAccountingAccountRequest)
            .GetProperties()
            .Select(x => x.Name)
            .ToArray();

        Assert.DoesNotContain("Code", alanlar);

        // POZİTİF KONTROL: sözleşme boş değil, ad hâlâ değiştirilebiliyor.
        Assert.Contains("Name", alanlar);
    }

    /// <summary>KOD, GÜNCELLEMEDEN SONRA AYNI KALIR.</summary>
    [Fact]
    public async Task K1_GuncellemeSonrasi_KodDegismiyor()
    {
        var companyId = await SirketKurAsync();

        using var scope = fixture.Factory.Services.CreateScope();
        var servis = Servis(scope);

        var olusan = await servis.CreateAsync(
            Yeni(companyId, "600", "SATIŞLAR"), CancellationToken.None);

        var guncel = await servis.UpdateAsync(
            olusan.Id, Guncelle("YURT İÇİ SATIŞLAR", olusan.Surum),
            CancellationToken.None);

        Assert.Equal("600", guncel.Code);
        Assert.Equal("YURT İÇİ SATIŞLAR", guncel.Name);
    }

    // ═══════════════════════════════════════════════════════════════
    // K3 — TEK KAPI
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// GÜNCELLEME SÖZLEŞMESİNDE AKTİFLİK ALANI YOK.
    ///
    /// İki kapı olsaydı biri diğerini sessizce ezerdi: az önce
    /// pasife alınmış bir hesap, eski `IsActive=true` taşıyan bir
    /// güncelleme formuyla geri açılırdı.
    /// </summary>
    [Fact]
    public void K3_GuncellemeSozlesmesinde_AktiflikAlaniYok()
    {
        var alanlar = typeof(UpdateAccountingAccountRequest)
            .GetProperties()
            .Select(x => x.Name)
            .ToArray();

        Assert.DoesNotContain("IsActive", alanlar);
        Assert.Contains("Name", alanlar);
    }

    /// <summary>PASİFE ALMA GERİ ALINABİLİR.</summary>
    [Fact]
    public async Task K3_PasifeAlma_GeriAlinabilir()
    {
        var companyId = await SirketKurAsync();

        using var scope = fixture.Factory.Services.CreateScope();
        var servis = Servis(scope);

        var hesap = await servis.CreateAsync(
            Yeni(companyId, "601", "YURT DIŞI SATIŞLAR"), CancellationToken.None);

        await servis.DeactivateAsync(hesap.Id, CancellationToken.None);
        Assert.False((await servis.GetByIdAsync(hesap.Id, CancellationToken.None))!.IsActive);

        await servis.ActivateAsync(hesap.Id, CancellationToken.None);
        Assert.True((await servis.GetByIdAsync(hesap.Id, CancellationToken.None))!.IsActive);
    }

    // ═══════════════════════════════════════════════════════════════
    // K5 — EKSİK ÜST HESAP OTOMATİK AÇILMAZ
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task K5_EksikUstHesap_TemizHata()
    {
        var companyId = await SirketKurAsync();

        using var scope = fixture.Factory.Services.CreateScope();
        var servis = Servis(scope);

        var hata = await Assert.ThrowsAsync<ArgumentException>(
            () => servis.CreateAsync(
                Yeni(companyId, "102.01.03", "VAKIFBANK TL"),
                CancellationToken.None));

        Assert.Contains("102.01", hata.Message);

        // ÜST HESAP OTOMATİK AÇILMADI.
        using var db = fixture.Factory.Services.CreateScope();
        var ctx = db.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await ctx.AccountingAccounts
            .AnyAsync(x => x.CompanyId == companyId && x.Code == "102.01"));
    }

    /// <summary>
    /// NOKTASIZ KOD ÜST HESAP İSTEMEZ — kural fazla geniş olmasın.
    /// </summary>
    [Fact]
    public async Task K5_NoktasizKok_Acilabiliyor()
    {
        var companyId = await SirketKurAsync();

        using var scope = fixture.Factory.Services.CreateScope();

        var hesap = await Servis(scope).CreateAsync(
            Yeni(companyId, "102", "BANKALAR"), CancellationToken.None);

        Assert.Equal("102", hesap.Code);
    }

    // ═══════════════════════════════════════════════════════════════
    // K4(b) — HAREKETİ OLAN HESABA ALT HESAP EKLENEMEZ
    // ═══════════════════════════════════════════════════════════════

    private async Task FisKesAsync(Guid companyId, Guid hesapId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var fis = new AccountingVoucher
        {
            CompanyId = companyId,
            VoucherNumber = $"TST-{Guid.NewGuid():N}"[..12]
        };
        db.AccountingVouchers.Add(fis);

        db.AccountingVoucherLines.Add(new AccountingVoucherLine
        {
            AccountingVoucherId = fis.Id,
            AccountingAccountId = hesapId,
            LineNumber = 1,
            DebitAmount = 100m,
            DebitAmountLocal = 100m
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// FİŞ KESİLMİŞ HESABIN ALTINA HESAP AÇILAMAZ.
    ///
    /// Bakiye, hangi alt hesaba ait olduğu belirsiz bir yerde
    /// kalırdı: toplam doğru görünür, kırılım yalan söylerdi.
    /// </summary>
    [Fact]
    public async Task K4b_HareketliHesabaAltHesapEklenemez()
    {
        var companyId = await SirketKurAsync();

        Guid ustId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var ust = await Servis(scope).CreateAsync(
                Yeni(companyId, "153", "TİCARİ MALLAR"), CancellationToken.None);

            ustId = ust.Id;
        }

        await FisKesAsync(companyId, ustId);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var hata = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Servis(scope).CreateAsync(
                    Yeni(companyId, "153.01", "DEMİR", ustId),
                    CancellationToken.None));

            Assert.Contains("yaprak", hata.Message);
        }
    }

    /// <summary>
    /// HAREKETİ OLMAYAN HESABA ALT HESAP AÇILABİLİR — kural fazla
    /// geniş olmasın. Bu iddia olmasaydı, her eklemeyi reddeden bir
    /// muhafız da üstteki testi geçerdi.
    /// </summary>
    [Fact]
    public async Task K4b_HareketsizHesabaAltHesapEklenebilir()
    {
        var companyId = await SirketKurAsync();

        using var scope = fixture.Factory.Services.CreateScope();
        var servis = Servis(scope);

        var ust = await servis.CreateAsync(
            Yeni(companyId, "120", "ALICILAR"), CancellationToken.None);

        var alt = await servis.CreateAsync(
            Yeni(companyId, "120.01", "YURT İÇİ ALICILAR", ust.Id),
            CancellationToken.None);

        Assert.Equal("120.01", alt.Code);
    }

    /// <summary>
    /// HAREKETLİ HESABIN ALTINA TAŞIMA DA ENGELLENİR.
    ///
    /// `UpdateAsync` üst hesabı değiştirmeye izin veriyor. Bir hesabı
    /// fiş kesilmiş bir hesabın ALTINA TAŞIMAK, oraya alt hesap
    /// EKLEMEKLE aynı şeydir; yalnız oluşturmaya konsaydı kural bir
    /// satır ötede delinirdi.
    ///
    /// BU TEST KAPSAMI GENİŞLETTİĞİM YERİ ÖLÇÜYOR. Kuralın metni
    /// "eklenemez" diyordu; taşımayı da kapsadığını okudum ve
    /// uyguladım — okumamı ölçüsüz bırakmıyorum.
    /// </summary>
    [Fact]
    public async Task K4b_HareketliHesabinAltinaTasinamaz()
    {
        var companyId = await SirketKurAsync();

        Guid hareketliId;
        Guid tasinacakId;
        DateTime tasinacakSurum;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var servis = Servis(scope);

            var hareketli = await servis.CreateAsync(
                Yeni(companyId, "157", "DİĞER STOKLAR"), CancellationToken.None);

            var tasinacak = await servis.CreateAsync(
                Yeni(companyId, "159", "VERİLEN SİPARİŞ AVANSLARI"),
                CancellationToken.None);

            hareketliId = hareketli.Id;
            tasinacakId = tasinacak.Id;
            tasinacakSurum = tasinacak.Surum;
        }

        await FisKesAsync(companyId, hareketliId);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var hata = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Servis(scope).UpdateAsync(
                    tasinacakId,
                    Guncelle("VERİLEN AVANSLAR", tasinacakSurum, hareketliId),
                    CancellationToken.None));

            Assert.Contains("yaprak", hata.Message);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // K8 — ESKİ FORM YAKALANIR
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// ESKİ SÜRÜMLE GÖNDERİLEN GÜNCELLEME REDDEDİLİR.
    ///
    /// BU TESTİN SENARYOSU KRİTİK: eşzamanlı iki istek DEĞİL.
    /// Eşzamanlı istekleri `xmin` tek başına da yakalar; o senaryo
    /// tele taşımanın eklediği hiçbir şeyi kanıtlamaz.
    ///
    /// Buradaki senaryo KULLANICININ AÇIK FORMU: sürüm okundu,
    /// arada başkası kaydetti, eski sürümle gönderildi.
    /// </summary>
    [Fact]
    public async Task K8_EskiSurumle_Guncelleme_Reddedilir()
    {
        var companyId = await SirketKurAsync();

        Guid hesapId;
        DateTime eskiSurum;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var hesap = await Servis(scope).CreateAsync(
                Yeni(companyId, "770", "GENEL YÖNETİM GİDERLERİ"),
                CancellationToken.None);

            hesapId = hesap.Id;
            eskiSurum = hesap.Surum;
        }

        // BAŞKASI KAYDETTİ — ayrı kapsam, ayrı DbContext.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var guncel = await Servis(scope)
                .GetByIdAsync(hesapId, CancellationToken.None);

            await Servis(scope).UpdateAsync(
                hesapId, Guncelle("GENEL YÖNETİM", guncel!.Surum),
                CancellationToken.None);
        }

        // ESKİ FORM: elindeki sürüm artık bayat.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var hata = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => Servis(scope).UpdateAsync(
                    hesapId, Guncelle("BAŞKA BİR AD", eskiSurum),
                    CancellationToken.None));

            Assert.Contains("başka bir kullanıcı", hata.Message);
        }
    }

    /// <summary>
    /// GÜNCEL SÜRÜMLE GÜNCELLEME GEÇER — muhafız fazla geniş olmasın.
    ///
    /// Bu iddia olmasaydı, HER güncellemeyi reddeden bozuk bir
    /// muhafız da üstteki testi geçerdi ve kimse hesap adı
    /// değiştiremezdi.
    /// </summary>
    [Fact]
    public async Task K8_GuncelSurumle_GuncellemeGecer()
    {
        var companyId = await SirketKurAsync();

        using var scope = fixture.Factory.Services.CreateScope();
        var servis = Servis(scope);

        var hesap = await servis.CreateAsync(
            Yeni(companyId, "760", "PAZARLAMA GİDERLERİ"), CancellationToken.None);

        var guncel = await servis.UpdateAsync(
            hesap.Id, Guncelle("PAZARLAMA SATIŞ DAĞITIM", hesap.Surum),
            CancellationToken.None);

        Assert.Equal("PAZARLAMA SATIŞ DAĞITIM", guncel.Name);
    }

    /// <summary>SÜRÜM YOKSA REDDEDİLİR — atlanmaz (Kural 39).</summary>
    [Fact]
    public async Task K8_SurumYoksa_Reddedilir()
    {
        var companyId = await SirketKurAsync();

        using var scope = fixture.Factory.Services.CreateScope();
        var servis = Servis(scope);

        var hesap = await servis.CreateAsync(
            Yeni(companyId, "780", "FİNANSMAN GİDERLERİ"), CancellationToken.None);

        var hata = await Assert.ThrowsAsync<ArgumentException>(
            () => servis.UpdateAsync(
                hesap.Id, Guncelle("YENİ AD", null), CancellationToken.None));

        /*
         * MESAJ KULLANICIYA NE YAPACAĞINI SÖYLEMELİ.
         *
         * Yayın anında sayfası açık olan kullanıcının paketi eskidir
         * ve sürüm göndermez. Reddedilmesi doğru; ama "sürüm
         * zorunludur" ona hiçbir şey anlatmaz. Bu iddia mesajın
         * eylem içerdiğini kilitliyor.
         */
        Assert.Contains("Sayfayı yenileyip", hata.Message);
    }

    // ═══════════════════════════════════════════════════════════════
    // K9 — DENETİM KAYDI
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// AD DEĞİŞİKLİĞİ ESKİ VE YENİ DEĞERLE KAYDA GEÇER.
    ///
    /// "Ad değişti" demek yetmez: bu paketin var olma sebebi
    /// "Banka 1" gibi adların düzeltilmesi ve sonradan "neydi"
    /// sorusunun sorulacak olması.
    /// </summary>
    [Fact]
    public async Task K9_AdDegisikligi_EskiVeYeniDegerleKaydedilir()
    {
        var companyId = await SirketKurAsync();

        Guid hesapId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var servis = Servis(scope);

            var hesap = await servis.CreateAsync(
                Yeni(companyId, "740", "HİZMET ÜRETİM MALİYETİ"),
                CancellationToken.None);

            hesapId = hesap.Id;

            await servis.UpdateAsync(
                hesap.Id, Guncelle("HİZMET MALİYETİ", hesap.Surum),
                CancellationToken.None);
        }

        using var son = fixture.Factory.Services.CreateScope();
        var db = son.ServiceProvider.GetRequiredService<AppDbContext>();

        var kayit = await db.SecurityAuditEvents
            .Where(x => x.EntityId == hesapId
                && x.Action == "hesap-plani.ad-degistir")
            .SingleAsync();

        Assert.Contains("HİZMET ÜRETİM MALİYETİ", kayit.DetailsJson);
        Assert.Contains("HİZMET MALİYETİ", kayit.DetailsJson);
    }

    /// <summary>PASİFE ALMA AYRI EYLEM OLARAK KAYDA GEÇER.</summary>
    [Fact]
    public async Task K9_PasifeAlma_AyriEylemOlarakKaydedilir()
    {
        var companyId = await SirketKurAsync();

        Guid hesapId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var servis = Servis(scope);

            var hesap = await servis.CreateAsync(
                Yeni(companyId, "689", "DİĞER OLAĞANDIŞI GİDER"),
                CancellationToken.None);

            hesapId = hesap.Id;

            await servis.DeactivateAsync(hesap.Id, CancellationToken.None);
        }

        using var son = fixture.Factory.Services.CreateScope();
        var db = son.ServiceProvider.GetRequiredService<AppDbContext>();

        var eylemler = await db.SecurityAuditEvents
            .Where(x => x.EntityId == hesapId)
            .Select(x => x.Action)
            .ToListAsync();

        Assert.Contains("hesap-plani.ekle", eylemler);
        Assert.Contains("hesap-plani.pasife-al", eylemler);
    }
}
