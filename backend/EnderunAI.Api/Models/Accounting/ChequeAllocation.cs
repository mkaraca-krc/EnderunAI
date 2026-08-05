namespace EnderunAI.Api.Models;

/// <summary>
/// Bir çekin proje/masraf merkezi kırılımı. Tek çek birden fazla
/// şantiyenin ya da Merkez'in ödemesini karşılayabilir; muhasebe fişinin
/// cari tarafı bu satırlara göre bölünür.
///
/// Tercih edilen kullanım FATURA BAĞLANTISIDIR: çek hangi faturaları
/// ödüyorsa onlar seçilir, proje ve masraf merkezi faturadan türetilir.
/// Böylece dağılım tahmin değil, belgeye dayanan bir gerçek olur ve
/// "hangi fatura ödendi" sorusu da aynı kayıttan cevaplanır.
///
/// Elle dağılım (tutarla bölme) da mümkündür; faturası henüz girilmemiş
/// ödemeler için gereklidir.
///
/// Yüzde ile bölme yalnızca ekranda bir kolaylıktır: burada YALNIZCA
/// tutar saklanır. Yüzde de saklansaydı iki kaynak oluşur ve yuvarlama
/// yüzünden ikisi zamanla birbirini tutmazdı.
/// </summary>
public sealed class ChequeAllocation : BaseEntity
{
    public Guid ChequeId { get; set; }
    public Cheque Cheque { get; set; } = null!;

    /// <summary>Bu paya düşen tutar (çekin para biriminde).</summary>
    public decimal Amount { get; set; }

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>
    /// Masraf merkezi kodu. Fatura bağlantılı satırda faturadan,
    /// elle dağılımda kullanıcıdan gelir.
    /// </summary>
    public string? CostCenterCode { get; set; }

    /// <summary>Verilen çekin ödediği tedarikçi faturası.</summary>
    public Guid? SupplierInvoiceId { get; set; }
    public SupplierInvoice? SupplierInvoice { get; set; }

    /// <summary>Alınan çekin tahsil ettiği satış faturası.</summary>
    public Guid? SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }

    public string? Description { get; set; }
}
