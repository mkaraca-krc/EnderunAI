using System.Globalization;
using System.Xml.Linq;

namespace EnderunAI.Api.Services.EInvoice;

/// <summary>
/// UBL-TR 2.1 e-fatura ayrıştırıcısı.
///
/// Veritabanına ve ağa dokunmaz — saf XML → nesne. Bordro, tazminat ve
/// hakediş motorlarıyla aynı desen: gerçek faturalarla birebir test
/// edilebilsin diye.
///
/// Hiçbir alan uydurulmaz: bulunamayan alan null kalır ve gerekçesi
/// <see cref="ParsedInvoice.Problems"/> içine yazılır. Zorunlu alanlar
/// çıkarılamazsa fatura "okunamadı" sayılır ve AI yedeği devreye girer.
/// </summary>
public static class UblTrInvoiceParser
{
    private static readonly XNamespace Cbc =
        "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    private static readonly XNamespace Cac =
        "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";

    /// <summary>
    /// XML metnini ayrıştırır. Bozuk XML veya tanınmayan kök eleman
    /// durumunda istisna FIRLATMAZ; sebebi Problems içinde döner ki
    /// toplu yüklemede diğer dosyalar etkilenmesin.
    /// </summary>
    public static ParsedInvoice Parse(string xml)
    {
        var problems = new List<string>();

        XDocument document;

        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException exception)
        {
            return Empty(
                $"XML okunamadı (bozuk dosya): {exception.Message}");
        }

        var root = document.Root;

        if (root is null)
            return Empty("XML boş.");

        // UBL fatura kökü "Invoice" olmalı; e-arşiv/e-irsaliye gibi
        // başka belgeler yanlışlıkla yüklenmiş olabilir.
        if (!string.Equals(root.Name.LocalName, "Invoice", StringComparison.OrdinalIgnoreCase))
        {
            return Empty(
                $"Tanınmayan belge tipi: kök eleman '{root.Name.LocalName}'. " +
                "Bu ekran UBL-TR e-fatura (Invoice) bekliyor.");
        }

        var profileId = Text(root.Element(Cbc + "ProfileID"));
        var invoiceTypeCode = Text(root.Element(Cbc + "InvoiceTypeCode"));

        // İade faturasında atıf yapılan orijinal fatura numarası.
        var referencedInvoiceNumber = Text(root
            .Elements(Cac + "BillingReference")
            .Select(x => x.Element(Cac + "InvoiceDocumentReference"))
            .FirstOrDefault(x => x is not null)?
            .Element(Cbc + "ID"));
        var invoiceNumber = Text(root.Element(Cbc + "ID"));
        var issueDate = Date(root.Element(Cbc + "IssueDate"));
        var currency = Text(root.Element(Cbc + "DocumentCurrencyCode")) ?? "TRY";

        if (string.IsNullOrWhiteSpace(invoiceNumber))
            problems.Add("Fatura numarası (cbc:ID) bulunamadı.");

        if (issueDate is null)
            problems.Add("Fatura tarihi (cbc:IssueDate) okunamadı.");

        var supplier = ReadParty(root, "AccountingSupplierParty");
        var customer = ReadParty(root, "AccountingCustomerParty");

        if (string.IsNullOrWhiteSpace(supplier.TaxNumber))
            problems.Add("Satıcı VKN'si bulunamadı.");

        if (string.IsNullOrWhiteSpace(customer.TaxNumber))
            problems.Add("Alıcı VKN'si bulunamadı.");

        var lines = ReadLines(root, problems);

        if (lines.Count == 0)
            problems.Add("Faturada hiç kalem (cac:InvoiceLine) bulunamadı.");

        var (vatTotal, withholding) = ReadTaxTotals(root);

        var monetary = root.Element(Cac + "LegalMonetaryTotal");

        var lineExtensionTotal = Amount(monetary?.Element(Cbc + "LineExtensionAmount"));
        var taxExclusive = Amount(monetary?.Element(Cbc + "TaxExclusiveAmount"));
        var taxInclusive = Amount(monetary?.Element(Cbc + "TaxInclusiveAmount"));
        var payable = Amount(monetary?.Element(Cbc + "PayableAmount"));

        if (payable is null)
            problems.Add("Ödenecek tutar (LegalMonetaryTotal/PayableAmount) okunamadı.");

