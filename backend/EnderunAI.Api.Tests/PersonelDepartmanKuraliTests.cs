using EnderunAI.Api.Services.Common;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// PERSONEL DEPARTMAN KURALI — SAF, VERİTABANISIZ.
///
/// Bu dosya kuralın DOĞRU olduğunu gösteriyor; kuralın gerçekten
/// ÇAĞRILDIĞI `PersonelDepartmanUcuTests` içinde ölçülüyor. İkisi ayrı
/// iddia — doğru bir kural hiç çağrılmadan da yeşil kalır
/// (`2d90c946`).
/// </summary>
public sealed class PersonelDepartmanKuraliTests
{
    private static readonly Guid Sirket = Guid.NewGuid();
    private static readonly Guid BaskaSirket = Guid.NewGuid();
    private static readonly Guid Departman = Guid.NewGuid();

    [Fact]
    public void DepartmandanCikarma_Kabul()
    {
        /*
         * `null` BİR HATA DEĞİL, BİR KARAR: "bu personel hiçbir
         * departmana bağlı değil". Reddedilseydi yanlış atanan bir
         * personel düzeltilemezdi.
         */
        Assert.Null(PersonelDepartmanKurali.Dogrula(
            departmanId: null,
            departmanVarMi: false,
            departmanAktifMi: false,
            departmanSirketId: null,
            personelSirketId: Sirket));
    }

    [Fact]
    public void OlmayanDepartman_Reddedilir()
    {
        var hata = PersonelDepartmanKurali.Dogrula(
            Departman, departmanVarMi: false, departmanAktifMi: false,
            departmanSirketId: null, personelSirketId: Sirket);

        Assert.Equal("Seçilen departman bulunamadı.", hata);
    }

    [Fact]
    public void PasifDepartman_Reddedilir()
    {
        var hata = PersonelDepartmanKurali.Dogrula(
            Departman, departmanVarMi: true, departmanAktifMi: false,
            departmanSirketId: Sirket, personelSirketId: Sirket);

        Assert.Equal("Seçilen departman aktif değil; personel atanamaz.", hata);
    }

    [Fact]
    public void BaskaSirketinDepartmani_Reddedilir()
    {
        /*
         * BU KONTROL BUGÜN HİÇBİR İSTEĞİ REDDETMİYOR: canlıda tek
         * şirket var. Ama tek şirketli olmak bir GARANTİ değil, bir
         * DURUM — ve iki bağlam arasında yabancı anahtar olmadığı için
         * veritabanı bu bağı doğrulamıyor. Kontrol yoksa, ikinci
         * şirket açıldığı gün bir şirketin personeli diğerinin
         * departmanına (ve o departmanın mesaj kanalına) düşer.
         */
        var hata = PersonelDepartmanKurali.Dogrula(
            Departman, departmanVarMi: true, departmanAktifMi: true,
            departmanSirketId: BaskaSirket, personelSirketId: Sirket);

        Assert.Equal("Departman başka bir şirkete ait; personel atanamaz.", hata);
    }

    [Fact]
    public void GecerliDepartman_Kabul_POZITIF_KONTROL()
    {
        /*
         * POZİTİF KONTROL: yukarıdaki dört test, kural HER ŞEYİ
         * reddetse de yeşil kalırdı.
         */
        Assert.Null(PersonelDepartmanKurali.Dogrula(
            Departman, departmanVarMi: true, departmanAktifMi: true,
            departmanSirketId: Sirket, personelSirketId: Sirket));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SirketKontrolu_VarlikVeAktiflikten_SONRA_Olculur(bool aktif)
    {
        /*
         * SIRA TESPİTİ: var olmayan bir departman için "başka şirkete
         * ait" demek yanlış bilgi olurdu — kullanıcı var olmayan bir
         * kaydın şirketini aramaya çıkardı. Kontrollerin sırası
         * davranışın parçası, bu yüzden sabitleniyor.
         */
        var hata = PersonelDepartmanKurali.Dogrula(
            Departman, departmanVarMi: false, departmanAktifMi: aktif,
            departmanSirketId: BaskaSirket, personelSirketId: Sirket);

        Assert.Equal("Seçilen departman bulunamadı.", hata);
    }

    [Fact]
    public void AyniDepartman_DegisiklikSayilmaz()
    {
        /*
         * TARİHÇENİN ANLAMI BUNA BAĞLI: aynı departmanı ikinci kez
         * göndermek bir geçiş değildir. Kaydedilseydi tarihçe hiç
         * olmamış değişikliklerle dolar ve M3'ün "ayrıldığı tarihe
         * kadarki geçmiş" hesabı yanlış cevaplar üretirdi.
         */
        Assert.False(PersonelDepartmanKurali.DegisiklikMi(Departman, Departman));
        Assert.False(PersonelDepartmanKurali.DegisiklikMi(null, null));
    }

    [Fact]
    public void FarkliDepartman_DegisiklikSayilir_POZITIF_KONTROL()
    {
        Assert.True(PersonelDepartmanKurali.DegisiklikMi(null, Departman));
        Assert.True(PersonelDepartmanKurali.DegisiklikMi(Departman, null));
        Assert.True(PersonelDepartmanKurali.DegisiklikMi(Departman, BaskaSirket));
    }
}
