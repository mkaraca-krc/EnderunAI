namespace EnderunAI.Api.Models;

/// <summary>
/// Şirketin yıl bazlı kurumlar vergisi oranı.
///
/// Yıl bazlı, çünkü oran mevzuatla değişiyor ve geçmiş yılın tahmini
/// geriye dönük olarak bugünkü oranla yeniden hesaplanmamalı.
///
/// Koda gömülü varsayılan YOK: oran girilmemişse tahmin üretilmez ve
/// ekran bunu söyler. Daha önce hesap sessizce %25'e düşüyordu —
/// doğru olabilirdi ama kimse girmediği için doğru olduğu
/// bilinmiyordu; ayarlanabilir görünüp ayarlanamıyordu.
/// </summary>
public sealed class CompanyCorporateTaxRate : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Oranın geçerli olduğu hesap dönemi.</summary>
    public int Year { get; set; }

    /// <summary>Kurumlar vergisi oranı (%).</summary>
    public decimal Rate { get; set; }

    /// <summary>Oranın dayanağı (kanun/tebliğ no, tarih).</summary>
    public string? Note { get; set; }
}
