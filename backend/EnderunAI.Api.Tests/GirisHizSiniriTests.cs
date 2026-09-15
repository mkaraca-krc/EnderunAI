using EnderunAI.Api.Security;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// GİRİŞ HIZ SINIRI — İKİ AYRI SAYAÇ, SÜRELİ KİLİT, CÖMERT EŞİK.
///
/// ═══ ÖLÇÜLEN OLAY (2026-09-15) ═══
///
/// 16 günde `/login` sayfasına 109.495 istek geldi. Saldırı kümesi
/// (185.177.x.x, 6 IP) gerçek giriş ucuna **hiç dokunmadı** — POST'ları
/// `/login` (404) ve PHP sömürü yollarına gitti. Gerçek uca gelen 96
/// POST'un 71'i BAŞARILI ve hepsi bizim kullanıcılarımızın adreslerinden.
///
/// Yine de sınır sertleştirildi: bugün tarayıcı olan yarın deneme yapar.
///
/// ═══ POZİTİF KONTROL ZORUNLU ═══
///
/// Sınır yanlış kurulursa geçiş gününde personeli kilitler. Bu yüzden
/// ilk test "normal kullanıcı engellenmiyor" der — o geçmeden diğerleri
/// bir şey kanıtlamaz.
/// </summary>
public sealed class GirisHizSiniriTests
{
    [Fact]
    public void NormalGirisDizisi_TAKILMAZ_POZITIF_KONTROL()
    {
        var servis = new LoginAttemptService();
        const string ip = "ip:203.0.113.1";
        const string kul = "kul:ahmet";

        // Yanlış parola, yanlış parola, sonra DOĞRU parola.
        servis.RecordFailure(ip);
        servis.RecordFailure(kul, esik: 10);
        servis.RecordFailure(ip);
        servis.RecordFailure(kul, esik: 10);

        Assert.False(servis.IsLocked(ip, out _), "İki yanlış denemede IP kilitlendi — geçiş gününde personeli kilitler.");
        Assert.False(servis.IsLocked(kul, out _), "İki yanlış denemede kullanıcı kilitlendi.");

        // Doğru parola: iki sayaç da sıfırlanır.
        servis.RecordSuccess(ip);
        servis.RecordSuccess(kul);

        Assert.False(servis.IsLocked(ip, out _));
        Assert.False(servis.IsLocked(kul, out _));
    }

    [Fact]
    public void IpEsigi_AsilincaKilitlenir()
    {
        var servis = new LoginAttemptService();
        const string ip = "ip:203.0.113.2";

        for (var i = 0; i < 5; i++)
        {
            Assert.False(servis.IsLocked(ip, out _), $"{i}. denemede erken kilitlendi.");
            servis.RecordFailure(ip);
        }

        Assert.True(servis.IsLocked(ip, out var kalan), "5 başarısız denemeden sonra IP kilitlenmedi.");
        Assert.True(kalan > TimeSpan.Zero, "Kilit süresi yok — kalıcı kilit olmamalı ama süre de olmalı.");
        Assert.True(kalan <= TimeSpan.FromMinutes(15), $"Kilit 15 dakikadan uzun: {kalan}");
    }

    [Fact]
    public void KullaniciAdiEsigi_DAHA_COMERT()
    {
        var servis = new LoginAttemptService();
        const string kul = "kul:mehmet";

        // IP eşiği 5; kullanıcı adı eşiği 10 — aradaki fark BİLEREK.
        for (var i = 0; i < 9; i++)
        {
            servis.RecordFailure(kul, esik: 10);
            Assert.False(servis.IsLocked(kul, out _), $"{i + 1}. denemede kilitlendi — kullanıcı eşiği cömert olmalı.");
        }

        servis.RecordFailure(kul, esik: 10);
        Assert.True(servis.IsLocked(kul, out _), "10 denemeden sonra kullanıcı adı kilitlenmedi.");
    }

    [Fact]
    public void IkiSayac_BIRBIRINDEN_BAGIMSIZ()
    {
        var servis = new LoginAttemptService();

        for (var i = 0; i < 5; i++)
        {
            servis.RecordFailure("ip:203.0.113.3");
        }

        Assert.True(servis.IsLocked("ip:203.0.113.3", out _));

        // Aynı kullanıcı BAŞKA bir IP'den girebilmeli: IP kilidi
        // kullanıcıyı kilitlemez.
        Assert.False(servis.IsLocked("kul:ayse", out _));
        Assert.False(servis.IsLocked("ip:203.0.113.4", out _));
    }

    [Fact]
    public void KilitKALICI_DEGIL_SureliOlmali()
    {
        var servis = new LoginAttemptService();
        const string ip = "ip:203.0.113.5";

        for (var i = 0; i < 5; i++)
        {
            servis.RecordFailure(ip);
        }

        Assert.True(servis.IsLocked(ip, out var kalan));

        // KALICI KİLİT, saldırganın elinde hizmet engelleme aracıdır:
        // kullanıcı adını bilen herkes o hesabı kapatabilirdi.
        Assert.True(kalan.TotalMinutes is > 0 and <= 15,
            $"Kilit süresi makul değil: {kalan}. Kalıcı kilit YASAK.");
    }
}
