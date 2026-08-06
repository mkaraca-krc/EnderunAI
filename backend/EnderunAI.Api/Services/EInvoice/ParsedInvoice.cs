namespace EnderunAI.Api.Services.EInvoice;

/// <summary>
/// Faturanın bizim açımızdan yönü.
///
/// DİKKAT: UBL-TR'de <c>InvoiceTypeCode</c> normal faturada her zaman
/// "SATIS" yazar — kesen tarafın açısındandır. Yön asla oradan okunmaz;
/// VKN karşılaştırmasıyla belirlenir. Alan yalnızca IADE ayrımı için
/// okunur.
/// </summary>
public enum InvoiceDirection
{
    /// <summary>Yön belirlenemedi (hiçbir taraf bizim VKN değil).</summary>
    Unknown = 0,

    /// <summary>Gelen fatura: alıcı biziz, satıcı tedarikçi.</summary>
    Purchase = 1,

    /// <summary>Giden fatura: satıcı biziz, alıcı müşteri.</summary>
    Sales = 2
}

/// <summary>Faturayı hangi katman okudu — denetim izi.</summary>
public enum InvoiceParseSource
{
    /// <summary>Standart UBL-TR ayrıştırıcı (hızlı, AI'sız).</summary>
    Standard = 0,

    /// <summary>
    /// AI yedek ayrıştırıcı. Bu faturalar ASLA otomatik onaylanmaz;
    /// her zaman elle kontrol edilir.
    /// </summary>
    Ai = 1
}

public sealed record ParsedParty(string? TaxNumber, string? Name);

public sealed record ParsedInvoiceLine(
    int LineNumber,
    string Name,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    /// <summary>Satırın KDV hariç tutarı (LineExtensionAmount).</summary>
    decimal LineExtensionAmount,
    decimal VatRate,
    decimal VatAmount);

/// <summary>
/// XML'den çıkarılmış fatura. Ayrıştırıcı hiçbir alanı uydurmaz;
/// bulunamayan alan null kalır ve <see cref="Problems"/> içinde
/// gerekçesi yazar.
/// </summary>
public sealed record ParsedInvoice(
    string? ProfileId,
    string? InvoiceNumber,
    DateTime? IssueDate,
    string CurrencyCode,
    ParsedParty Supplier,
    ParsedParty Customer,
    IReadOnlyList<ParsedInvoiceLine> Lines,
    decimal? LineExtensionTotal,
    decimal? TaxExclusiveAmount,
    decimal? TaxInclusiveAmount,
    decimal? PayableAmount,
    decimal VatTotal,
    /// <summary>KDV tevkifatı; yoksa sıfır.</summary>
    decimal WithholdingAmount,
    InvoiceParseSource ParseSource,
    IReadOnlyList<string> Problems,
    /// <summary>
    /// UBL-TR <c>InvoiceTypeCode</c>: SATIS, IADE, TEVKIFAT, ISTISNA...
    /// Yön için ASLA kullanılmaz (her fatura keseni açısından "SATIS"
    /// yazar) ama IADE değeri belgenin bir iade faturası olduğunu
    /// söyler ve bunun başka karşılığı yoktur.
    /// </summary>
    string? InvoiceTypeCode = null,
    /// <summary>
    /// İade faturasında atıf yapılan orijinal fatura numarası
    /// (cac:BillingReference). Orijinali otomatik eşleştirmek için;
    /// bulunamazsa kullanıcı elle seçer.
    /// </summary>
    string? ReferencedInvoiceNumber = null,
    /// <summary>
    /// Faturanın kendi beyan ettiği kur (cac:PricingExchangeRate veya
    /// cac:TaxExchangeRate → cbc:CalculationRate). Dövizli faturada
    /// satıcının kullandığı kur budur; TCMB arşivinden gelen kura
    /// tercih edilir, çünkü faturadaki TL tutarlar bu kurla hesaplanmış.
    /// Belge TL ise veya kur beyan edilmemişse null.
    /// </summary>
    decimal? ExchangeRate = null)
{
    /// <summary>
    /// Zorunlu alanlar çıkarılabildi mi. Çıkarılamadıysa AI yedeği
    /// devreye girer.
    /// </summary>
    public bool HasRequiredFields =>
        !string.IsNullOrWhiteSpace(InvoiceNumber) &&
        IssueDate is not null &&
        !string.IsNullOrWhiteSpace(Supplier.TaxNumber) &&
        !string.IsNullOrWhiteSpace(Customer.TaxNumber) &&
        Lines.Count > 0 &&
        PayableAmount is not null;

    /// <summary>
    /// Faturanın bizim açımızdan yönü. Alıcı biz isek alış, satıcı biz
    /// isek satış; hiçbiri değilse bilinmiyor (dosya atlanır).
    /// </summary>
    public InvoiceDirection ResolveDirection(string? ourTaxNumber)
    {
        if (string.IsNullOrWhiteSpace(ourTaxNumber))
            return InvoiceDirection.Unknown;

        var ours = Normalize(ourTaxNumber);

        if (Normalize(Customer.TaxNumber) == ours)
            return InvoiceDirection.Purchase;

        if (Normalize(Supplier.TaxNumber) == ours)
            return InvoiceDirection.Sales;

        return InvoiceDirection.Unknown;
    }

    /// <summary>
    /// VKN karşılaştırması boşluk ve biçim farklarına takılmamalı;
    /// yalnızca rakamlar karşılaştırılır.
    /// </summary>
    public static string Normalize(string? taxNumber) =>
        new((taxNumber ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>
    /// Belge bir iade faturası mı (InvoiceTypeCode = IADE).
    /// </summary>
    public bool IsReturnDocument =>
        string.Equals(InvoiceTypeCode?.Trim(), "IADE", StringComparison.OrdinalIgnoreCase);
}
