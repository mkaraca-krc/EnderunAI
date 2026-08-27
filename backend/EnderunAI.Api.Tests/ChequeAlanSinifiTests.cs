using System.Reflection;
using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Accounting;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ALAN SINIFI — CIRCIR (ÇEK/2 · K4).
///
/// Bu dosyanın işi tek bir şeyi imkânsız kılmak: <see
/// cref="UpdateChequeRequest"/>'e yeni bir alan eklenip sınıfının
/// yazılmaması, ya da sınıfı yazılıp kilit karşılaştırmasına
/// eklenmemesi.
///
/// İKİNCİSİ DAHA TEHLİKELİ: sözlükte "Kilitli" yazan ama
/// karşılaştırılmayan bir alan, ekranda kilitli görünür, denetimde
/// kilitli sayılır ve fiilen serbesttir. Test bunu her kilitli alan
/// için TEK TEK deneyerek kapatıyor — listeye bakarak değil,
/// davranışa bakarak.
/// </summary>
public sealed class ChequeAlanSinifiTests
{
    private static PropertyInfo[] IstekAlanlari() =>
        typeof(UpdateChequeRequest).GetProperties(
            BindingFlags.Public | BindingFlags.Instance);

    /// <summary>
    /// SINIFSIZ ALAN KALAMAZ. Yeni alan ekleyip sözlüğe yazmayan
    /// burada kırmızı görür.
    /// </summary>
    [Fact]
    public void HerIstekAlaninin_SinifiTanimli()
    {
        var eksikler = IstekAlanlari()
            .Select(x => x.Name)
            .Where(x => !ChequeAlanSiniflari.Tumu.ContainsKey(x))
            .ToArray();

        Assert.True(eksikler.Length == 0,
            "Sınıfı tanımlanmamış alan(lar): " + string.Join(", ", eksikler) +
            ". ChequeAlanSiniflari sözlüğüne ekleyin.");
    }

    /// <summary>
    /// SÖZLÜKTE ARTIK ALAN KALMAZ — silinen bir alan sözlükte
    /// unutulursa kimse fark etmez ve sözlük zamanla gerçeği
    /// anlatmayan bir listeye döner.
    /// </summary>
    [Fact]
    public void SozluktekiHerAlan_IstektePayVar()
    {
        var adlar = IstekAlanlari().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);

        var fazlalar = ChequeAlanSiniflari.Tumu.Keys
            .Where(x => !adlar.Contains(x))
            .ToArray();

