namespace EnderunAI.Api.Services.Hakedis;

/// <summary>
/// Bir poz satırının hesaba giren girdileri.
///
/// Birim fiyat üç bileşenden oluşur: malzeme, montaj (işçilik) ve genel
/// gider &amp; kâr. Miktar üçü için ortaktır; NATURA icmalindeki üç kolon
/// doğrudan bu ayrımdan çıkar.
/// </summary>
/// <param name="PositionCode">Poz kodu — önceki dönemlerle eşleşme buradan.</param>
/// <param name="PreviousQuantity">Önceki hakedişlerin toplam miktarı.</param>
public sealed record HakedisItemInput(
    string PositionCode,
    decimal ContractQuantity,
    decimal PreviousQuantity,
    decimal CurrentQuantity,
    decimal MaterialUnitPrice,
    decimal LaborUnitPrice,
    decimal OverheadUnitPrice,
    Guid? SectionId = null);

/// <summary>Hesaplanmış poz satırı.</summary>
public sealed record HakedisItemResult(
    string PositionCode,
    Guid? SectionId,
    decimal ContractQuantity,
    decimal PreviousQuantity,
    decimal CurrentQuantity,
    decimal CumulativeQuantity,
    decimal MaterialUnitPrice,
    decimal LaborUnitPrice,
    decimal OverheadUnitPrice,
    decimal UnitPrice,
    decimal MaterialAmount,
    decimal LaborAmount,
    decimal OverheadAmount,
    decimal PreviousAmount,
    decimal CurrentAmount,
    decimal CumulativeAmount,
    decimal CompletionRate,
    bool ExceedsContractQuantity);

/// <summary>
/// Bölüm icmali — NATURA'daki imalat bölümü satırı (Panolar, Kuvvetli
/// Akım, Topraklama vb.).
/// </summary>
public sealed record HakedisSectionSummary(
    Guid? SectionId,
    decimal MaterialAmount,
    decimal LaborAmount,
    decimal OverheadAmount,
    decimal CurrentAmount,
    decimal PreviousAmount,
    decimal CumulativeAmount);

/// <summary>
/// Hakedişin poz tarafının tamamı: satırlar, bölüm icmali ve toplamlar.
/// </summary>
public sealed record HakedisItemsResult(
    IReadOnlyList<HakedisItemResult> Items,
    IReadOnlyList<HakedisSectionSummary> Sections,
    decimal MaterialTotal,
    decimal LaborTotal,
    decimal OverheadTotal,
    decimal CurrentTotal,
    decimal PreviousTotal,
    decimal CumulativeTotal);

