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

    // ---------- İhzarat ----------

    /// <summary>
    /// Bir ihzarat kaleminin hesaba giren hali.
    /// </summary>
    /// <param name="PreviouslyOffsetAmount">Önceki hakedişlerde mahsup
    /// edilmiş toplam.</param>
    public sealed record AdvanceMaterialInput(
        Guid Id,
        string PositionCode,
        decimal Quantity,
        decimal UnitPrice,
        decimal ValuationRate,
        decimal PreviouslyOffsetAmount);

    public sealed record AdvanceMaterialResult(
        Guid Id,
        string PositionCode,
        decimal Amount,
        decimal PreviouslyOffsetAmount,
        decimal OpenAmount);

    /// <summary>
    /// İhzarat tutarı: miktar × birim fiyat × bedellendirme oranı.
    /// Açık bakiye, önceki mahsuplar düşülerek bulunur ve negatife
    /// düşemez.
    /// </summary>
    public static AdvanceMaterialResult CalculateAdvanceMaterial(
        AdvanceMaterialInput input)
    {
        var quantity = Math.Max(0m, input.Quantity);
        var unitPrice = Math.Max(0m, input.UnitPrice);
        var rate = Math.Clamp(input.ValuationRate, 0m, 100m);

        var amount = Round(quantity * unitPrice * rate / 100m);
        var offset = Math.Max(0m, Round(input.PreviouslyOffsetAmount));

        return new AdvanceMaterialResult(
            Id: input.Id,
            PositionCode: input.PositionCode,
            Amount: amount,
            PreviouslyOffsetAmount: offset,
            OpenAmount: Math.Max(0m, Round(amount - offset)));
    }

    /// <summary>
    /// Bir ihzarat kalemi için önerilen mahsup tutarı.
    ///
    /// Öneri, pozun bu dönem imalata dönen tutarıyla açık ihzarat
    /// bakiyesinin küçüğüdür: imalatı aşan mahsup anlamsız, bakiyeyi
    /// aşan mahsup ise çift tahsilat olurdu.
    /// </summary>
    public static decimal SuggestOffset(
        decimal openAdvanceAmount, decimal currentWorkAmountForPosition) =>
        Math.Max(0m, Math.Min(
            Round(openAdvanceAmount),
            Round(currentWorkAmountForPosition)));

    /// <summary>
    /// Mahsup tutarının geçerliliği. Açık bakiyeyi aşan mahsup hiçbir
    /// koşulda kabul edilmez — çift tahsilatın engeli burasıdır.
    /// </summary>
    /// <returns>Hata mesajı; geçerliyse null.</returns>
    public static string? ValidateOffset(
        string positionCode, decimal openAdvanceAmount, decimal requestedOffset)
    {
        if (requestedOffset < 0m)
            return $"'{positionCode}' ihzarat mahsubu negatif olamaz.";

        if (Round(requestedOffset) > Round(openAdvanceAmount))
        {
            return $"'{positionCode}' için mahsup ({requestedOffset:N2}) açık " +
                   $"ihzarat bakiyesini ({openAdvanceAmount:N2}) aşamaz. " +
                   "Aşan mahsup aynı işin iki kez tahsil edilmesi demek olurdu.";
        }

        return null;
    }

    // ---------- Kesintiler ----------

    /// <summary>Alt kalemli kesintinin tek satırı.</summary>
    public sealed record DeductionLineInput(
        string Name,
        decimal UnitPrice,
        decimal Quantity,
        decimal VatRate);

    public sealed record DeductionLineResult(
        string Name,
        decimal UnitPrice,
        decimal Quantity,
        decimal VatRate,
        decimal NetAmount,
        decimal VatAmount,
        decimal GrossAmount);

    /// <summary>
    /// Bir kesinti kaleminin girdileri.
    /// </summary>
    /// <param name="CumulativeBaseAmount">Kesintinin uygulanacağı
    /// kümülatif taban (genelde kümülatif hakediş tutarı).</param>
    /// <param name="PreviousAmount">Önceki hakedişlerde bu türden
    /// kesilmiş toplam.</param>
    /// <param name="ManualAmount">Elle girilen tutar; verilirse oran
    /// yok sayılır.</param>
    /// <param name="Lines">Alt kalemler; doluysa tutar bunlardan gelir.</param>
    public sealed record DeductionInput(
        int DeductionType,
        string Description,
        decimal Rate,
        decimal CumulativeBaseAmount,
        decimal PreviousAmount,
        decimal? ManualAmount = null,
        IReadOnlyList<DeductionLineInput>? Lines = null);

    public sealed record DeductionResult(
        int DeductionType,
        string Description,
        decimal Rate,
        decimal CumulativeBaseAmount,
        decimal PreviousAmount,
        decimal CumulativeAmount,
        decimal Amount,
        bool IsManualAmount,
        IReadOnlyList<DeductionLineResult> Lines);

    /// <summary>
    /// Bir kesinti kalemini hesaplar.
    ///
    /// KÜMÜLATİF MANTIK: kümülatif kesinti = kümülatif taban × oran;
    /// bu dönem kesilecek = kümülatif − önceki dönemlerde kesilen.
    /// "Bu dönem tutarı × oran" yaklaşımı, oran dönemler arasında
    /// değiştiğinde geçmişi düzeltemezdi.
    ///
    /// Alt kalemler (yemek, konaklama, İSG) varsa tutar oranla değil
    /// birim fiyat × adet × KDV toplamıyla bulunur; bu kalemler zaten
    /// dönemseldir, kümülatif düzeltmeye tabi değildir.
    /// </summary>
    public static DeductionResult CalculateDeduction(DeductionInput input)
    {
        var lines = (input.Lines ?? [])
            .Select(CalculateDeductionLine)
            .ToList();

        var previous = Math.Max(0m, Round(input.PreviousAmount));

        if (lines.Count > 0)
        {
            var lineTotal = Round(lines.Sum(x => x.GrossAmount));

            return new DeductionResult(
                DeductionType: input.DeductionType,
                Description: input.Description,
                Rate: input.Rate,
                CumulativeBaseAmount: 0m,
                PreviousAmount: previous,
                CumulativeAmount: Round(previous + lineTotal),
                Amount: lineTotal,
                IsManualAmount: false,
                Lines: lines);
        }

        if (input.ManualAmount is decimal manual)
        {
            var amount = Math.Max(0m, Round(manual));

            return new DeductionResult(
                input.DeductionType, input.Description, input.Rate,
                CumulativeBaseAmount: Round(input.CumulativeBaseAmount),
                PreviousAmount: previous,
                CumulativeAmount: Round(previous + amount),
                Amount: amount,
                IsManualAmount: true,
                Lines: lines);
        }

        var cumulativeBase = Math.Max(0m, Round(input.CumulativeBaseAmount));
        var cumulativeAmount = Round(cumulativeBase * input.Rate / 100m);

        // Kümülatif kesinti önceki toplamın altına düşerse (taban
        // küçüldü) bu dönemde geri ödeme yapılmaz; kesinti sıfırlanır.
        var currentAmount = Math.Max(0m, Round(cumulativeAmount - previous));

        return new DeductionResult(
            DeductionType: input.DeductionType,
            Description: input.Description,
            Rate: input.Rate,
            CumulativeBaseAmount: cumulativeBase,
            PreviousAmount: previous,
            CumulativeAmount: Round(previous + currentAmount),
            Amount: currentAmount,
            IsManualAmount: false,
            Lines: lines);
    }

    private static DeductionLineResult CalculateDeductionLine(DeductionLineInput line)
    {
        var unitPrice = Math.Max(0m, line.UnitPrice);
        var quantity = Math.Max(0m, line.Quantity);
        var vatRate = Math.Max(0m, line.VatRate);

        var net = Round(unitPrice * quantity);
        var vat = Round(net * vatRate / 100m);

        return new DeductionLineResult(
            Name: line.Name,
            UnitPrice: unitPrice,
            Quantity: quantity,
            VatRate: vatRate,
            NetAmount: net,
            VatAmount: vat,
            GrossAmount: Round(net + vat));
    }

    // ---------- Barter ----------

    /// <summary>
    /// Barter bakiyesi: hakedişlerden kesilen barter tutarı işverenden
    /// mal/hizmet olarak alınacak alacaktır.
    ///
    /// Bakiye = kümülatif kesilen − teslim alınan. Teslim alınan
    /// kesilenden fazla olamaz; fazlası hatalı kayıt demektir ve
    /// bakiye negatife düşürülmez.
    /// </summary>
    public static decimal CalculateBarterBalance(
        decimal cumulativeDeducted, decimal totalReceived) =>
        Math.Max(0m, Round(cumulativeDeducted) - Round(totalReceived));

    internal static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
