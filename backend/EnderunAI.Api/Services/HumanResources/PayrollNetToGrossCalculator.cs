namespace EnderunAI.Api.Services.HumanResources;

/// <summary>
/// Brütleştirme sonucu.
/// </summary>
/// <param name="GrossEarnings">Hedef nete ulaşan brüt.</param>
/// <param name="AchievedNet">Bu brütle gerçekte çıkan net.</param>
/// <param name="TargetNet">İstenen net.</param>
/// <param name="Difference">AchievedNet − TargetNet. Yuvarlama
/// nedeniyle sıfır olmayabilir; işaretiyle birlikte döner ki ekranda
/// gizlenmeden gösterilebilsin.</param>
/// <param name="Iterations">Yakınsama için yapılan deneme sayısı —
/// teşhis amaçlı.</param>
/// <param name="Payroll">Bulunan brütle üretilen tam bordro; ekranda
/// kesinti kırılımı gösterilebilsin diye birlikte döner.</param>
public sealed record NetToGrossResult(
    decimal GrossEarnings,
    decimal AchievedNet,
    decimal TargetNet,
    decimal Difference,
    int Iterations,
    PayrollResult Payroll)
{
    /// <summary>
    /// Hedef nete kuruşu kuruşuna ulaşıldı mı. Yuvarlama yüzünden bazı
    /// net değerlerine tam karşılık gelen bir brüt olmayabilir.
    /// </summary>
    public bool IsExact => Math.Abs(Difference) <= 0.01m;
}

/// <summary>
/// Netten brüte hesap (brütleştirme).
///
/// Ücretler pratikte net konuşulur ("eline 45.000 geçecek") ama bordro
/// brütten yürür. Bu motor girilen nete ulaşan brütü bulur.
///
/// YÖNTEM: kapalı formül yok — <see cref="PayrollCalculationService"/>
/// içinde SGK taban/tavan sıkıştırması, kümülatif dilim geçişi ve
/// asgari ücret istisnasının üst sınırı gibi parçalı fonksiyonlar var;
/// bunlar cebirsel olarak ters çevrilemez. Bunun yerine mevcut motor
/// ikili aramayla çağrılır: net, brüte göre monoton arttığı için arama
/// güvenli biçimde yakınsar.
///
/// Mevcut brüt→net motoruna DOKUNULMAZ. Mevzuatın tamamı (istisnalar,
/// dilimler, taban/tavan) orada tek yerde durur; burada yalnızca
/// "hangi brüt bu neti verir" sorusu çözülür. Kural iki yere
/// kopyalansaydı biri güncellenip diğeri unutulurdu.
///
/// Static ve veritabanısız — bordro motoruyla aynı desen.
/// </summary>
public static class PayrollNetToGrossCalculator
{
    /// <summary>Kuruş hassasiyeti; bundan daha ince aranmaz.</summary>
    private const decimal Precision = 0.01m;

    /// <summary>
    /// Güvenlik freni. Kuruş hassasiyetinde ikili arama pratikte 30-40
    /// adımda biter; bu sınıra dayanmak bir mantık hatasına işarettir.
    /// </summary>
    private const int MaxIterations = 200;

    /// <summary>
    /// Hedef nete ulaşan brütü bulur.
    /// </summary>
    /// <param name="parameters">Yıla ait bordro parametreleri.</param>
    /// <param name="targetNet">İstenen aylık net.</param>
    /// <param name="month">Ay (1-12) — asgari ücret istisnası aya bağlı.</param>
    /// <param name="cumulativeIncomeTaxBaseBefore">Aynı yılda bu aydan
    /// önce oluşmuş kümülatif matrah. Yıl ilerledikçe aynı net daha
    /// yüksek brüt gerektirir; bu yüzden ay ve kümülatif matrah
    /// zorunlu girdidir.</param>
    public static NetToGrossResult CalculateGrossFromNet(
        PayrollParameters parameters,
        decimal targetNet,
        int month,
        decimal cumulativeIncomeTaxBaseBefore = 0m,
        decimal sgkExemptEarnings = 0m,
        decimal incomeTaxExemptEarnings = 0m,
        decimal otherDeductions = 0m)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (targetNet < 0m)
            throw new ArgumentException("Hedef net negatif olamaz.", nameof(targetNet));