/// <summary>
/// Hakediş hesabı. Veritabanına ve zamana bağlı değil — aynı girdi her
/// zaman aynı çıktıyı üretir; bordro ve tazminat motorlarıyla aynı
/// ilke. Hesap daha önce controller içinde private static metotlardaydı;
/// NATURA yapısıyla birlikte matematiği ağırlaştığı ve test edilmesi
/// gerektiği için buraya taşındı.
/// </summary>
public static class HakedisCalculationService
{
    /// <summary>
    /// Poz satırlarını hesaplar ve bölüm bazında icmal çıkarır.
    ///
    /// PURSANTAJ: her poz için "önceki miktar + bu dönem miktar = genel
    /// toplam"; tutarlar birim fiyatla çarpılarak bulunur. Önceki miktar
    /// çağıran tarafından, aynı projenin önceki hakedişlerinden poz
    /// koduyla toplanarak verilir (minha mantığı).
    /// </summary>
    public static HakedisItemsResult CalculateItems(
        IEnumerable<HakedisItemInput> inputs)
    {
        var results = new List<HakedisItemResult>();

        foreach (var input in inputs)
        {
            var previousQuantity = Math.Max(0m, input.PreviousQuantity);
            var currentQuantity = Math.Max(0m, input.CurrentQuantity);
            var contractQuantity = Math.Max(0m, input.ContractQuantity);

            var material = Math.Max(0m, input.MaterialUnitPrice);
            var labor = Math.Max(0m, input.LaborUnitPrice);
            var overhead = Math.Max(0m, input.OverheadUnitPrice);
            var unitPrice = Round(material + labor + overhead);

            var cumulativeQuantity = previousQuantity + currentQuantity;

            results.Add(new HakedisItemResult(
                PositionCode: input.PositionCode?.Trim() ?? string.Empty,
                SectionId: input.SectionId,
                ContractQuantity: contractQuantity,
                PreviousQuantity: previousQuantity,
                CurrentQuantity: currentQuantity,
                CumulativeQuantity: cumulativeQuantity,
                MaterialUnitPrice: material,
                LaborUnitPrice: labor,
                OverheadUnitPrice: overhead,
                UnitPrice: unitPrice,
                MaterialAmount: Round(currentQuantity * material),
                LaborAmount: Round(currentQuantity * labor),
                OverheadAmount: Round(currentQuantity * overhead),
                PreviousAmount: Round(previousQuantity * unitPrice),
                CurrentAmount: Round(currentQuantity * unitPrice),
                CumulativeAmount: Round(cumulativeQuantity * unitPrice),
                CompletionRate: contractQuantity > 0m
                    ? Round(cumulativeQuantity / contractQuantity * 100m)
                    : 0m,
                // Sözleşme miktarının aşılması hata değil (ilave iş
                // olabilir) ama görülmesi gerekir.
                ExceedsContractQuantity:
                    contractQuantity > 0m && cumulativeQuantity > contractQuantity));
        }

        var sections = results
            .GroupBy(x => x.SectionId)
            .Select(group => new HakedisSectionSummary(
                SectionId: group.Key,
                MaterialAmount: Round(group.Sum(x => x.MaterialAmount)),
                LaborAmount: Round(group.Sum(x => x.LaborAmount)),
                OverheadAmount: Round(group.Sum(x => x.OverheadAmount)),
                CurrentAmount: Round(group.Sum(x => x.CurrentAmount)),
                PreviousAmount: Round(group.Sum(x => x.PreviousAmount)),
                CumulativeAmount: Round(group.Sum(x => x.CumulativeAmount))))
            .ToList();

        return new HakedisItemsResult(
            Items: results,
            Sections: sections,
            MaterialTotal: Round(results.Sum(x => x.MaterialAmount)),
            LaborTotal: Round(results.Sum(x => x.LaborAmount)),
            OverheadTotal: Round(results.Sum(x => x.OverheadAmount)),
            CurrentTotal: Round(results.Sum(x => x.CurrentAmount)),
            PreviousTotal: Round(results.Sum(x => x.PreviousAmount)),
            CumulativeTotal: Round(results.Sum(x => x.CumulativeAmount)));
    }

    /// <summary>
    /// Üst hesap girdileri.
    /// </summary>
    /// <param name="CumulativeWorkAmount">Kümülatif imalat tutarı
    /// (bu hakediş dahil).</param>
    /// <param name="CumulativeAdvanceMaterialAmount">Kümülatif açık
    /// ihzarat tutarı (mahsup edilenler düşülmüş).</param>
    /// <param name="PreviousTotalAmount">Önceki hakedişlerin toplamı —
    /// minha edilecek tutar.</param>
    /// <param name="PriceDifferenceAmount">Bu döneme ait fiyat farkı.</param>
    /// <param name="WithholdingNumerator">KDV tevkifat payı (ör. 4).</param>
    /// <param name="WithholdingDenominator">KDV tevkifat paydası (ör. 10).</param>
    /// <param name="IncomeTaxWithholdingRate">Stopaj oranı (%). Opsiyonel;
    /// sıfırsa uygulanmaz.</param>
    /// <param name="TotalDeductionAmount">Bu dönemde kesilecek kesinti
    /// toplamı.</param>
    public sealed record HakedisHeaderInput(
        decimal CumulativeWorkAmount,
        decimal CumulativeAdvanceMaterialAmount,
        decimal PreviousTotalAmount,
        decimal PriceDifferenceAmount,
        decimal VatRate,
        int WithholdingNumerator,
        int WithholdingDenominator,
        decimal IncomeTaxWithholdingRate,
        decimal TotalDeductionAmount);

