using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Accounting;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ÇEK DURUM KURALLARI — SAF TESTLER.
///
/// Kural iki yerden çağrılıyor (liste süzgeci + satır bayrağı) ve
/// tek yerde yaşıyor. İki yere gömülseydi ÇEK/1'de yakalanan hatanın
/// aynısı olurdu: sunucu ile ekran aynı soruya farklı cevap verir.
/// </summary>
public sealed class ChequeStatusRulesTests
{
    /// <summary>
    /// AÇIK KÜME TAM OLARAK DÖRT DURUM.
    ///
    /// Sayımlandırma büyüdüğünde yeni durumun açık mı kapalı mı
    /// olduğuna KARAR VERİLMESİ gerekir; sessizce kapanmış sayılıp
    /// listeden düşmesi, çekin kaybolması demektir. Bu test yeni
    /// durum eklendiğinde kırmızıya döner ve kararı zorlar.
    /// </summary>
    [Fact]
    public void AcikKume_TamOlarakDortDurum()
    {
        Assert.Equal(
            new[]
            {
                ChequeStatus.Portfolio,
                ChequeStatus.AtBank,
                ChequeStatus.AtFactoring,
                ChequeStatus.Issued
            },
            ChequeStatusRules.AcikDurumlar);
    }

    [Theory]
    [InlineData(ChequeStatus.Portfolio)]
    [InlineData(ChequeStatus.AtBank)]
    [InlineData(ChequeStatus.AtFactoring)]
    [InlineData(ChequeStatus.Issued)]
    public void AcikDurumlar_AcikSayilir(ChequeStatus durum) =>
        Assert.True(ChequeStatusRules.AcikMi(durum));

    /// <summary>
    /// KAPANMIŞ DURUMLAR — ÖDENEN ÇEK BURADA.
    ///
    /// `Paid` şikayetin kendisi: ödenmiş çek varsayılan listede
    /// duruyordu. `Bounced` kararla kapanmış sayıldı (alacak cariye
    /// döndü, orada izleniyor).
    /// </summary>
    [Theory]
    [InlineData(ChequeStatus.Collected)]
    [InlineData(ChequeStatus.Bounced)]
    [InlineData(ChequeStatus.Paid)]
    [InlineData(ChequeStatus.Returned)]
    [InlineData(ChequeStatus.Replaced)]
    [InlineData(ChequeStatus.Voided)]
    public void KapanmisDurumlar_AcikSayilmaz(ChequeStatus durum) =>
        Assert.False(ChequeStatusRules.AcikMi(durum));

    /// <summary>
    /// TOPLAMA YALNIZ İPTAL GİRMEZ.
    ///
    /// Ödenen çek toplama GİRER — ama varsayılan listede olmadığı
    /// için varsayılan toplamda da görünmez. Kullanıcı "Ödendi"
    /// süzgecini seçtiğinde hem satırları hem toplamı görmeli;
    /// dolu liste + sıfır toplam anlamsız bir ekran olurdu.
    /// </summary>
    [Theory]
    [InlineData(ChequeStatus.Portfolio)]
    [InlineData(ChequeStatus.Issued)]
    [InlineData(ChequeStatus.Paid)]
    [InlineData(ChequeStatus.Bounced)]
    public void IptalDisindakiHerDurum_ToplamaGirer(ChequeStatus durum) =>
        Assert.True(ChequeStatusRules.ToplamaGirer(durum));

    [Fact]
    public void Iptal_ToplamaGirmez() =>
        Assert.False(ChequeStatusRules.ToplamaGirer(ChequeStatus.Voided));

    private static ChequeStatusRules.ListeDurumIstegi Istek(
        ChequeStatus? secilen = null,
        bool kapanmislar = false,
        bool iptaller = false) =>
        new(secilen, kapanmislar, iptaller);

    /// <summary>
    /// VARSAYILAN: YALNIZ AÇIK ÇEKLER.
    ///
    /// Şikayetin kendisi: ödenen çek varsayılan listede duruyordu.
    /// </summary>
    [Fact]
    public void Varsayilan_YalnizAciklar() =>
        Assert.Equal(ChequeStatusRules.AcikDurumlar,
            ChequeStatusRules.CozumleDurumKumesi(Istek()));

    /// <summary>
    /// AÇIK SEÇİM HER ŞEYİ EZER — ÖNCELİK KURALI.
    ///
    /// Kullanıcı "Ödendi" seçtiyse ödenmişleri ister. Varsayılanın onu
    /// elemesi, kullanıcıya "istediğin şeyi göremezsin" demek olurdu.
    /// Varsayılan süzgeç bir kolaylıktır; açık istek geldiğinde susar.
    /// </summary>
    [Theory]
    [InlineData(ChequeStatus.Paid)]
    [InlineData(ChequeStatus.Collected)]
    [InlineData(ChequeStatus.Bounced)]
    [InlineData(ChequeStatus.Voided)]
    public void AcikSecim_VarsayilaniEzer(ChequeStatus secilen) =>
        Assert.Equal([secilen],
            ChequeStatusRules.CozumleDurumKumesi(Istek(secilen: secilen)));

    /// <summary>
    /// İPTAL BAYRAĞI TEK BAŞINA ÇALIŞIR — ÇARPIŞMA YOK.
    ///
    /// ÇEK/1'de iki süzgeç VE ile birleşiyordu ve dar olan sessizce
    /// kazanıyordu: "iptalleri göster" denmesine rağmen açık süzgeci
    /// iptali eliyor, ekran boş geliyordu. Bu test o çarpışmanın geri
    /// gelmediğini sabitliyor.
    /// </summary>
    [Fact]
    public void IptalBayragi_AciklaraEklenir()
    {
        var kume = ChequeStatusRules.CozumleDurumKumesi(Istek(iptaller: true));

        Assert.Contains(ChequeStatus.Voided, kume);
        Assert.Contains(ChequeStatus.Issued, kume);
        Assert.DoesNotContain(ChequeStatus.Paid, kume);
    }

    /// <summary>
    /// KAPANMIŞLAR BAYRAĞI İPTALİ GETİRMEZ — İKİSİ AYRI KAPI.
    ///
    /// İptal de kapanmıştır ama kendi bayrağı var: "ödenmişleri
    /// göreyim" demek "iptalleri de göreyim" demek değildir.
    /// </summary>
    [Fact]
    public void KapanmislarBayragi_IptaliGetirmez()
    {
        var kume = ChequeStatusRules.CozumleDurumKumesi(Istek(kapanmislar: true));

        Assert.Contains(ChequeStatus.Paid, kume);
        Assert.Contains(ChequeStatus.Bounced, kume);
        Assert.DoesNotContain(ChequeStatus.Voided, kume);
    }

    /// <summary>İki bayrak birlikte: her şey gelir.</summary>
    [Fact]
    public void IkiBayrakBirlikte_HerSeyGelir()
    {
        var kume = ChequeStatusRules.CozumleDurumKumesi(
            Istek(kapanmislar: true, iptaller: true));

        Assert.Equal(Enum.GetValues<ChequeStatus>().Length, kume.Distinct().Count());
    }
}
