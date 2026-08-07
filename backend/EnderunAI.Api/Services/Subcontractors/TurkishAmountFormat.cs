using System.Globalization;

namespace EnderunAI.Api.Services.Subcontractors;

/// <summary>
/// Kullanıcıya gösterilen Türkçe metinlerdeki tutar biçimi.
///
/// NEDEN GEREKLİ: sunucuda kültür ayarlı değil, yani varsayılan
/// invariant kültür geçerli. Türkçe bir cümlenin içine
/// <c>{tutar:N2}</c> yazıldığında "60,000.00" çıkıyor ve bunu okuyan
/// bir kullanıcı ALTMIŞ olarak anlıyor — binlik ile ondalık ayıracı yer
/// değiştirmiş oluyor. Tutarın bin katı yanlış okunması, hakediş
/// onayında geri dönüşü zor bir hata.
///
/// Kültürü global olarak değiştirmek yerine burada açıkça vermek
/// bilinçli: global ayar, sayı ayrıştıran (Excel içe aktarma, e-fatura)
/// kodların davranışını da sessizce değiştirirdi.
/// </summary>
public static class TurkishAmountFormat
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>İki ondalıklı, binlik ayıraçlı tutar: "60.000,00".</summary>
    public static string Amount(decimal value) =>
        value.ToString("N2", Turkish);

    /// <summary>Yüzde işaretsiz oran: "5,50".</summary>
    public static string Rate(decimal value) =>
        value.ToString("N2", Turkish);

    /// <summary>Ondalıksız miktar: "320".</summary>
    public static string Count(decimal value) =>
        value.ToString("N0", Turkish);
}