        return new ParsedInvoice(
            ProfileId: profileId,
            InvoiceNumber: invoiceNumber,
            IssueDate: issueDate,
            CurrencyCode: currency,
            Supplier: supplier,
            Customer: customer,
            Lines: lines,
            LineExtensionTotal: lineExtensionTotal,
            TaxExclusiveAmount: taxExclusive,
            TaxInclusiveAmount: taxInclusive,
            PayableAmount: payable,
            VatTotal: vatTotal,
            WithholdingAmount: withholding,
            ParseSource: InvoiceParseSource.Standard,
            Problems: problems,
            InvoiceTypeCode: invoiceTypeCode,
            ReferencedInvoiceNumber: referencedInvoiceNumber,
            ExchangeRate: ReadExchangeRate(root, currency));
    }

    /// <summary>
    /// Faturanın beyan ettiği kuru okur. UBL-TR'de iki yer var:
    /// <c>cac:PricingExchangeRate</c> (fiyatlandırma) ve
    /// <c>cac:TaxExchangeRate</c> (vergi). İlki tercih edilir; faturadaki
    /// TL tutarlar onunla hesaplanmış oluyor.
    ///
    /// Yön kontrolü şart: kaynak belge para birimi, hedef TRY olmalı.
    /// Ters yönde kotalanmış bir kur (TRY→USD) doğrudan kullanılırsa
    /// tutar 47 kat yerine 47'de bir çıkar.
    /// </summary>
    private static decimal? ReadExchangeRate(XElement root, string documentCurrency)
    {
        if (string.Equals(documentCurrency, "TRY", StringComparison.OrdinalIgnoreCase))
            return null;

        foreach (var name in new[] { "PricingExchangeRate", "TaxExchangeRate" })
        {
            var element = root.Element(Cac + name);
            if (element is null)
                continue;

            var source = Text(element.Element(Cbc + "SourceCurrencyCode"));
            var target = Text(element.Element(Cbc + "TargetCurrencyCode"));
            var rate = Amount(element.Element(Cbc + "CalculationRate"));

            if (rate is null or <= 0)
                continue;

            // Para birimleri belirtilmemişse belge para birimi → TRY
            // varsayılır; UBL-TR'de yaygın olan da bu.
            var sourceOk = string.IsNullOrWhiteSpace(source)
                || string.Equals(source, documentCurrency, StringComparison.OrdinalIgnoreCase);
            var targetOk = string.IsNullOrWhiteSpace(target)
                || string.Equals(target, "TRY", StringComparison.OrdinalIgnoreCase);

            if (sourceOk && targetOk)
                return rate;
        }

        return null;
    }

    /// <summary>
    /// Taraf bilgisi: VKN ve unvan. VKN
    /// <c>Party/PartyIdentification/ID</c> altında; birden fazla
    /// tanımlayıcı olabilir (VKN, TCKN, MERSİS) — şema belirtilmişse
    /// VKN/TCKN tercih edilir, yoksa ilk sayısal kimlik alınır.
    /// </summary>
    private static ParsedParty ReadParty(XElement root, string wrapperName)
    {
        var party = root.Element(Cac + wrapperName)?.Element(Cac + "Party");

        if (party is null)
            return new ParsedParty(null, null);

        var identifications = party.Elements(Cac + "PartyIdentification")
            .Select(x => x.Element(Cbc + "ID"))
            .Where(x => x is not null)
            .ToList();

        var preferred = identifications.FirstOrDefault(x =>
        {
            var scheme = x!.Attribute("schemeID")?.Value;
            return string.Equals(scheme, "VKN", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(scheme, "TCKN", StringComparison.OrdinalIgnoreCase);
        }) ?? identifications.FirstOrDefault();

        var name = Text(party.Element(Cac + "PartyName")?.Element(Cbc + "Name"))
            // Şahıs firmalarında unvan yerine kişi adı gelebilir.
            ?? BuildPersonName(party);

        return new ParsedParty(Text(preferred), name);
    }

    private static string? BuildPersonName(XElement party)
    {
        var person = party.Element(Cac + "Person");

        if (person is null)
            return null;

        var first = Text(person.Element(Cbc + "FirstName"));
        var family = Text(person.Element(Cbc + "FamilyName"));

        var full = $"{first} {family}".Trim();
        return string.IsNullOrWhiteSpace(full) ? null : full;
    }

    private static List<ParsedInvoiceLine> ReadLines(
        XElement root, List<string> problems)
    {
        var lines = new List<ParsedInvoiceLine>();
        var index = 1;

        foreach (var line in root.Elements(Cac + "InvoiceLine"))
        {
            var name = Text(line.Element(Cac + "Item")?.Element(Cbc + "Name"))
                ?? Text(line.Element(Cac + "Item")?.Element(Cbc + "Description"))
                ?? "";

            var quantityElement = line.Element(Cbc + "InvoicedQuantity");
            var quantity = Amount(quantityElement) ?? 0m;
            var unit = quantityElement?.Attribute("unitCode")?.Value ?? "";

            var lineExtension = Amount(line.Element(Cbc + "LineExtensionAmount")) ?? 0m;
            var unitPrice = Amount(
                line.Element(Cac + "Price")?.Element(Cbc + "PriceAmount")) ?? 0m;

            // Birim fiyat yoksa satır tutarından türet; miktar sıfırsa
            // türetilemez ve sıfır kalır.
            if (unitPrice == 0m && quantity != 0m)
                unitPrice = Round(lineExtension / quantity, 6);

            var (lineVatRate, lineVatAmount) = ReadLineTax(line);

            if (string.IsNullOrWhiteSpace(name))
                problems.Add($"{index}. kalemin adı (Item/Name) bulunamadı.");

            lines.Add(new ParsedInvoiceLine(
                LineNumber: index++,
                Name: name,
                Quantity: quantity,
                Unit: unit,
                UnitPrice: unitPrice,
                LineExtensionAmount: lineExtension,
                VatRate: lineVatRate,
                VatAmount: lineVatAmount));
        }

        return lines;
    }

    /// <summary>Satır KDV oranı ve tutarı (TaxTotal/TaxSubtotal).</summary>
    private static (decimal Rate, decimal Amount) ReadLineTax(XElement line)
    {
        var subtotal = line.Element(Cac + "TaxTotal")?.Element(Cac + "TaxSubtotal");

        if (subtotal is null)
            return (0m, 0m);

        var rate = Amount(subtotal.Element(Cbc + "Percent")) ?? 0m;
        var amount = Amount(subtotal.Element(Cbc + "TaxAmount")) ?? 0m;

        // Oran yalnızca TaxCategory altında olabilir.
        if (rate == 0m)
        {
            rate = Amount(subtotal
                .Element(Cac + "TaxCategory")?
                .Element(Cbc + "Percent")) ?? 0m;
        }

        return (rate, amount);
    }

    /// <summary>
    /// Belge seviyesi vergiler. KDV <c>TaxTotal</c> altında; tevkifat
    /// <c>WithholdingTaxTotal</c> altında ayrı gelir.
    /// </summary>
    private static (decimal VatTotal, decimal Withholding) ReadTaxTotals(XElement root)
    {
        var vat = root.Elements(Cac + "TaxTotal")
            .Select(x => Amount(x.Element(Cbc + "TaxAmount")) ?? 0m)
            .Sum();

        var withholding = root.Elements(Cac + "WithholdingTaxTotal")
            .Select(x => Amount(x.Element(Cbc + "TaxAmount")) ?? 0m)
            .Sum();

        return (Round(vat, 2), Round(withholding, 2));
    }

    private static ParsedInvoice Empty(string problem) =>
        new(null, null, null, "TRY",
            new ParsedParty(null, null), new ParsedParty(null, null),
            [], null, null, null, null, 0m, 0m,
            InvoiceParseSource.Standard, [problem]);

    private static string? Text(XElement? element)
    {
        var value = element?.Value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static DateTime? Date(XElement? element)
    {
        var value = Text(element);

        if (value is null)
            return null;

        // UBL tarihleri ISO (yyyy-MM-dd); saat bileşeni gelirse de
        // tarih kısmı alınır.
        return DateTime.TryParse(
            value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc)
            : null;
    }

    /// <summary>
    /// UBL tutarları nokta ondalıklı (invariant). Türkçe biçim
    /// (virgüllü) beklenmez ama entegratör hatasına karşı ikinci deneme
    /// yapılır — sessizce sıfır dönmek yanlış tutar üretirdi.
    /// </summary>
    private static decimal? Amount(XElement? element)
    {
        var value = Text(element);

        if (value is null)
            return null;

        if (decimal.TryParse(
                value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return decimal.TryParse(
            value, NumberStyles.Any, new CultureInfo("tr-TR"), out var turkish)
            ? turkish
            : null;
    }

    private static decimal Round(decimal value, int digits) =>
        decimal.Round(value, digits, MidpointRounding.AwayFromZero);
}
