namespace EnderunAI.Api.Services.Subcontractors;

/// <summary>Birim fiyatlı kalemin girdileri.</summary>
/// <param name="PreviousQuantity">Önceki hakedişlerin kümülatif
/// mutabakat miktarı.</param>
/// <param name="AgreedQuantity">Bu hakedişte mutabık kalınan KÜMÜLATİF
/// miktar — dönem miktarı değil.</param>
public sealed record SubcontractorItemInput(
    decimal ContractQuantity,
    decimal PreviousQuantity,
    decimal AgreedQuantity,
    decimal UnitPrice);

public sealed record SubcontractorItemResult(
    decimal PreviousQuantity,
    decimal AgreedQuantity,
    decimal CurrentQuantity,
    decimal PreviousAmount,
    decimal CurrentAmount,
    decimal CumulativeAmount);

/// <summary>Götürü kısmın girdileri.</summary>
/// <param name="AgreedProgressRate">Mutabık kalınan KÜMÜLATİF ilerleme
/// yüzdesi (0-100).</param>
public sealed record SubcontractorSectionInput(
    decimal SectionAmount,
    decimal PreviousProgressRate,
    decimal AgreedProgressRate);

public sealed record SubcontractorSectionResult(
    decimal PreviousProgressRate,
    decimal AgreedProgressRate,
    decimal PreviousAmount,
    decimal CurrentAmount,
    decimal CumulativeAmount);

/// <summary>
/// Taşeron hakedişinin hesabı. Static ve veritabanısız —
/// <see cref="Hakedis.HakedisCalculationService"/> ile aynı desen.
///
/// KÜMÜLATİF (MİNHA) MANTIK: her satırda mutabık kalınan tutar
/// KÜMÜLATİFTİR; bu dönem ödenecek = kümülatif − önceki. Dönem tutarını
/// doğrudan girmek, geçmiş bir satır düzeltildiğinde toplamı bozardı.
///
/// Hesap her zaman MUTABAKAT rakamıyla yapılır; sahadan gelen öneri
/// yalnızca ekranda karşılaştırma için durur.
/// </summary>
public static class SubcontractorHakedisCalculator
{
    private static decimal Round(decimal value) => decimal.Round(value, 2);

    /// <summary>
    /// Birim fiyatlı kalem. Mutabakat miktarı sözleşme miktarını aşabilir
    /// (ilave iş); bu bir hata değil, kâr analizinde sapma olarak görünür.
    /// </summary>
    public static SubcontractorItemResult CalculateItem(
        SubcontractorItemInput input)
    {
        var previousQuantity = Math.Max(0m, input.PreviousQuantity);
        var agreedQuantity = Math.Max(0m, input.AgreedQuantity);
        var unitPrice = Math.Max(0m, input.UnitPrice);

        // Kümülatif miktar öncekinin altına düşerse (geçmiş düzeltmesi)
        // bu dönemde eksi iş yazılmaz; dönem sıfırlanır ve düzeltme
        // sonraki dönemlerde kendini gösterir.
        var currentQuantity = Math.Max(0m, agreedQuantity - previousQuantity);

        var previousAmount = Round(previousQuantity * unitPrice);
        var cumulativeAmount = Round(agreedQuantity * unitPrice);
        var currentAmount = Round(currentQuantity * unitPrice);

        return new SubcontractorItemResult(
            PreviousQuantity: previousQuantity,
            AgreedQuantity: agreedQuantity,
            CurrentQuantity: currentQuantity,
            PreviousAmount: previousAmount,
            CurrentAmount: currentAmount,
            CumulativeAmount: cumulativeAmount);
    }

    /// <summary>
    /// Götürü kısım. İlerleme yüzdesi %100'ü aşamaz: götürüde bedel
    /// sabittir, %100'ün üstü sözleşme dışı iş demektir ve ayrı bir
    /// sözleşme/ilave iş olarak yürümelidir.
    /// </summary>
    public static SubcontractorSectionResult CalculateSection(
        SubcontractorSectionInput input)
    {
        var sectionAmount = Math.Max(0m, input.SectionAmount);
        var previousRate = Math.Clamp(input.PreviousProgressRate, 0m, 100m);
        var agreedRate = Math.Clamp(input.AgreedProgressRate, 0m, 100m);

        var previousAmount = Round(sectionAmount * previousRate / 100m);
        var cumulativeAmount = Round(sectionAmount * agreedRate / 100m);
        var currentAmount = Math.Max(0m, Round(cumulativeAmount - previousAmount));

        return new SubcontractorSectionResult(
            PreviousProgressRate: previousRate,
            AgreedProgressRate: agreedRate,
            PreviousAmount: previousAmount,
            CurrentAmount: currentAmount,
            CumulativeAmount: cumulativeAmount);
    }

    /// <summary>
    /// Hakedişin ödeme satırı.
    ///
    /// Kesinti toplamı bu dönem tutarını aşarsa net SIFIRA çekilir ve
    /// aşan kısım bu hakedişte tahsil edilmez — eksi ödeme, taşerondan
    /// para istemek demektir ve mutabakat konusudur, otomatik
    /// yapılamaz.
    /// </summary>
    public static (decimal GrossPayable, decimal NetPayable, decimal Uncollected)
        CalculatePayment(decimal currentAmount, decimal totalDeduction)
    {
        var gross = Math.Max(0m, Round(currentAmount));
        var deduction = Math.Max(0m, Round(totalDeduction));

        var net = Round(gross - deduction);

        return net >= 0m
            ? (gross, net, 0m)
            : (gross, 0m, Round(-net));
    }
}
