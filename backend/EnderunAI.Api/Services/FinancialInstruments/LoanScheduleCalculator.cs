namespace EnderunAI.Api.Services.FinancialInstruments;

/// <summary>Planın tek satırı.</summary>
public sealed record LoanScheduleLine(
    int Number,
    DateTime DueDate,
    decimal PrincipalAmount,
    decimal InterestAmount)
{
    public decimal TotalAmount => decimal.Round(PrincipalAmount + InterestAmount, 2);
}

/// <summary>
/// Kredi taksit planı — eşit taksitli (annüite) kredi.
///
/// SAF ve VERİTABANISIZ: <see cref="HumanResources.AdvanceInstallmentCalculator"/>
/// ile aynı desen. Hesap metinden ve girdiden ibaret olduğu için
/// testte tek başına sınanabiliyor ve hem plan üretiminde hem
/// yeniden hesaplamada aynı sonucu veriyor.
///
/// KURUŞ FARKI SON TAKSİTTE KAPANIR: her satır ayrı ayrı
/// yuvarlandığı için anapara toplamı çekilen tutarı birkaç kuruş
/// aşabilir ya da altında kalabilir. Fark son taksitin anaparasına
/// yazılıyor — dağıtılsaydı hiçbir satır bankanın tablosuyla
/// tutmazdı; görmezden gelinseydi kredi kapandığında bakiye sıfıra
/// inmezdi.
/// </summary>
public static class LoanScheduleCalculator
{
    /// <param name="monthlyInterestRate">Aylık faiz oranı, yüzde olarak.</param>
    public static IReadOnlyList<LoanScheduleLine> Build(
        decimal principal,
        decimal monthlyInterestRate,
        int installmentCount,
        DateTime firstInstallmentDate)
    {
        if (principal <= 0m || installmentCount <= 0)
            return [];

        var rate = monthlyInterestRate / 100m;

        var lines = new List<LoanScheduleLine>(installmentCount);

        // FAİZSİZ KREDİ: oran sıfırsa annüite formülü sıfıra bölme
        // üretir; anapara eşit bölünür.
        var payment = rate == 0m
            ? decimal.Round(principal / installmentCount, 2)
            : Annuity(principal, rate, installmentCount);

        var remaining = principal;

        for (var index = 0; index < installmentCount; index++)
        {
            var due = firstInstallmentDate.AddMonths(index);

            var interest = decimal.Round(remaining * rate, 2);
            var principalPart = decimal.Round(payment - interest, 2);

            var isLast = index == installmentCount - 1;

            if (isLast)
            {
                // Kalan anaparanın tamamı son taksite: kredi
                // kapandığında bakiye tam sıfır olmalı.
                principalPart = decimal.Round(remaining, 2);
            }
            else if (principalPart > remaining)
            {
                principalPart = decimal.Round(remaining, 2);
            }

            lines.Add(new LoanScheduleLine(
                index + 1, due.Date, principalPart, interest));

            remaining = decimal.Round(remaining - principalPart, 2);
        }

        return lines;
    }

    /// <summary>
    /// Eşit taksit tutarı: P × i × (1+i)^n / ((1+i)^n − 1).
    ///
    /// Üs alma <c>double</c> üzerinden yapılıyor (decimal'de kuvvet
    /// yok), sonuç kuruşa yuvarlanıyor. Doğruluk kaybı taksit
    /// tutarında kuruş düzeyinde kalır ve zaten son taksitte
    /// kapanır.
    /// </summary>
    private static decimal Annuity(decimal principal, decimal rate, int count)
    {
        var factor = Math.Pow(1d + (double)rate, count);

        var payment = (double)principal * (double)rate * factor / (factor - 1d);

        return decimal.Round((decimal)payment, 2);
    }
}
