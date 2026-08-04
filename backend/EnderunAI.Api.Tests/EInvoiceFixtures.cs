namespace EnderunAI.Api.Tests;

/// <summary>
/// UBL-TR 2.1 fatura örnekleri.
///
/// Rakamlar kullanıcının verdiği iki gerçek faturadan alındı: SEZDEM'e
/// kesilen satış (68.443,20 TL) ve AY Global'den gelen alış
/// (3.101,76 TL). Gerçek XML dosyaları sunucuya konulduğunda bu
/// fixture'ların yanına eklenip aynı testlerden geçirilecek.
///
/// KRİTİK ayrıntı: her iki faturada da InvoiceTypeCode "SATIS" yazar —
/// kesen tarafın açısındandır. Yön yalnızca VKN'den belirlenir.
/// </summary>
public static class EInvoiceFixtures
{
    /// <summary>Enderun'un VKN'si — yön tespitinin dayanağı.</summary>
    public const string OurTaxNumber = "3341211200";

    /// <summary>
    /// GİDEN fatura: satıcı biziz (VKN bizim), alıcı SEZDEM.
    /// 1200 adet × 47,53 = 57.036,00 + %20 KDV 11.407,20 = 68.443,20
    /// </summary>
    public static string SalesInvoice(
        string invoiceNumber = "ENE2026000000123",
        string customerTaxNumber = "7710035506") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                 xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
                 xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2">
          <cbc:ProfileID>TICARIFATURA</cbc:ProfileID>
          <cbc:ID>{invoiceNumber}</cbc:ID>
          <cbc:IssueDate>2026-03-18</cbc:IssueDate>
          <cbc:InvoiceTypeCode>SATIS</cbc:InvoiceTypeCode>
          <cbc:DocumentCurrencyCode>TRY</cbc:DocumentCurrencyCode>
          <cac:AccountingSupplierParty>
            <cac:Party>
              <cac:PartyIdentification>
                <cbc:ID schemeID="VKN">{OurTaxNumber}</cbc:ID>
              </cac:PartyIdentification>
              <cac:PartyName>
                <cbc:Name>ENDERUN ELEKTRIK URETIM ENERJI A.S.</cbc:Name>
              </cac:PartyName>
            </cac:Party>
          </cac:AccountingSupplierParty>
          <cac:AccountingCustomerParty>
            <cac:Party>
              <cac:PartyIdentification>
                <cbc:ID schemeID="VKN">{customerTaxNumber}</cbc:ID>
              </cac:PartyIdentification>
              <cac:PartyName>
                <cbc:Name>SEZDEM ELEKTRIK SAN. TIC. LTD. STI.</cbc:Name>
              </cac:PartyName>
            </cac:Party>
          </cac:AccountingCustomerParty>
          <cac:TaxTotal>
            <cbc:TaxAmount currencyID="TRY">11407.20</cbc:TaxAmount>
            <cac:TaxSubtotal>
              <cbc:TaxableAmount currencyID="TRY">57036.00</cbc:TaxableAmount>
              <cbc:TaxAmount currencyID="TRY">11407.20</cbc:TaxAmount>
              <cbc:Percent>20</cbc:Percent>
              <cac:TaxCategory>
                <cac:TaxScheme><cbc:Name>KDV</cbc:Name></cac:TaxScheme>
              </cac:TaxCategory>
            </cac:TaxSubtotal>
          </cac:TaxTotal>
          <cac:LegalMonetaryTotal>
            <cbc:LineExtensionAmount currencyID="TRY">57036.00</cbc:LineExtensionAmount>
            <cbc:TaxExclusiveAmount currencyID="TRY">57036.00</cbc:TaxExclusiveAmount>
            <cbc:TaxInclusiveAmount currencyID="TRY">68443.20</cbc:TaxInclusiveAmount>
            <cbc:PayableAmount currencyID="TRY">68443.20</cbc:PayableAmount>
          </cac:LegalMonetaryTotal>
          <cac:InvoiceLine>
            <cbc:ID>1</cbc:ID>
            <cbc:InvoicedQuantity unitCode="NIU">1200</cbc:InvoicedQuantity>
            <cbc:LineExtensionAmount currencyID="TRY">57036.00</cbc:LineExtensionAmount>
            <cac:TaxTotal>
              <cbc:TaxAmount currencyID="TRY">11407.20</cbc:TaxAmount>
              <cac:TaxSubtotal>
                <cbc:TaxableAmount currencyID="TRY">57036.00</cbc:TaxableAmount>
                <cbc:TaxAmount currencyID="TRY">11407.20</cbc:TaxAmount>
                <cbc:Percent>20</cbc:Percent>
              </cac:TaxSubtotal>
            </cac:TaxTotal>
            <cac:Item>
              <cbc:Name>40 CT-KM Ek Elemani</cbc:Name>
            </cac:Item>
            <cac:Price>
              <cbc:PriceAmount currencyID="TRY">47.53</cbc:PriceAmount>
            </cac:Price>
          </cac:InvoiceLine>
        </Invoice>
        """;

    /// <summary>
    /// GELEN fatura: alıcı biziz, satıcı AY Global. Çoklu kalem.
    /// 1.850,00 + 730,80 = 2.584,80 + %20 KDV 516,96 = 3.101,76
    /// </summary>
    public static string PurchaseInvoice(
        string invoiceNumber = "AYG2026000000456",
        string supplierTaxNumber = "1234567890") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                 xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
                 xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2">
          <cbc:ProfileID>TEMELFATURA</cbc:ProfileID>
          <cbc:ID>{invoiceNumber}</cbc:ID>
          <cbc:IssueDate>2026-03-05</cbc:IssueDate>
          <cbc:InvoiceTypeCode>SATIS</cbc:InvoiceTypeCode>
          <cbc:DocumentCurrencyCode>TRY</cbc:DocumentCurrencyCode>
          <cac:AccountingSupplierParty>
            <cac:Party>
              <cac:PartyIdentification>
                <cbc:ID schemeID="VKN">{supplierTaxNumber}</cbc:ID>
              </cac:PartyIdentification>
              <cac:PartyName>
                <cbc:Name>AY GLOBAL ELEKTRIK MALZEMELERI LTD. STI.</cbc:Name>
              </cac:PartyName>
            </cac:Party>
          </cac:AccountingSupplierParty>
          <cac:AccountingCustomerParty>
            <cac:Party>
              <cac:PartyIdentification>
                <cbc:ID schemeID="VKN">{OurTaxNumber}</cbc:ID>
              </cac:PartyIdentification>
              <cac:PartyName>
                <cbc:Name>ENDERUN ELEKTRIK URETIM ENERJI A.S.</cbc:Name>
              </cac:PartyName>
            </cac:Party>
          </cac:AccountingCustomerParty>
          <cac:TaxTotal>
            <cbc:TaxAmount currencyID="TRY">516.96</cbc:TaxAmount>
            <cac:TaxSubtotal>
              <cbc:TaxableAmount currencyID="TRY">2584.80</cbc:TaxableAmount>
              <cbc:TaxAmount currencyID="TRY">516.96</cbc:TaxAmount>
              <cbc:Percent>20</cbc:Percent>
            </cac:TaxSubtotal>
          </cac:TaxTotal>
          <cac:LegalMonetaryTotal>
            <cbc:LineExtensionAmount currencyID="TRY">2584.80</cbc:LineExtensionAmount>
            <cbc:TaxExclusiveAmount currencyID="TRY">2584.80</cbc:TaxExclusiveAmount>
            <cbc:TaxInclusiveAmount currencyID="TRY">3101.76</cbc:TaxInclusiveAmount>
            <cbc:PayableAmount currencyID="TRY">3101.76</cbc:PayableAmount>
          </cac:LegalMonetaryTotal>
          <cac:InvoiceLine>
            <cbc:ID>1</cbc:ID>
            <cbc:InvoicedQuantity unitCode="MTR">100</cbc:InvoicedQuantity>
            <cbc:LineExtensionAmount currencyID="TRY">1850.00</cbc:LineExtensionAmount>
            <cac:TaxTotal>
              <cbc:TaxAmount currencyID="TRY">370.00</cbc:TaxAmount>
              <cac:TaxSubtotal>
                <cbc:TaxAmount currencyID="TRY">370.00</cbc:TaxAmount>
                <cbc:Percent>20</cbc:Percent>
              </cac:TaxSubtotal>
            </cac:TaxTotal>
            <cac:Item><cbc:Name>NYAF Kablo 3x2.5</cbc:Name></cac:Item>
            <cac:Price><cbc:PriceAmount currencyID="TRY">18.50</cbc:PriceAmount></cac:Price>
          </cac:InvoiceLine>
          <cac:InvoiceLine>
            <cbc:ID>2</cbc:ID>
            <cbc:InvoicedQuantity unitCode="NIU">40</cbc:InvoicedQuantity>
            <cbc:LineExtensionAmount currencyID="TRY">734.80</cbc:LineExtensionAmount>
            <cac:TaxTotal>
              <cbc:TaxAmount currencyID="TRY">146.96</cbc:TaxAmount>
              <cac:TaxSubtotal>
                <cbc:TaxAmount currencyID="TRY">146.96</cbc:TaxAmount>
                <cbc:Percent>20</cbc:Percent>
              </cac:TaxSubtotal>
            </cac:TaxTotal>
            <cac:Item><cbc:Name>Kofra 12 Modul</cbc:Name></cac:Item>
            <cac:Price><cbc:PriceAmount currencyID="TRY">18.37</cbc:PriceAmount></cac:Price>
          </cac:InvoiceLine>
        </Invoice>
        """;