    /// <summary>
    /// Üst hesabın tüm ara değerleri. Ara değerler ayrı ayrı döner ki
    /// NATURA çıktısındaki satırlar birebir yazılabilsin ve denetlenebilsin.
    /// </summary>
    public sealed record HakedisHeaderResult(
        decimal CumulativeWorkAmount,
        decimal CumulativeAdvanceMaterialAmount,
        decimal CumulativeTotalAmount,
        decimal PreviousTotalAmount,
        decimal CurrentAmount,
        decimal PriceDifferenceAmount,
        decimal TaxableAmount,
        decimal VatAmount,
        decimal WithholdingAmount,
        decimal DeclaredVatAmount,
        decimal IncomeTaxWithholdingAmount,
        decimal GrossPayableAmount,
        decimal TotalDeductionAmount,
        decimal NetPayableAmount);

    /// <summary>
    /// Üst hesap (NATURA sırasıyla):
    ///
    ///   kümülatif imalat + açık ihzarat = kümülatif toplam
    ///   − önceki hakedişler (minha)      = bu hakediş
    ///   + fiyat farkı                    = KDV matrahı
    ///   + KDV                            = brüt
    ///   − KDV tevkifatı                  (alıcı beyan eder)
    ///   − stopaj (varsa)
    ///   − kesintiler                     = tahsil edilecek
    ///
    /// İhzarat kümülatif tarafta durur: sonraki hakedişte imalata
    /// dönüştüğünde açık ihzarat azalır, imalat artar; toplam değişmez.
    /// Çift tahsilat böylece hesabın kendi yapısıyla engellenir.
    /// </summary>
    public static HakedisHeaderResult CalculateHeader(HakedisHeaderInput input)
    {
        var cumulativeWork = Round(input.CumulativeWorkAmount);
        var cumulativeAdvance = Round(input.CumulativeAdvanceMaterialAmount);
        var cumulativeTotal = Round(cumulativeWork + cumulativeAdvance);

        var previousTotal = Round(input.PreviousTotalAmount);
        var currentAmount = Round(cumulativeTotal - previousTotal);

        var priceDifference = Round(input.PriceDifferenceAmount);
        var taxableAmount = Round(currentAmount + priceDifference);

        var vatAmount = Round(taxableAmount * input.VatRate / 100m);

        // Tevkifat KDV'nin bir oranıdır (ör. 4/10); kesilen kısmı alıcı
        // beyan eder, kalanı satıcı.
        var withholding = input.WithholdingDenominator > 0
            ? Round(vatAmount * input.WithholdingNumerator / input.WithholdingDenominator)
            : 0m;

        var declaredVat = Round(vatAmount - withholding);

        // Stopaj matrahı KDV hariç tutardır.
        var incomeTaxWithholding = input.IncomeTaxWithholdingRate > 0m
            ? Round(taxableAmount * input.IncomeTaxWithholdingRate / 100m)
            : 0m;

        var gross = Round(taxableAmount + vatAmount);
        var deductions = Round(input.TotalDeductionAmount);

        var net = Round(gross - withholding - incomeTaxWithholding - deductions);

        return new HakedisHeaderResult(
            CumulativeWorkAmount: cumulativeWork,
            CumulativeAdvanceMaterialAmount: cumulativeAdvance,
            CumulativeTotalAmount: cumulativeTotal,
            PreviousTotalAmount: previousTotal,
            CurrentAmount: currentAmount,
            PriceDifferenceAmount: priceDifference,
            TaxableAmount: taxableAmount,
            VatAmount: vatAmount,
            WithholdingAmount: withholding,
            DeclaredVatAmount: declaredVat,
            IncomeTaxWithholdingAmount: incomeTaxWithholding,
            GrossPayableAmount: gross,
            TotalDeductionAmount: deductions,
            NetPayableAmount: net);
    }

    internal static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
