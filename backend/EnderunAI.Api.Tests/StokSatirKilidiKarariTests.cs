using EnderunAI.Api.Services.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// TAZELEME KARARI — SAF TESTLER.
///
/// NEDEN BURADA: bu karar önce iki ayrı bariyer olarak koda gömülüydü
/// ve sondada İKİSİ DE tek tek kanıtlanamadı. Birini kaldırınca diğeri
/// sonucu aynı tutuyor, test yeşil kalıyordu — yani yeşil hiçbir şey
/// söylemiyordu (Kural 25). Karar saf fonksiyona çıkarıldı; artık her
/// koşul doğrudan sınanıyor ve sabotajı kaçmıyor.
/// </summary>
public sealed class StokSatirKilidiKarariTests
{
    /// <summary>
    /// İLK KİLİT + DOKUNULMAMIŞ KAYIT → TAZELENİR.
    ///
    /// Tek gerçek tazeleme hâli: satır bu işlemde ilk kez kilitlendi
    /// ve üzerinde bekleyen değişiklik yok. Kilitten sonra taze
    /// miktarı okumazsak kilit hiçbir şey korumaz — EF, kimlik
    /// haritasındaki bayat nesneyi döndürür.
    /// </summary>
    [Fact]
    public void IlkKilitVeDokunulmamisKayit_Tazelenir()
    {
        Assert.True(StokSatirKilidiKarari.TazelenmeliMi(
            ilkKilit: true, EntityState.Unchanged));
    }

    /// <summary>
    /// İKİNCİ KİLİT → TAZELENMEZ.
    ///
    /// Aynı kalem faturada iki satırsa ikinci satır birincinin
    /// kaydedilmemiş düşüşünü geri alırdı: 5 stoktan 2+2 çıkınca
    /// 3 yerine 1 kalırdı. Koruma, korumak istediği hatayı üretirdi.
    /// </summary>
    [Theory]
    [InlineData(EntityState.Unchanged)]
    [InlineData(EntityState.Modified)]
    public void IkinciKilit_Tazelenmez(EntityState durum)
    {
        Assert.False(StokSatirKilidiKarari.TazelenmeliMi(
            ilkKilit: false, durum));
    }

    /// <summary>
    /// DEĞİŞMİŞ KAYIT → TAZELENMEZ, İLK KİLİT OLSA BİLE.
    ///
    /// Kayıt kilitlenmeden önce değiştirilmişse üzerinde bu işlemin
    /// kaydedilmemiş değişikliği vardır; tazeleme onu siler. Bu,
    /// tekrar-engelleyicinin KAPSAMADIĞI ayrı bir hâl — iki koşul
    /// birbirinin yedeği değil.
    /// </summary>
    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Deleted)]
    public void DegismisKayit_Tazelenmez(EntityState durum)
    {
        Assert.False(StokSatirKilidiKarari.TazelenmeliMi(
            ilkKilit: true, durum));
    }

    /// <summary>
    /// İZLENEN KAYIT YOKSA → TAZELENECEK BİR ŞEY YOK.
    ///
    /// Satır bu bağlamda henüz okunmamış; kilitten sonraki sorgu
    /// zaten veritabanına gidecek.
    /// </summary>
    [Fact]
    public void IzlenenKayitYok_Tazelenmez()
    {
        Assert.False(StokSatirKilidiKarari.TazelenmeliMi(
            ilkKilit: true, izlenenDurum: null));
    }
}