    /// <summary>
    /// Tevkifatlı gelen fatura: 10.000 + %20 KDV 2.000, tevkifat 4/10 =
    /// 800 → ödenecek 11.200.
    /// </summary>
    public static string PurchaseInvoiceWithWithholding() => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                 xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
                 xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2">
          <cbc:ProfileID>TEMELFATURA</cbc:ProfileID>
          <cbc:ID>TEV2026000000001</cbc:ID>
          <cbc:IssueDate>2026-03-20</cbc:IssueDate>
          <cbc:InvoiceTypeCode>SATIS</cbc:InvoiceTypeCode>
          <cbc:DocumentCurrencyCode>TRY</cbc:DocumentCurrencyCode>
          <cac:AccountingSupplierParty>
            <cac:Party>
              <cac:PartyIdentification><cbc:ID schemeID="VKN">9876543210</cbc:ID></cac:PartyIdentification>
              <cac:PartyName><cbc:Name>TEVKIFATLI TASERON LTD.</cbc:Name></cac:PartyName>
            </cac:Party>
          </cac:AccountingSupplierParty>
          <cac:AccountingCustomerParty>
            <cac:Party>
              <cac:PartyIdentification><cbc:ID schemeID="VKN">{OurTaxNumber}</cbc:ID></cac:PartyIdentification>
              <cac:PartyName><cbc:Name>ENDERUN ELEKTRIK URETIM ENERJI A.S.</cbc:Name></cac:PartyName>
            </cac:Party>
          </cac:AccountingCustomerParty>
          <cac:TaxTotal>
            <cbc:TaxAmount currencyID="TRY">2000.00</cbc:TaxAmount>
            <cac:TaxSubtotal>
              <cbc:TaxableAmount currencyID="TRY">10000.00</cbc:TaxableAmount>
              <cbc:TaxAmount currencyID="TRY">2000.00</cbc:TaxAmount>
              <cbc:Percent>20</cbc:Percent>
            </cac:TaxSubtotal>
          </cac:TaxTotal>
          <cac:WithholdingTaxTotal>
            <cbc:TaxAmount currencyID="TRY">800.00</cbc:TaxAmount>
          </cac:WithholdingTaxTotal>
          <cac:LegalMonetaryTotal>
            <cbc:LineExtensionAmount currencyID="TRY">10000.00</cbc:LineExtensionAmount>
            <cbc:TaxExclusiveAmount currencyID="TRY">10000.00</cbc:TaxExclusiveAmount>
            <cbc:TaxInclusiveAmount currencyID="TRY">12000.00</cbc:TaxInclusiveAmount>
            <cbc:PayableAmount currencyID="TRY">11200.00</cbc:PayableAmount>
          </cac:LegalMonetaryTotal>
          <cac:InvoiceLine>
            <cbc:ID>1</cbc:ID>
            <cbc:InvoicedQuantity unitCode="NIU">1</cbc:InvoicedQuantity>
            <cbc:LineExtensionAmount currencyID="TRY">10000.00</cbc:LineExtensionAmount>
            <cac:TaxTotal>
              <cbc:TaxAmount currencyID="TRY">2000.00</cbc:TaxAmount>
              <cac:TaxSubtotal>
                <cbc:TaxAmount currencyID="TRY">2000.00</cbc:TaxAmount>
                <cbc:Percent>20</cbc:Percent>
              </cac:TaxSubtotal>
            </cac:TaxTotal>
            <cac:Item><cbc:Name>Elektrik tesisat isciligi</cbc:Name></cac:Item>
            <cac:Price><cbc:PriceAmount currencyID="TRY">10000.00</cbc:PriceAmount></cac:Price>
          </cac:InvoiceLine>
        </Invoice>
        """;

    /// <summary>Üçüncü tarafların faturası — bizim VKN hiç geçmiyor.</summary>
    public static string ForeignInvoice() =>
        PurchaseInvoice(supplierTaxNumber: "1111111111")
            .Replace(OurTaxNumber, "2222222222");

    /// <summary>
    /// Kalem toplamı genel toplamı tutmuyor: satır 50.000 diyor, belge
    /// başlığı 57.036. Metin değiştirme yerine ayrı bir gövde yazıldı —
    /// replace, girinti değişince sessizce tutmayabilir ve test yanlış
    /// yere yeşil verirdi.
    /// </summary>
    public static string InconsistentInvoice() => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                 xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
                 xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2">
          <cbc:ProfileID>TICARIFATURA</cbc:ProfileID>
          <cbc:ID>TUTARSIZ2026000000001</cbc:ID>
          <cbc:IssueDate>2026-03-18</cbc:IssueDate>
          <cbc:InvoiceTypeCode>SATIS</cbc:InvoiceTypeCode>
          <cbc:DocumentCurrencyCode>TRY</cbc:DocumentCurrencyCode>
          <cac:AccountingSupplierParty>
            <cac:Party>
              <cac:PartyIdentification><cbc:ID schemeID="VKN">{OurTaxNumber}</cbc:ID></cac:PartyIdentification>
              <cac:PartyName><cbc:Name>ENDERUN ELEKTRIK URETIM ENERJI A.S.</cbc:Name></cac:PartyName>
            </cac:Party>
          </cac:AccountingSupplierParty>
          <cac:AccountingCustomerParty>
            <cac:Party>
              <cac:PartyIdentification><cbc:ID schemeID="VKN">7710035506</cbc:ID></cac:PartyIdentification>
              <cac:PartyName><cbc:Name>SEZDEM ELEKTRIK SAN. TIC. LTD. STI.</cbc:Name></cac:PartyName>
            </cac:Party>
          </cac:AccountingCustomerParty>
          <cac:TaxTotal>
            <cbc:TaxAmount currencyID="TRY">11407.20</cbc:TaxAmount>
            <cac:TaxSubtotal>
              <cbc:TaxableAmount currencyID="TRY">57036.00</cbc:TaxableAmount>
              <cbc:TaxAmount currencyID="TRY">11407.20</cbc:TaxAmount>
              <cbc:Percent>20</cbc:Percent>
            </cac:TaxSubtotal>
          </cac:TaxTotal>
          <cac:LegalMonetaryTotal>
            <cbc:LineExtensionAmount currencyID="TRY">57036.00</cbc:LineExtensionAmount>
            <cbc:TaxExclusiveAmount currencyID="TRY">57036.00</cbc:TaxExclusiveAmount>
            <cbc:TaxInclusiveAmount currencyID="TRY">68443.20</cbc:TaxInclusiveAmount>
            <cbc:PayableAmount currencyID="TRY">68443.20</cbc:PayableAmount>
          </cac:LegalMonetaryTotal>
          <cac:InvoiceLine>
            <cbc:ID>1</cbc:ID>
            <cbc:InvoicedQuantity unitCode="NIU">1200</cbc:InvoicedQuantity>
            <cbc:LineExtensionAmount currencyID="TRY">50000.00</cbc:LineExtensionAmount>
            <cac:TaxTotal>
              <cbc:TaxAmount currencyID="TRY">11407.20</cbc:TaxAmount>
              <cac:TaxSubtotal>
                <cbc:TaxAmount currencyID="TRY">11407.20</cbc:TaxAmount>
                <cbc:Percent>20</cbc:Percent>
              </cac:TaxSubtotal>
            </cac:TaxTotal>
            <cac:Item><cbc:Name>40 CT-KM Ek Elemani</cbc:Name></cac:Item>
            <cac:Price><cbc:PriceAmount currencyID="TRY">47.53</cbc:PriceAmount></cac:Price>
          </cac:InvoiceLine>
        </Invoice>
        """;

    /// <summary>
    /// Kuruş farkı: satır 57.036,02 iken belge 57.036,00. Tolerans
    /// içinde kalmalı, tutarsızlık sayılmamalı.
    /// </summary>
    public static string PennyRoundingInvoice() =>
        InconsistentInvoice().Replace("50000.00", "57036.02");

    public const string BrokenXml =
        "<Invoice><cbc:ID>KIRIK</Invoice>";

    /// <summary>e-İrsaliye yanlışlıkla yüklendiğinde.</summary>
    public const string WrongDocumentType = """
        <?xml version="1.0" encoding="UTF-8"?>
        <DespatchAdvice xmlns="urn:oasis:names:specification:ubl:schema:xsd:DespatchAdvice-2">
          <ID>IRS2026000000001</ID>
        </DespatchAdvice>
        """;
}
