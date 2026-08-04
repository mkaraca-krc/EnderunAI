namespace EnderunAI.Api.Services.EInvoice;

/// <summary>
/// Faturanın bizim açımızdan yönü.
///
/// DİKKAT: UBL-TR'de <c>InvoiceTypeCode</c> her faturada "SATIS" yazar
/// — kesen tarafın açısındandır. Yön asla oradan okunmaz; VKN
/// karşılaştırmasıyla belirlenir.
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
    IReadOnlyList<string> Problems)
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
}
