using EnderunAI.Api.Formatting;
namespace EnderunAI.Api.Services.EInvoice;

public sealed record InvoiceValidationResult(
    bool IsConsistent,
    IReadOnlyList<string> Issues)
{
    public static InvoiceValidationResult Ok() => new(true, []);
}

/// <summary>
/// Okunan faturanın kendi içinde tutarlı olup olmadığını denetler.
///
/// Amaç yalnızca bozuk dosyayı yakalamak değil: AI yedeği devreye
/// girdiğinde uydurulmuş bir tutarın sessizce kabul edilmesini de bu
/// kontroller engeller. Bu yüzden hem standart hem AI çıktısına
/// uygulanır.
/// </summary>
public static class UblTrInvoiceValidator
{
    /// <summary>
    /// Kuruş toleransı. Entegratörler satır bazında yuvarlarken genel
    /// toplamla birkaç kuruş fark oluşabiliyor; bu sapma "tutarsızlık"
    /// sayılmamalı, gerçek hata ile karışmamalı.
    /// </summary>
    private const decimal Tolerance = 0.05m;

    public static InvoiceValidationResult Validate(ParsedInvoice invoice)
    {
        var issues = new List<string>();

        // 1) Satırların KDV hariç toplamı belge toplamıyla uyuşmalı.
        if (invoice.Lines.Count > 0 && invoice.LineExtensionTotal is decimal declaredLines)
        {
            var computed = Round(invoice.Lines.Sum(x => x.LineExtensionAmount));

            if (Math.Abs(computed - Round(declaredLines)) > Tolerance)
            {
                issues.Add(
                    $"Kalem toplamı ({TurkishFormat.Amount(computed)}) belgedeki toplamla " +
                    $"({TurkishFormat.Amount(declaredLines)}) uyuşmuyor.");
            }
        }

        // 2) Satır KDV'lerinin toplamı belge KDV'siyle uyuşmalı.
        if (invoice.Lines.Count > 0 && invoice.VatTotal > 0m)
        {
            var computedVat = Round(invoice.Lines.Sum(x => x.VatAmount));

            // Satırlarda KDV hiç verilmemişse karşılaştırma anlamsız.
            if (computedVat > 0m &&
                Math.Abs(computedVat - Round(invoice.VatTotal)) > Tolerance)
            {
                issues.Add(
                    $"Satır KDV toplamı ({TurkishFormat.Amount(computedVat)}) belge KDV'siyle " +
                    $"({TurkishFormat.Amount(invoice.VatTotal)}) uyuşmuyor.");
            }
        }

        // 3) KDV hariç + KDV = KDV dahil olmalı.
        if (invoice.TaxExclusiveAmount is decimal exclusive &&
            invoice.TaxInclusiveAmount is decimal inclusive)
        {
            var expected = Round(exclusive + invoice.VatTotal);

            if (Math.Abs(expected - Round(inclusive)) > Tolerance)
            {
                issues.Add(
                    $"KDV hariç ({TurkishFormat.Amount(exclusive)}) + KDV ({TurkishFormat.Amount(invoice.VatTotal)}) = " +
                    $"{TurkishFormat.Amount(expected)}, ancak KDV dahil tutar {TurkishFormat.Amount(inclusive)} yazıyor.");
            }
        }

        // 4) Ödenecek tutar, KDV dahil tutardan tevkifat düşülmüş hali
        //    olmalı. Tevkifatlı faturada ödenecek daha küçüktür.
        if (invoice.PayableAmount is decimal payable &&
            invoice.TaxInclusiveAmount is decimal taxInclusive)
        {
            var expected = Round(taxInclusive - invoice.WithholdingAmount);

            if (Math.Abs(expected - Round(payable)) > Tolerance)
            {
                issues.Add(
                    $"Ödenecek tutar ({TurkishFormat.Amount(payable)}), KDV dahil tutardan " +
                    $"({TurkishFormat.Amount(taxInclusive)}) tevkifat ({TurkishFormat.Amount(invoice.WithholdingAmount)}) " +
                    $"düşülmüş haliyle ({TurkishFormat.Amount(expected)}) uyuşmuyor.");
            }
        }

        return new InvoiceValidationResult(issues.Count == 0, issues);
    }

    /// <summary>
    /// Fatura kaydedilebilir mi: zorunlu alanlar var mı ve tutarlar
    /// tutuyor mu. Tutarsız fatura kaydedilebilir ama "elle kontrol"
    /// işaretiyle — engellemek yerine işaretlemek doğru, çünkü gerçek
    /// faturada da kuruş hatası olabilir ve ön muhasebe karar vermeli.
    /// </summary>
    public static IReadOnlyList<string> CollectBlockingProblems(ParsedInvoice invoice)
    {
        var blocking = new List<string>();

        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
            blocking.Add("Fatura numarası okunamadı.");

        if (invoice.IssueDate is null)
            blocking.Add("Fatura tarihi okunamadı.");

        if (invoice.Lines.Count == 0)
            blocking.Add("Faturada kalem bulunamadı.");

        if (invoice.PayableAmount is null)
            blocking.Add("Ödenecek tutar okunamadı.");

        return blocking;
    }

    private static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
