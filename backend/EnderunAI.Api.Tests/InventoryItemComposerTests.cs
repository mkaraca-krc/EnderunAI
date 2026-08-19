using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Inventory;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// AD VE MÜKERRER İMZASI ÜRETİCİSİ — saf, veritabanısız birim testleri.
///
/// NEDEN AYRI DOSYA VE NEDEN DOĞRUDAN: uçtan uca test bu kuralları
/// GİZLİYORDU. Kontrolcü seçenekleri kategori sırasına göre topladığı
/// için besleme sırası zaten normalleşiyor; sıralama mantığını
/// kaldırdığımda uçtan uca test hâlâ geçti (sonda KAÇIRDI).
///
/// Yani üretici içindeki sıralama, uç üzerinden ölçüldüğünde "yük
/// taşımıyor" görünüyordu. Ama o normalleştirme kontrolcünün tesadüfi
/// bir davranışı — yarın toplama biçimi değişirse imza seçim sırasına
/// bağlı hale gelir ve aynı malzeme ikinci kez açılabilir.
///
/// Bu testler kuralı KAYNAĞINDA sabitliyor.
/// </summary>
public sealed class InventoryItemComposerTests
{
    private static InventoryItemComposer.SelectedAttribute Attr(
        string code, int sortOrder, string value, string? display = null) =>
        new(code, sortOrder, value, display ?? value);

    /// <summary>
    /// İMZA SEÇİM SIRASINDAN BAĞIMSIZ. Aksi hâlde kullanıcı özellikleri
    /// farklı sırayla seçerek mükerrer engelini delerdi.
    /// </summary>
    [Fact]
    public void Imza_SecimSirasindanBagimsiz()
    {
        var duz = new[]
        {
            Attr("OLCU", 10, "200"),
            Attr("KALINLIK", 20, "1.5"),
            Attr("CINS", 30, "Perfore"),
            Attr("KAPLAMA", 40, "Paslanmaz")
        };

        var ters = duz.Reverse().ToArray();
        var karisik = new[] { duz[2], duz[0], duz[3], duz[1] };

        var beklenen = InventoryItemComposer.BuildSignature("KABLO_TAVASI", duz);

        Assert.Equal(beklenen, InventoryItemComposer.BuildSignature("KABLO_TAVASI", ters));
        Assert.Equal(beklenen, InventoryItemComposer.BuildSignature("KABLO_TAVASI", karisik));
    }

    /// <summary>
    /// İMZA GÖSTERİME DEĞİL DEĞERE DAYANIR. Gösterim metni değişirse
    /// ("200" → "200mm") aynı malzeme yeniden açılabilir hâle
    /// gelmemeli.
    /// </summary>
    [Fact]
    public void Imza_GosterimDegisseDeAyniKalir()
    {
        var once = new[] { Attr("OLCU", 10, "200", "200") };
        var sonra = new[] { Attr("OLCU", 10, "200", "200mm") };

        Assert.Equal(
            InventoryItemComposer.BuildSignature("KABLO_TAVASI", once),
            InventoryItemComposer.BuildSignature("KABLO_TAVASI", sonra));
    }

    [Fact]
    public void Imza_FarkliDegerFarkliImza()
    {
        var a = new[] { Attr("OLCU", 10, "200") };
        var b = new[] { Attr("OLCU", 10, "300") };

        Assert.NotEqual(
            InventoryItemComposer.BuildSignature("KABLO_TAVASI", a),
            InventoryItemComposer.BuildSignature("KABLO_TAVASI", b));
    }

    /// <summary>
    /// AD ÖZELLİK SIRASINA göre dizilir — seçim sırasına DEĞİL. Aksi
    /// hâlde aynı malzeme, kullanıcının doldurma sırasına göre farklı
    /// adlar alırdı.
    /// </summary>
    [Fact]
    public void Ad_OzellikSirasinaGoreDizilir()
    {
        var karisik = new[]
        {
            Attr("KAPLAMA", 40, "Paslanmaz", "Paslanmaz"),
            Attr("OLCU", 10, "200", "200mm"),
            Attr("CINS", 30, "Perfore", "Perfore"),
            Attr("KALINLIK", 20, "1.5", "1.5mm")
        };

        var ad = InventoryItemComposer.BuildName("Kablo Tavası", karisik);

        Assert.Equal("Kablo Tavası 200mm 1.5mm Perfore Paslanmaz", ad);
    }

    [Fact]
    public void Ad_GosterimYoksaDegerKullanilir()
    {
        var ad = InventoryItemComposer.BuildName("Pano",
            [Attr("TIP", 10, "Dağıtım"), Attr("SIRA", 20, "54")]);

        Assert.Equal("Pano Dağıtım 54", ad);
    }

    /// <summary>
    /// SERBEST kategoride imza üretilmez — her ürün tekildir.
    /// </summary>
    [Fact]
    public void SerbestKategori_ImzaGerektirmez()
    {
        Assert.True(InventoryItemComposer.RequiresSignature(InventoryCategoryKind.Standard));
        Assert.False(InventoryItemComposer.RequiresSignature(InventoryCategoryKind.Free));
    }
}