        Assert.True(fazlalar.Length == 0,
            "İstekte karşılığı olmayan alan(lar): " + string.Join(", ", fazlalar));
    }

    /// <summary>
    /// TAŞIYICI SINIF BİR SAKLANMA YERİ DEĞİLDİR.
    ///
    /// Bu küme sabitlenmezse, kilitlemek istemediği bir mali alanı
    /// "Kontrol" diye işaretleyen biri bütün kapıyı sessizce
    /// atlatabilirdi.
    /// </summary>
    [Fact]
    public void KontrolSinifi_SabitKalir()
    {
        var kontroller = ChequeAlanSiniflari.Tumu
            .Where(x => x.Value.Sinif == ChequeAlanSinifi.Kontrol)
            .Select(x => x.Key)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ChequeAlanSiniflari.KontrolAlanlari.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            kontroller);
    }

    /// <summary>
    /// K2 — TANIMLAYICI KÜME TAM OLARAK ÜÇ ALAN.
    ///
    /// Mehmet'in listesi: keşideci, şube, açıklama/not. Dördüncü bir
    /// alanın buraya sessizce eklenmesi, kapanmış çekte o alanın
    /// düzenlenebilir olması demektir — kararla gelir, kaymayla değil.
    /// </summary>
    [Fact]
    public void TanimlayiciKume_UcAlandanIbaret()
    {
        var tanimlayicilar = ChequeAlanSiniflari.Tumu
            .Where(x => x.Value.Sinif == ChequeAlanSinifi.Tanimlayici)
            .Select(x => x.Key)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "BankBranch", "Description", "Drawer" },
            tanimlayicilar);
    }

    /// <summary>Her alanın etiketi dolu — boş etiket denetim kaydını okunmaz yapar.</summary>
    [Fact]
    public void HerAlanin_EtiketiDolu()
    {
        foreach (var (ad, tanim) in ChequeAlanSiniflari.Tumu)
            Assert.False(string.IsNullOrWhiteSpace(tanim.Etiket), $"'{ad}' etiketsiz.");
    }

    /// <summary>Bilinmeyen alan sessizce bir sınıfa düşmez.</summary>
    [Fact]
    public void BilinmeyenAlan_IstisnaAtar()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ChequeAlanSiniflari.Sinif("UyduruAlan"));

        Assert.Contains("düzenleme sınıfı tanımlı değil", ex.Message);
    }

    // ═══════════════════════════════════════════════════════════════
    // ASIL ÇIRÇIR: her kilitli alan GERÇEKTEN karşılaştırılıyor mu
    // ═══════════════════════════════════════════════════════════════

    private static Cheque OrnekCek() => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        Direction = ChequeDirection.Received,
        ChequeNumber = "12345",
        BankName = "Test Bankası",
        BankBranch = "Merkez",
        Drawer = "Keşideci",
        CurrentAccountId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        CostCenterCode = "MRK",
        Amount = 10_000m,
        CurrencyCode = "TRY",
        ExchangeRate = 1m,
        IssueDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
        DueDate = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
        ProgressPaymentId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        SupplierInvoiceId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
        Description = "açıklama"
    };

    /// <summary>Çekle birebir aynı değerleri taşıyan istek.</summary>
    private static UpdateChequeRequest AyniIstek(Cheque c) => new(
        ChequeNumber: c.ChequeNumber,
        BankName: c.BankName,
        BankBranch: c.BankBranch,
        Drawer: c.Drawer,
        CurrentAccountId: c.CurrentAccountId,
        ProjectId: c.ProjectId,
        Amount: c.Amount,
        IssueDate: c.IssueDate,
        DueDate: c.DueDate,
        ProgressPaymentId: c.ProgressPaymentId,
        SupplierInvoiceId: c.SupplierInvoiceId,
        Description: c.Description,
        CostCenterCode: c.CostCenterCode,
        RowVersion: DateTime.UtcNow,
        EditReason: "test",
        CurrencyCode: c.CurrencyCode,
        ExchangeRate: c.ExchangeRate);

    /// <summary>Değişiklik yoksa kilitli alan da yok.</summary>
    [Fact]
    public void AyniDegerler_HicbirKilitliAlaniDegistirmez()
    {
        var cek = OrnekCek();
        Assert.Empty(ChequeAlanSiniflari.DegisenKilitliAlanlar(cek, AyniIstek(cek)));
    }

    /// <summary>
    /// TANIMLAYICI ALANLARI DEĞİŞTİRMEK KİLİDİ TETİKLEMEZ. K2'nin
    /// kendisi: kapanmış çekte bu üç alan düzeltilebilir.
    /// </summary>
    [Fact]
    public void TanimlayiciDegisiklikler_KilidiTetiklemez()
    {
        var cek = OrnekCek();
        var istek = AyniIstek(cek) with
        {
            Drawer = "Düzeltilmiş Keşideci",
            BankBranch = "Kızılay",
            Description = "yazım hatası düzeltildi"
        };

        Assert.Empty(ChequeAlanSiniflari.DegisenKilitliAlanlar(cek, istek));
    }

    /// <summary>
    /// HER KİLİTLİ ALAN, TEK TEK, GERÇEKTEN YAKALANIYOR.
    ///
    /// Sözlükte "Kilitli" yazıp `DegisenKilitliAlanlar` içinde
    /// karşılaştırılmayan bir alan burada kırmızı verir. Yansımayla
    /// yürüdüğü için yeni eklenen kilitli alanı kendiliğinden kapsar —
    /// testi güncellemeyi unutmak mümkün değil.
    /// </summary>
    [Fact]
    public void HerKilitliAlan_DegisinceBildiriliyor()
    {
        var cek = OrnekCek();
        var taban = AyniIstek(cek);

        var kilitliler = ChequeAlanSiniflari.Tumu
            .Where(x => x.Value.Sinif == ChequeAlanSinifi.Kilitli)
            .Select(x => x.Key)
            .ToArray();

        Assert.NotEmpty(kilitliler);

        var yakalanmayanlar = new List<string>();

        foreach (var alan in kilitliler)
        {
            var ozellik = typeof(UpdateChequeRequest).GetProperty(alan)
                ?? throw new InvalidOperationException($"'{alan}' istekte yok.");

            var bozuk = FarkliDegerle(taban, ozellik);

            var degisenler = ChequeAlanSiniflari.DegisenKilitliAlanlar(cek, bozuk);

            if (!degisenler.Contains(alan))
                yakalanmayanlar.Add(alan);
        }

        Assert.True(yakalanmayanlar.Count == 0,
            "Kilitli sayılan ama DEĞİŞİMİ YAKALANMAYAN alan(lar): " +
            string.Join(", ", yakalanmayanlar) +
            ". ChequeAlanSiniflari.DegisenKilitliAlanlar içinde karşılaştırın.");
    }

    /// <summary>
    /// Bir özelliği, mevcuttan KESİN farklı bir değere çevirir.
    /// Yansımayla `with` yapılamadığı için birincil kurucu yeniden
    /// çağrılıyor — parametre sırası kurucudan okunduğu için yeni
    /// alan eklenince de çalışmaya devam eder.
    /// </summary>
    private static UpdateChequeRequest FarkliDegerle(
        UpdateChequeRequest taban, PropertyInfo hedef)
    {
        var kurucu = typeof(UpdateChequeRequest).GetConstructors().Single();

        var argumanlar = kurucu.GetParameters()
            .Select(p =>
            {
                var ozellik = typeof(UpdateChequeRequest).GetProperty(p.Name!)!;
                var mevcut = ozellik.GetValue(taban);

                return string.Equals(p.Name, hedef.Name, StringComparison.Ordinal)
                    ? BaskaDeger(p.ParameterType, mevcut)
                    : mevcut;
            })
            .ToArray();

        return (UpdateChequeRequest)kurucu.Invoke(argumanlar);
    }

    private static object? BaskaDeger(Type tur, object? mevcut)
    {
        var temel = Nullable.GetUnderlyingType(tur) ?? tur;

        if (temel == typeof(string))
            return (mevcut as string) == "BASKA" ? "BASKA-2" : "BASKA";

        if (temel == typeof(decimal))
            return (decimal?)mevcut is { } d ? d + 1m : 1m;

        if (temel == typeof(DateTime))
            return (DateTime?)mevcut is { } t
                ? t.AddDays(1)
                : new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        if (temel == typeof(Guid))
            return Guid.Parse("99999999-9999-9999-9999-999999999999");

        if (temel == typeof(int)) return ((int?)mevcut ?? 0) + 1;
        if (temel == typeof(bool)) return !((bool?)mevcut ?? false);

        throw new NotSupportedException(
            $"'{tur}' türü için farklı değer üretilemedi; teste ekleyin.");
    }
}