        if (month is < 1 or > 12)
            throw new ArgumentException("Ay 1-12 aralığında olmalıdır.", nameof(month));

        var target = decimal.Round(targetNet, 2, MidpointRounding.AwayFromZero);

        if (target == 0m)
        {
            var zero = Evaluate(parameters, 0m, month, cumulativeIncomeTaxBaseBefore,
                sgkExemptEarnings, incomeTaxExemptEarnings, otherDeductions);

            return new NetToGrossResult(0m, zero.NetPay, 0m, zero.NetPay, 0, zero);
        }

        var iterations = 0;

        // Alt sınır: brüt asla netten küçük olamaz (kesintiler negatif
        // değil). Üst sınır: netin biraz üstünden başlayıp yetmezse
        // katlanarak genişletilir — sabit bir çarpan (ör. 2,5) yüksek
        // gelirlerde ya da ağır "diğer kesinti" varken yetmeyebilir.
        var low = target;
        var high = Math.Max(target * 1.5m, target + 1_000m);

        while (Evaluate(parameters, high, month, cumulativeIncomeTaxBaseBefore,
                   sgkExemptEarnings, incomeTaxExemptEarnings, otherDeductions).NetPay < target)
        {
            low = high;
            high *= 2m;

            if (++iterations > MaxIterations)
            {
                throw new InvalidOperationException(
                    $"Brütleştirme yakınsamadı: {target:N2} TL net için makul bir brüt " +
                    "bulunamadı. Bordro parametrelerini kontrol edin.");
            }
        }

        // İkili arama: neti hedefe eşit veya ondan büyük tutan EN KÜÇÜK
        // brütü ara. Eşitlik yakalanamadığında personelin eksik değil
        // fazla alması tercih edilir.
        while (high - low > Precision)
        {
            if (++iterations > MaxIterations)
                break;

            var mid = decimal.Round((low + high) / 2m, 2, MidpointRounding.AwayFromZero);

            // Yuvarlama orta noktayı sınıra yapıştırırsa döngü sonsuza
            // gider; bir kuruş ilerletip çık.
            if (mid <= low || mid >= high)
                break;

            var net = Evaluate(parameters, mid, month, cumulativeIncomeTaxBaseBefore,
                sgkExemptEarnings, incomeTaxExemptEarnings, otherDeductions).NetPay;

            if (net < target)
                low = mid;
            else
                high = mid;
        }

        var gross = decimal.Round(high, 2, MidpointRounding.AwayFromZero);
        var payroll = Evaluate(parameters, gross, month, cumulativeIncomeTaxBaseBefore,
            sgkExemptEarnings, incomeTaxExemptEarnings, otherDeductions);

        return new NetToGrossResult(
            GrossEarnings: gross,
            AchievedNet: payroll.NetPay,
            TargetNet: target,
            Difference: decimal.Round(payroll.NetPay - target, 2, MidpointRounding.AwayFromZero),
            Iterations: iterations,
            Payroll: payroll);
    }

    private static PayrollResult Evaluate(
        PayrollParameters parameters,
        decimal gross,
        int month,
        decimal cumulativeBefore,
        decimal sgkExempt,
        decimal incomeTaxExempt,
        decimal otherDeductions) =>
        PayrollCalculationService.Calculate(
            parameters,
            new PayrollInput(
                Month: month,
                GrossEarnings: gross,
                SgkExemptEarnings: sgkExempt,
                IncomeTaxExemptEarnings: incomeTaxExempt,
                CumulativeIncomeTaxBaseBefore: cumulativeBefore,
                OtherDeductions: otherDeductions));
}
