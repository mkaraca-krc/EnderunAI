using System.Net;
using EnderunAI.Api.Security.Adres;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// İSTEMCİ ADRESİ ÇÖZÜCÜSÜ (VEKİL/2, 2026-09-17).
///
/// En önemli iki iddia:
///   1. `XFF_YALNIZ_VEKILDEN_GELINCE_OKUNUR` — doğrudan bağlanan biri
///      başlık yazarak kendini başka bir adres gibi gösteremez.
///   2. `VEKILSIZ_ISARETLENIR` — güvenilmeyen durumda sessizce geri
///      düşülmez, kayda işaret konur.
/// </summary>
public sealed class IstemciAdresCozucuTests
{
    private static HttpContext Baglam(string? uzakAdres, string? xff)
    {
        var ctx = new DefaultHttpContext();
        if (uzakAdres is not null)
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(uzakAdres);
        if (xff is not null)
            ctx.Request.Headers["X-Forwarded-For"] = xff;
        return ctx;
    }

    [Fact]
    public void VekildenGelince_SON_eleman_okunur()
    {
        // nginx gerçek eşi SONA ekler; ilk eleman istemcinin yazdığıdır.
        var s = IstemciAdresCozucu.Coz(Baglam("127.0.0.1", "9.9.9.9, 78.175.232.135"));
        Assert.Equal("78.175.232.135", s.Adres);
        Assert.True(s.VekildenGeldi);
        Assert.Equal("78.175.232.135", s.KayitIcin);
    }

    [Fact]
    public void ILK_eleman_ASLA_okunmaz()
    {
        // 2026-09-15 ölçümü: ilk elemanı almak hız sınırını istemcinin
        // kontrolüne bırakıyordu.
        var s = IstemciAdresCozucu.Coz(Baglam("127.0.0.1", "9.9.9.9, 1.2.3.4"));
        Assert.NotEqual("9.9.9.9", s.Adres);
    }

    [Fact]
    public void XFF_YALNIZ_VEKILDEN_GELINCE_OKUNUR()
    {
        // Doğrudan bağlanan biri (vekil değil) başlık yazsa da OKUNMAZ.
        var s = IstemciAdresCozucu.Coz(Baglam("203.0.113.9", "9.9.9.9"));
        Assert.Equal("203.0.113.9", s.Adres);
        Assert.False(s.VekildenGeldi);
        Assert.DoesNotContain("9.9.9.9", s.KayitIcin);
    }

    [Fact]
    public void VEKILSIZ_ISARETLENIR_sessiz_geri_dusus_yok()
    {
        var s = IstemciAdresCozucu.Coz(Baglam("203.0.113.9", null));
        Assert.Equal("203.0.113.9 (vekilsiz)", s.KayitIcin);
    }

    [Fact]
    public void IPv6_esmeli_loopback_de_vekil_sayilir()
    {
        var s = IstemciAdresCozucu.Coz(Baglam("::ffff:127.0.0.1", "5.25.151.146"));
        Assert.Equal("5.25.151.146", s.Adres);
        Assert.True(s.VekildenGeldi);
    }

    [Fact]
    public void Vekilden_geldi_ama_baslik_yok_adres_baglantidan_alinir()
    {
        // Vekil başlığı iletmiyorsa adres bağlantıdan gelir; ama istek
        // GERÇEKTEN vekilden geldiği için `(vekilsiz)` işareti konmaz —
        // eksik olan başlıktır, güven değil.
        var s = IstemciAdresCozucu.Coz(Baglam("127.0.0.1", null));
        Assert.Equal("127.0.0.1", s.Adres);
        Assert.True(s.VekildenGeldi);
        Assert.Equal("127.0.0.1", s.KayitIcin);
    }

    [Fact]
    public void Baglam_yoksa_unknown_ve_vekilsiz()
    {
        var s = IstemciAdresCozucu.Coz(null);
        Assert.Equal("unknown", s.Adres);
        Assert.False(s.VekildenGeldi);
    }
}
