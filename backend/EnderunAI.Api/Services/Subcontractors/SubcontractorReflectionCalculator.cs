using EnderunAI.Api.Models;
using EnderunAI.Api.Formatting;

namespace EnderunAI.Api.Services.Subcontractors;

/// <summary>Taşeron hakedişine önerilecek tek bir yansıtma kalemi.</summary>
/// <param name="DeductionType">Kesinti türü —
/// <see cref="HakedisDeductionType"/> ordinali.</param>
/// <param name="Amount">Önerilen tutar (KDV dahil, işveren tarafındaki
/// kesintiyle aynı esas).</param>
/// <param name="Description">Hakediş satırında görünecek açıklama;
/// hesabın nasıl kurulduğunu içerir.</param>
/// <param name="Basis">Hesabın dayandığı sayılar — kullanıcı rakamı
/// göremeden onaylamak zorunda kalmasın.</param>
public sealed record SubcontractorReflection(
    int DeductionType,
    decimal Amount,
    string Description,
    string Basis);

/// <summary>
/// Yemek/konaklama yansıtması için tek bir alt kalem: işveren
/// hakedişindeki birim fiyat, taşeron işçilerinin puantaj adedi.
/// </summary>
/// <param name="Name">Alt kalem adı ("Öğlen", "Yatılı").</param>
/// <param name="EmployerUnitPrice">İşveren hakedişindeki birim fiyat
/// (KDV dahil) — yansıtma kâr merkezi değildir, aynı fiyatla geçer.</param>
/// <param name="SubcontractorQuantity">Taşeron işçilerinin o kalemdeki
/// puantaj adedi.</param>
public sealed record ReflectionLineInput(
    string Name,
    decimal EmployerUnitPrice,
    decimal SubcontractorQuantity);

/// <summary>
/// İşveren hakedişimizden kesilen İSG / yemek / konaklama bedelinin
/// taşerona yansıtılması.
///
/// Static ve veritabanısız — <see cref="Isg.OsgbDeductionCalculator"/>
/// ve bordro motorlarıyla aynı desen: hesap kuralı tek yerde durur,
/// test edilebilir, ve hangi ekrandan çağrılırsa çağrılsın aynı sayıyı
/// verir.
///
/// İLKE — HESAPLANAMAYAN DURUMDA ÖNERİ ÜRETİLMEZ (null döner). İşveren
/// kesintisi yoksa, taşeron işçisi yoksa ya da şantiye toplamı sıfırsa
/// uydurma tutar önerilmez: ön muhasebe boş satır görür ve mutabakata
/// göre kendi girer. Yanlış bir öneri, boş satırdan çok daha pahalıya
/// mal olur — çünkü onaylanır.
/// </summary>
public static class SubcontractorReflectionCalculator
{
    /// <summary>
    /// İSG yansıtması: işveren hakedişimizden kesilen İSG payının,
    /// taşeron işçilerinin şantiyedeki payı kadarı.
    ///
    /// Payda ŞANTİYEDE FİİLEN ÇALIŞAN sayısıdır (dönem içinde puantaj
    /// kaydı olan tekil kişi): işveren İSG kesintisi de fiilen çalışan
    /// üzerinden doğduğu için iki taraf aynı tabana oturur. Atama
    /// listesi kullanılsaydı, ay içinde hiç gelmemiş personel de
    /// taşeronun payını düşürürdü.
    /// </summary>
    /// <param name="responsibility">Sözleşmedeki İSG tiki. Taşerondaysa
    /// yansıtma yapılmaz — kendi masrafını kendi karşılıyor.</param>
    /// <param name="employerOhsDeduction">İşveren hakedişimizden bu
    /// dönem kesilen İSG tutarı.</param>
    /// <param name="subcontractorWorkerCount">Dönemde şantiyede puantajı
    /// olan taşeron işçisi sayısı.</param>
    /// <param name="siteWorkerCount">Dönemde şantiyede puantajı olan
    /// TOPLAM işçi sayısı (taşeron işçileri dahil).</param>
    public static SubcontractorReflection? CalculateOhs(
        SubcontractorResponsibility responsibility,
        decimal employerOhsDeduction,
        int subcontractorWorkerCount,
        int siteWorkerCount)
    {
        if (responsibility != SubcontractorResponsibility.Us)
            return null;

        if (employerOhsDeduction <= 0m)
            return null;

        if (subcontractorWorkerCount <= 0 || siteWorkerCount <= 0)
            return null;

        // Taşeron işçisi şantiye toplamından fazla olamaz; olduysa veri
        // tutarsızdır ve oranı 1'i aşan bir yansıtma üretmek yerine
        // öneri vermiyoruz.
        if (subcontractorWorkerCount > siteWorkerCount)
            return null;

        var share = (decimal)subcontractorWorkerCount / siteWorkerCount;
        var amount = decimal.Round(employerOhsDeduction * share, 2);

        if (amount <= 0m)
            return null;

        return new SubcontractorReflection(
            DeductionType: (int)HakedisDeductionType.OhsContribution,
            Amount: amount,
            Description: "İSG katılım payı yansıtması",
            Basis:
                $"İşveren İSG kesintisi {TurkishFormat.Amount(employerOhsDeduction)} × " +
                $"({subcontractorWorkerCount} taşeron işçisi / " +
                $"{siteWorkerCount} şantiye işçisi)");
    }

