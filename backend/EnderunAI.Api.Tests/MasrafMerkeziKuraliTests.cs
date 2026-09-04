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
            null, null, null, null, null);

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
            Proje, null, null, null, null));

        Assert.Null(MasrafMerkeziKurali.Dogrula(
            null, Sube, null, null, null));

        Assert.Null(MasrafMerkeziKurali.Dogrula(
            Proje, null, Santiye, null, Proje));
    }

    // ───────── S2: CenterType çelişkisi ─────────

    [Theory]
    [InlineData(ExpenseCenterType.Branch)]
    [InlineData(ExpenseCenterType.ProjectSite)]
    public void ProjeSecilipBaskaTurYazilirsa_Reddedilir(ExpenseCenterType yanlisTur)
    {
        var hata = MasrafMerkeziKurali.Dogrula(
            Proje, null, null, yanlisTur, null);

        Assert.NotNull(hata);
        Assert.Contains("türü seçilen merkezle uyuşmuyor", hata);
    }

    [Fact]
    public void DogruTurYazilirsa_Kabul()
    {
        Assert.Null(MasrafMerkeziKurali.Dogrula(
            Proje, null, null, ExpenseCenterType.Project, null));

        Assert.Null(MasrafMerkeziKurali.Dogrula(
            null, Sube, null, ExpenseCenterType.Branch, null));
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
            BaskaProje, null, Santiye, null, Proje);

        Assert.NotNull(hata);
        Assert.Contains("seçilen projeye ait değil", hata);
    }

    [Fact]
    public void SantiyeVarProjeYok_Reddedilir()
    {
        var hata = MasrafMerkeziKurali.Dogrula(
            null, null, Santiye, null, Proje);

        Assert.NotNull(hata);
        Assert.Contains("projesi de gönderilmelidir", hata);
    }

    [Fact]
    public void SantiyeBulunamazsa_Reddedilir()
    {
        var hata = MasrafMerkeziKurali.Dogrula(
            Proje, null, Santiye, null, null);

        Assert.NotNull(hata);
        Assert.Contains("şantiye bulunamadı", hata);
    }

    // ───────── Çoklu seçim ─────────

    [Fact]
    public void ProjeVeSubeBirlikte_Reddedilir()
    {
        var hata = MasrafMerkeziKurali.Dogrula(
            Proje, Sube, null, null, null);

        Assert.NotNull(hata);
        Assert.Contains("Tek bir masraf merkezi", hata);
    }

    // ───────── Açık kalan kapı: BİLEREK ─────────

    [Fact]
    public void KaydaBagliGorev_De_MERKEZSIZ_GECEMEZ()
    {
        /*
         * ═══ BU TEST TERSİNE ÇEVRİLDİ — KURAL-KATMAN/1 ═══
         *
         * Eski hâli `KaydaBagliGorev_MerkezsizGecer_ACIK_KAPI` idi ve
         * BİR KUSURU sabitliyordu: `SourceModule` dolu olan istek
         * kuralın dışında kalıyor, merkezsiz geçebiliyordu. Yorumunda
         * da yazıyordu: *"KURAL-KATMAN/1 geldiğinde DEĞİŞTİRİLECEK —
         * silinmeyecek, tersine çevrilecek."*
         *
         * Kapatıldı ve test tersine çevrildi. Artık kaynak modül adı
         * diye bir kavram kuralda YOK — parametre bile kaldırıldı, ki
         * biri günün birinde yeniden bir dizge kontrolü yazmasın.
         *
         * ÖLÇÜM KAPATMAYI GÜVENLİ KILDI: kaçışın gerekçesi olan
         * "hakediş/mal kabul üzerinden doğan görev" canlıda HİÇ
         * gerçekleşmemişti (`MANUAL × 2`, `(boş) × 1`). Kaçışı
         * kullanan tek şey ön yüzün kendi işaretiydi — tam olarak muaf
         * OLMAMASI gereken durum.
         */
        Assert.Equal(
            "Masraf merkezi zorunludur: proje, şube ya da şantiye seçin.",
            MasrafMerkeziKurali.Dogrula(null, null, null, null, null));
    }
}
