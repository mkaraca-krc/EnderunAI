using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Accounting;
using EnderunAI.Api.Services.Inventory;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ZİMMETTE GİDER KURALI — SAF KARAR, VERİTABANI YOK.
///
/// Karar ayrı bir sınıfa çıkarıldığı için doğrudan sürülebiliyor.
/// Kural akışın içine gömülü olsaydı, "dayanıklıda gider yazılmadı"
/// iddiasını kanıtlamak için bir zimmet akışı kurmak, fiş aramak ve
/// yokluğunu göstermek gerekirdi — yokluk kanıtlaması ise en kolay
/// yanılan test biçimi.
/// </summary>
public sealed class ZimmetGiderKuraliTests
{
    [Theory]
    [InlineData(InventoryItemType.Consumable)]
    [InlineData(InventoryItemType.Material)]
    [InlineData(InventoryItemType.SparePart)]
    public void TukenenTurler_GiderYazar(InventoryItemType tur) =>
        Assert.True(ZimmetGiderKurali.GiderYazilir(tur));

    [Fact]
    public void DayanikliTasinir_GiderYazmaz() =>
        Assert.False(ZimmetGiderKurali.GiderYazilir(InventoryItemType.Equipment));

    /// <summary>
    /// TANINMAYAN TÜR GİDER YAZMAZ.
    ///
    /// İki yanlıştan geri alınabilir olanı: gider yazılmadıysa
    /// sonradan yazılır; yazıldıysa muhasebe kaydı oluşmuştur ve
    /// düzeltmesi ters kayıt ister.
    ///
    /// Enum'a ileride yeni bir tür eklenirse bu satır onu yakalamaz —
    /// yakalaması da gerekmiyor: yeni tür varsayılan olarak KAPALI
    /// tarafa düşüyor, açık tarafa değil.
    /// </summary>
    [Fact]
    public void TaninmayanTur_GiderYazmaz() =>
        Assert.False(ZimmetGiderKurali.GiderYazilir((InventoryItemType)99));

    /// <summary>
    /// Gerekçe metni denetim kaydına yazılıyor: "neden gider
    /// yazılmadı" sorusunun cevabı kayıtta dursun.
    /// </summary>
    [Fact]
    public void Gerekce_KararlaTutarli()
    {
        Assert.Contains("gider yazıldı", ZimmetGiderKurali.Gerekce(InventoryItemType.Consumable));
        Assert.Contains("gider yazılmadı", ZimmetGiderKurali.Gerekce(InventoryItemType.Equipment));
        Assert.Contains("tanınmayan", ZimmetGiderKurali.Gerekce((InventoryItemType)99));
    }
}

/// <summary>
/// HESAP KODU HİYERARŞİSİ — kod parçalanarak üst hesap bulunuyor.
/// </summary>
public sealed class HesapKoduHiyerarsisiTests
{
    [Theory]
    [InlineData("150", null)]
    [InlineData("150.01", "150")]
    [InlineData("150.01.02", "150.01")]
    [InlineData("740.03.09.001", "740.03.09")]
    public void UstKod_KoddanTuretiliyor(string kod, string? beklenen) =>
        Assert.Equal(beklenen, HesapKoduHiyerarsisi.UstKod(kod));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BosKod_UstuYok(string? kod) =>
        Assert.Null(HesapKoduHiyerarsisi.UstKod(kod));

    /// <summary>
    /// Başta nokta olan bozuk kod ("...") üst hesap ÜRETMEZ.
    /// `LastIndexOf('.') <= 0` koşulu bunu kapatıyor; kapatmasaydı
    /// boş dizeli bir üst kod aranır ve "üst hesap yok" hatası
    /// anlamsız bir kodla raporlanırdı.
    /// </summary>
    [Fact]
    public void BastaNoktaliKod_UstHesapUretmez() =>
        Assert.Null(HesapKoduHiyerarsisi.UstKod(".150"));

    [Theory]
    [InlineData("150", 1)]
    [InlineData("150.01", 2)]
    [InlineData("150.01.02", 3)]
    public void Seviye_NoktaSayisindanBirFazla(string kod, int beklenen) =>
        Assert.Equal(beklenen, HesapKoduHiyerarsisi.Seviye(kod));
}
