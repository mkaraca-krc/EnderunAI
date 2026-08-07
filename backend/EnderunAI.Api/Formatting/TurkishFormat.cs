using System.Globalization;

namespace EnderunAI.Api.Formatting;

/// <summary>
/// Kullanıcıya gösterilen Türkçe metinlerdeki sayı biçimi.
///
/// NEDEN GEREKLİ: sunucuda kültür ayarlı değil, yani varsayılan
/// invariant kültür geçerli. Türkçe bir cümlenin içine ham bir sayı
/// biçimi yazıldığında "60,000.00" çıkıyor ve bunu okuyan kullanıcı
/// ALTMIŞ olarak anlıyor — binlik ile ondalık ayıracı yer değiştirmiş
/// oluyor. Tutarın bin katı yanlış okunması, hakediş onayında ya da
/// fatura kontrolünde geri dönüşü zor bir hata.
///
/// KÜLTÜRÜ GLOBAL DEĞİŞTİRMEK ÇÖZÜM DEĞİL; üç ayrı yerde zarar verirdi:
/// 1. Sayı AYRIŞTIRAN kodlar (Excel içe aktarma, e-fatura okuma) aynı
///    metni farklı okumaya başlardı.
/// 2. ToLower() Türkçe kültürde "I" harfini "ı" yapar; arama filtreleri
///    sessizce farklı sonuç verirdi.
/// 3. Tarih ayrıştırma davranışı değişirdi.
///
/// Bu yüzden biçim, gösterildiği yerde AÇIKÇA veriliyor.
///
/// YALNIZCA GÖSTERİM İÇİNDİR. Makineye giden hiçbir yerde (dosya adı,
/// kod üretimi, UBL/XML, dış servis çağrısı, CSV) kullanılmamalı —
/// oralarda invariant kültür doğrudur.
/// </summary>
public static class TurkishFormat
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Tutar: iki ondalık, binlik ayıraçlı — "60.000,00".</summary>
    public static string Amount(decimal value) => value.ToString("N2", Turkish);

    /// <summary>Oran değeri; yüzde işaretini çağıran koyar — "5,50".</summary>
    public static string Rate(decimal value) => value.ToString("N2", Turkish);

    /// <summary>Miktar: dört ondalık — "1.250,7500".</summary>
    public static string Quantity(decimal value) => value.ToString("N4", Turkish);

    /// <summary>Ondalıksız sayı — "320".</summary>
    public static string Whole(decimal value) => value.ToString("N0", Turkish);

    /// <summary>Ondalık basamağı çağıran belirler.</summary>
    public static string Number(decimal value, int decimals) =>
        value.ToString($"N{decimals}", Turkish);
}
