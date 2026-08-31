using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Services.Common;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// MASRAF MERKEZİ KURALI — SAF TESTLER.
///
/// Kural veritabanı istemiyor; şantiyenin projesi dışarıdan veriliyor.
/// Bu sayede üç iddia doğrudan sınanabiliyor ve testler saniyeler değil
/// milisaniyeler sürüyor.
///
/// NEDEN AYRI SINIF SINANIYOR: kural önce `WorkTasksController`'ın POST
/// gövdesindeydi ve oradan sınanamıyordu; Hızır ise denetleyiciyi hiç
/// görmüyor. Kuralın saf olması, onu çağıran her yolun aynı kuralı
/// çağırdığını göstermeyi mümkün kılıyor.
/// </summary>
public sealed class MasrafMerkeziKuraliTests
{
    private static readonly Guid Proje = Guid.NewGuid();
    private static readonly Guid BaskaProje = Guid.NewGuid();
    private static readonly Guid Sube = Guid.NewGuid();
    private static readonly Guid Santiye = Guid.NewGuid();

    // ───────── S1: üçü de boş ─────────

    [Fact]
    public void UcuDeBossa_Reddedilir()
    {
        var hata = MasrafMerkeziKurali.Dogrula(
            null, null, null, null, null, null);

        Assert.NotNull(hata);
        Assert.Contains("Masraf merkezi zorunludur", hata);
    }

    [Fact]
    public void TekMerkezVarsa_Kabul_PozitifKontrol()
    {
        /*
         * POZİTİF KONTROL: yukarıdaki test, kural HER İSTEĞİ reddetse de
         * yeşil kalırdı. Bu test o ihtimali kapatıyor.
         */
        Assert.Null(MasrafMerkeziKurali.Dogrula(
            Proje, null, null, null, null, null));

        Assert.Null(MasrafMerkeziKurali.Dogrula(
            null, Sube, null, null, null, null));

        Assert.Null(MasrafMerkeziKurali.Dogrula(
            Proje, null, Santiye, null, null, Proje));
    }

    // ───────── S2: CenterType çelişkisi ─────────

    [Theory]
    [InlineData(ExpenseCenterType.Branch)]
    [InlineData(ExpenseCenterType.ProjectSite)]
    public void ProjeSecilipBaskaTurYazilirsa_Reddedilir(ExpenseCenterType yanlisTur)
    {
        var hata = MasrafMerkeziKurali.Dogrula(
            Proje, null, null, yanlisTur, null, null);

        Assert.NotNull(hata);
        Assert.Contains("türü seçilen merkezle uyuşmuyor", hata);
    }

    [Fact]
    public void DogruTurYazilirsa_Kabul()
    {
        Assert.Null(MasrafMerkeziKurali.Dogrula(
            Proje, null, null, ExpenseCenterType.Project, null, null));

        Assert.Null(MasrafMerkeziKurali.Dogrula(
            null, Sube, null, ExpenseCenterType.Branch, null, null));
    }

    [Fact]
    public void TurTuretme_SecimdenGelir()
    {
        Assert.Equal(ExpenseCenterType.Project,
            MasrafMerkeziKurali.TuruTuret(Proje, null, null));

        Assert.Equal(ExpenseCenterType.Branch,
            MasrafMerkeziKurali.TuruTuret(null, Sube, null));

        // Şantiye + projesi birlikte gelir; en dar merkez kazanır.
        Assert.Equal(ExpenseCenterType.ProjectSite,
            MasrafMerkeziKurali.TuruTuret(Proje, null, Santiye));

        Assert.Null(MasrafMerkeziKurali.TuruTuret(null, null, null));
    }

    // ───────── S3: şantiye ile projesi çelişirse ─────────

    [Fact]
    public void BaskaProjeninSantiyesi_Reddedilir()
    {
        /*
         * A projesinin şantiyesi B projesiyle gönderiliyor. İki kaynak
         * çelişirse hangisinin doğru olduğu bilinemez — reddedilir.
         */
        var hata = MasrafMerkeziKurali.Dogrula(
            BaskaProje, null, Santiye, null, null, Proje);

        Assert.NotNull(hata);
        Assert.Contains("seçilen projeye ait değil", hata);
    }

    [Fact]
    public void SantiyeVarProjeYok_Reddedilir()
    {
        var hata = MasrafMerkeziKurali.Dogrula(
            null, null, Santiye, null, null, Proje);

        Assert.NotNull(hata);
        Assert.Contains("projesi de gönderilmelidir", hata);
    }

    [Fact]
    public void SantiyeBulunamazsa_Reddedilir()
    {
        var hata = MasrafMerkeziKurali.Dogrula(
            Proje, null, Santiye, null, null, null);

        Assert.NotNull(hata);
        Assert.Contains("şantiye bulunamadı", hata);
    }

    // ───────── Çoklu seçim ─────────

    [Fact]
    public void ProjeVeSubeBirlikte_Reddedilir()
    {
        var hata = MasrafMerkeziKurali.Dogrula(
            Proje, Sube, null, null, null, null);

        Assert.NotNull(hata);
        Assert.Contains("Tek bir masraf merkezi", hata);
    }

    // ───────── Açık kalan kapı: BİLEREK ─────────

    [Fact]
    public void KaydaBagliGorev_MerkezsizGecer_ACIK_KAPI()
    {
        /*
         * BU TEST BİR KUSURU SABİTLİYOR, BİR DAVRANIŞI DEĞİL.
         *
         * `SourceModule` dolu olan istek kuralın dışında kalıyor ve
         * merkezsiz geçebiliyor. Ön yüz artık her zaman merkez
         * gönderdiği için bu kaçış FİİLEN kullanılmıyor — ama KAPI
         * AÇIK ve öyle olduğu burada yazılı.
         *
         * Kapanması, kuralın dizgeye değil kaydın TÜRÜNE bakmasıyla
         * olacak: KURAL-KATMAN/1. O paket geldiğinde bu test
         * DEĞİŞTİRİLECEK — silinmeyecek, tersine çevrilecek.
         */
        Assert.Null(MasrafMerkeziKurali.Dogrula(
            null, null, null, null, "HAKEDIS", null));
    }
}
