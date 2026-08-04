using EnderunAI.Api.Services.EInvoice;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// UBL-TR ayrıştırıcısı. Beklenen değerler kullanıcının verdiği iki
/// gerçek faturadan alındı; ayrıştırıcı veritabanına bağlı olmadığı
/// için saf birim testi.
/// </summary>
public sealed class UblTrInvoiceParserTests
{
    // ---------- Temel ayrıştırma ----------

    /// <summary>
    /// Giden fatura (SEZDEM): 1200 × 47,53 = 57.036 + %20 KDV = 68.443,20
    /// </summary>
    [Fact]
    public void SalesInvoice_IsParsedWithAllTotals()
    {
        var invoice = UblTrInvoiceParser.Parse(EInvoiceFixtures.SalesInvoice());

        Assert.Empty(invoice.Problems);
        Assert.True(invoice.HasRequiredFields);

        Assert.Equal("ENE2026000000123", invoice.InvoiceNumber);
        Assert.Equal(new DateTime(2026, 3, 18), invoice.IssueDate!.Value.Date);
        Assert.Equal("TRY", invoice.CurrencyCode);
        Assert.Equal("TICARIFATURA", invoice.ProfileId);

        Assert.Equal(EInvoiceFixtures.OurTaxNumber, invoice.Supplier.TaxNumber);
        Assert.Equal("7710035506", invoice.Customer.TaxNumber);
        Assert.Contains("SEZDEM", invoice.Customer.Name);

        Assert.Equal(57_036.00m, invoice.LineExtensionTotal);
        Assert.Equal(11_407.20m, invoice.VatTotal);
        Assert.Equal(68_443.20m, invoice.TaxInclusiveAmount);
        Assert.Equal(68_443.20m, invoice.PayableAmount);
        Assert.Equal(0m, invoice.WithholdingAmount);

        var line = Assert.Single(invoice.Lines);
        Assert.Equal("40 CT-KM Ek Elemani", line.Name);
        Assert.Equal(1200m, line.Quantity);
        Assert.Equal("NIU", line.Unit);
        Assert.Equal(47.53m, line.UnitPrice);
        Assert.Equal(57_036.00m, line.LineExtensionAmount);
        Assert.Equal(20m, line.VatRate);
        Assert.Equal(11_407.20m, line.VatAmount);
    }

    /// <summary>Gelen fatura (AY Global): çoklu kalem doğru okunmalı.</summary>
    [Fact]
    public void PurchaseInvoice_ParsesMultipleLines()
    {
        var invoice = UblTrInvoiceParser.Parse(EInvoiceFixtures.PurchaseInvoice());

        Assert.Empty(invoice.Problems);
        Assert.Equal(2, invoice.Lines.Count);

        Assert.Equal("NYAF Kablo 3x2.5", invoice.Lines[0].Name);
        Assert.Equal(100m, invoice.Lines[0].Quantity);
        Assert.Equal("MTR", invoice.Lines[0].Unit);
        Assert.Equal(18.50m, invoice.Lines[0].UnitPrice);

        Assert.Equal("Kofra 12 Modul", invoice.Lines[1].Name);
        Assert.Equal(40m, invoice.Lines[1].Quantity);
        Assert.Equal(18.37m, invoice.Lines[1].UnitPrice);

        // Satır tutarları belge toplamını vermeli.
        Assert.Equal(
            invoice.LineExtensionTotal,
            invoice.Lines.Sum(x => x.LineExtensionAmount));

        Assert.Equal(3_101.76m, invoice.PayableAmount);
    }

    /// <summary>Tevkifat ayrı bir toplamdan okunur ve ödenecekten düşer.</summary>
    [Fact]
    public void Withholding_IsParsedSeparatelyFromVat()
    {
        var invoice = UblTrInvoiceParser.Parse(
            EInvoiceFixtures.PurchaseInvoiceWithWithholding());

        Assert.Equal(2_000.00m, invoice.VatTotal);
        Assert.Equal(800.00m, invoice.WithholdingAmount);
        Assert.Equal(12_000.00m, invoice.TaxInclusiveAmount);
        // KDV dahil 12.000 − tevkifat 800 = 11.200
        Assert.Equal(11_200.00m, invoice.PayableAmount);
    }

    // ---------- Yön tespiti (paketin kritik kuralı) ----------

    /// <summary>
    /// InvoiceTypeCode HER faturada "SATIS" yazar — kesen tarafın
    /// açısındandır. Yön yalnızca VKN'den belirlenir; bu iki test aynı
    /// tip kodlu iki faturanın zıt yönde olduğunu gösteriyor.
    /// </summary>
    [Fact]
    public void Direction_IsPurchaseWhenWeAreTheCustomer()
    {
        var invoice = UblTrInvoiceParser.Parse(EInvoiceFixtures.PurchaseInvoice());

        Assert.Contains("SATIS", EInvoiceFixtures.PurchaseInvoice());
        Assert.Equal(
            InvoiceDirection.Purchase,
            invoice.ResolveDirection(EInvoiceFixtures.OurTaxNumber));
    }

    [Fact]
    public void Direction_IsSalesWhenWeAreTheSupplier()
    {
        var invoice = UblTrInvoiceParser.Parse(EInvoiceFixtures.SalesInvoice());

        Assert.Contains("SATIS", EInvoiceFixtures.SalesInvoice());
        Assert.Equal(
            InvoiceDirection.Sales,
            invoice.ResolveDirection(EInvoiceFixtures.OurTaxNumber));
    }