    /// <summary>
    /// Yemek yansıtması: taşeron işçilerinin puantaj adetleri × işveren
    /// hakedişindeki birim fiyat.
    /// </summary>
    public static SubcontractorReflection? CalculateMeal(
        SubcontractorResponsibility responsibility,
        IReadOnlyList<ReflectionLineInput> lines) =>
        CalculateFromLines(
            responsibility,
            lines,
            (int)HakedisDeductionType.Meal,
            "Yemek yansıtması");

    /// <summary>
    /// Konaklama yansıtması: taşeron işçilerinin puantaj adetleri ×
    /// işveren hakedişindeki birim fiyat.
    /// </summary>
    public static SubcontractorReflection? CalculateAccommodation(
        SubcontractorResponsibility responsibility,
        IReadOnlyList<ReflectionLineInput> lines) =>
        CalculateFromLines(
            responsibility,
            lines,
            (int)HakedisDeductionType.Accommodation,
            "Konaklama yansıtması");

    /// <summary>
    /// Alt kalemli yansıtmaların ortak hesabı. Birim fiyatı ya da adedi
    /// olmayan alt kalem sessizce atlanır (o kalemi hiç kullanmamışız
    /// demektir); hiçbiri tutar üretmezse öneri de üretilmez.
    /// </summary>
    private static SubcontractorReflection? CalculateFromLines(
        SubcontractorResponsibility responsibility,
        IReadOnlyList<ReflectionLineInput> lines,
        int deductionType,
        string description)
    {
        if (responsibility != SubcontractorResponsibility.Us)
            return null;

        if (lines.Count == 0)
            return null;

        var total = 0m;
        var parts = new List<string>();

        foreach (var line in lines)
        {
            if (line.EmployerUnitPrice <= 0m || line.SubcontractorQuantity <= 0m)
                continue;

            var amount = decimal.Round(
                line.EmployerUnitPrice * line.SubcontractorQuantity, 2);

            total += amount;
            parts.Add(
                $"{line.Name}: {TurkishFormat.Whole(line.SubcontractorQuantity)} × " +
                $"{TurkishFormat.Amount(line.EmployerUnitPrice)}");
        }

        if (total <= 0m)
            return null;

        return new SubcontractorReflection(
            DeductionType: deductionType,
            Amount: decimal.Round(total, 2),
            Description: description,
            Basis: string.Join(" + ", parts));
    }
}
