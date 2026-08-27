using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Accounting;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// VERİLEN ÇEK KASADAN ÖDENMEZ — saf kural testleri (ÇEK/2).
///
/// Kuralın DAR olması gerekiyor: alınan çekin elden tahsili gerçek bir
/// akıştır ve kasaya girer. Kuralı "çek işlemlerinde kasa olmaz" diye
/// genişletmek, çalışan bir akışı kapatırdı.
/// </summary>
public sealed class CekOdemeHesabiKuraliTests
{
    [Fact]
    public void VerilenCekOdemesinde_KasaReddedilir()
    {
        Assert.False(CekOdemeHesabiKurali.Uygun(
            ChequeDirection.Issued, ChequeStatus.Issued, ChequeStatus.Paid,
            CashAccountType.Cash));
    }

    [Fact]
    public void VerilenCekOdemesinde_BankaKabulEdilir()
    {
        Assert.True(CekOdemeHesabiKurali.Uygun(
            ChequeDirection.Issued, ChequeStatus.Issued, ChequeStatus.Paid,
            CashAccountType.Bank));
    }

    /// <summary>
    /// ALINAN ÇEĞİN TAHSİLİ KASAYA GİREBİLİR — kural buraya taşmıyor.
    /// </summary>
    [Fact]
    public void AlinanCekTahsilinde_KasaSerbest()
    {
        Assert.True(CekOdemeHesabiKurali.Uygun(
            ChequeDirection.Received, ChequeStatus.Portfolio, ChequeStatus.Collected,
            CashAccountType.Cash));

        Assert.True(CekOdemeHesabiKurali.Uygun(
            ChequeDirection.Received, ChequeStatus.AtBank, ChequeStatus.Collected,
            CashAccountType.Cash));
    }

    /// <summary>
    /// VERİLEN ÇEĞİN BAŞKA GEÇİŞLERİ KISITLANMIYOR — yalnız
    /// "Verildi → Ödendi" konuşuluyor.
    /// </summary>
    [Fact]
    public void VerilenCeginBaskaGecisinde_KasaSerbest()
    {
        Assert.True(CekOdemeHesabiKurali.Uygun(
            ChequeDirection.Issued, ChequeStatus.Issued, ChequeStatus.Bounced,
            CashAccountType.Cash));
    }

    /// <summary>Hesap seçilmemişse bu kuralın söyleyeceği bir şey yok.</summary>
    [Fact]
    public void HesapYoksa_KuralSusar()
    {
        Assert.True(CekOdemeHesabiKurali.Uygun(
            ChequeDirection.Issued, ChequeStatus.Issued, ChequeStatus.Paid, null));
    }

    /// <summary>
    /// KURALIN KAPSADIĞI GEÇİŞ TEK. Kapsamın sessizce genişlemesi,
    /// kasadan tahsili olan gerçek akışları kapatırdı.
    /// </summary>
    [Fact]
    public void KapsananGecis_YalnizVerilenCekOdemesi()
    {
        var kapsanan = new List<string>();

        foreach (var yon in Enum.GetValues<ChequeDirection>())
        foreach (var from in Enum.GetValues<ChequeStatus>())
        foreach (var to in Enum.GetValues<ChequeStatus>())
        {
            if (!CekOdemeHesabiKurali.Uygun(yon, from, to, CashAccountType.Cash))
                kapsanan.Add($"{yon}:{from}->{to}");
        }

        Assert.Equal(new[] { "Issued:Issued->Paid" }, kapsanan.ToArray());
    }
}