    /// <summary>
    /// Bizim VKN hiç geçmiyorsa yön belirlenemez — dosya atlanmalı.
    /// Yanlış tarafa kayıt riskini kapatan kural.
    /// </summary>
    [Fact]
    public void Direction_IsUnknownWhenOurTaxNumberIsAbsent()
    {
        var invoice = UblTrInvoiceParser.Parse(EInvoiceFixtures.ForeignInvoice());

        Assert.Equal(
            InvoiceDirection.Unknown,
            invoice.ResolveDirection(EInvoiceFixtures.OurTaxNumber));
    }

    /// <summary>VKN karşılaştırması biçim farklarına takılmamalı.</summary>
    [Theory]
    [InlineData("3341211200")]
    [InlineData(" 3341211200 ")]
    [InlineData("334 121 12 00")]
    public void Direction_IgnoresTaxNumberFormatting(string ourTaxNumber)
    {
        var invoice = UblTrInvoiceParser.Parse(EInvoiceFixtures.PurchaseInvoice());

        Assert.Equal(
            InvoiceDirection.Purchase, invoice.ResolveDirection(ourTaxNumber));
    }

    // ---------- Hata yönetimi ----------

    /// <summary>
    /// Bozuk XML istisna FIRLATMAMALI; toplu yüklemede diğer dosyalar
    /// etkilenmemeli.
    /// </summary>
    [Fact]
    public void BrokenXml_ReturnsProblemInsteadOfThrowing()
    {
        var invoice = UblTrInvoiceParser.Parse(EInvoiceFixtures.BrokenXml);

        Assert.False(invoice.HasRequiredFields);
        Assert.Contains(invoice.Problems, x => x.Contains("XML okunamadı"));
    }

    /// <summary>Yanlış belge tipi (e-irsaliye) net mesajla reddedilmeli.</summary>
    [Fact]
    public void WrongDocumentType_IsRejectedWithClearMessage()
    {
        var invoice = UblTrInvoiceParser.Parse(EInvoiceFixtures.WrongDocumentType);

        Assert.False(invoice.HasRequiredFields);
        Assert.Contains(invoice.Problems, x => x.Contains("Tanınmayan belge tipi"));
    }

    /// <summary>Zorunlu alan eksikse fatura "okunamadı" sayılır.</summary>
    [Fact]
    public void MissingInvoiceNumber_MakesRequiredFieldsIncomplete()
    {
        var xml = EInvoiceFixtures.SalesInvoice()
            .Replace("<cbc:ID>ENE2026000000123</cbc:ID>", "");

        var invoice = UblTrInvoiceParser.Parse(xml);

        Assert.False(invoice.HasRequiredFields);
        Assert.Contains(invoice.Problems, x => x.Contains("Fatura numarası"));
    }

    // ---------- Tutarlılık ----------

    [Fact]
    public void ConsistentInvoice_PassesValidation()
    {
        var invoice = UblTrInvoiceParser.Parse(EInvoiceFixtures.SalesInvoice());
        var result = UblTrInvoiceValidator.Validate(invoice);

        Assert.True(result.IsConsistent);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void PurchaseInvoiceWithWithholding_PassesValidation()
    {
        var invoice = UblTrInvoiceParser.Parse(
            EInvoiceFixtures.PurchaseInvoiceWithWithholding());

        Assert.True(UblTrInvoiceValidator.Validate(invoice).IsConsistent);
    }

    /// <summary>
    /// Kalem toplamı genel toplamı tutmuyorsa tutarsızlık işaretlenir.
    /// Bu kontrol AI çıktısında uydurma tutarı da yakalar.
    /// </summary>
    [Fact]
    public void InconsistentTotals_AreDetected()
    {
        var invoice = UblTrInvoiceParser.Parse(EInvoiceFixtures.InconsistentInvoice());
        var result = UblTrInvoiceValidator.Validate(invoice);

        Assert.False(result.IsConsistent);
        Assert.Contains(result.Issues, x => x.Contains("uyuşmuyor"));
    }

    /// <summary>
    /// Kuruş farkı tutarsızlık sayılmaz — entegratörler satır bazında
    /// yuvarladığı için birkaç kuruş sapma normal.
    /// </summary>
    [Fact]
    public void PennyRounding_IsToleratedAsConsistent()
    {
        var invoice = UblTrInvoiceParser.Parse(
            EInvoiceFixtures.PennyRoundingInvoice());

        Assert.Equal(57_036.02m, invoice.Lines.Sum(x => x.LineExtensionAmount));
        Assert.True(UblTrInvoiceValidator.Validate(invoice).IsConsistent);
    }

    [Fact]
    public void BlockingProblems_ListMissingRequiredFields()
    {
        var invoice = UblTrInvoiceParser.Parse(EInvoiceFixtures.BrokenXml);
        var blocking = UblTrInvoiceValidator.CollectBlockingProblems(invoice);

        Assert.NotEmpty(blocking);
        Assert.Contains(blocking, x => x.Contains("Fatura numarası"));
    }

    [Fact]
    public void ValidInvoice_HasNoBlockingProblems()
    {
        var invoice = UblTrInvoiceParser.Parse(EInvoiceFixtures.PurchaseInvoice());

        Assert.Empty(UblTrInvoiceValidator.CollectBlockingProblems(invoice));
    }
}
