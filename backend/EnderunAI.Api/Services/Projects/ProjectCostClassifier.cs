using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.Projects;

/// <summary>
/// Maliyet kaydının sınıfını KAYNAĞINDAN türetir.
///
/// Saf ve veritabanısız: kural metinden ve enum'dan ibarettir, böylece
/// hem stok sarfı hem fatura hem geriye dönük sınıflama aynı kuralı
/// kullanır. Kural iki yere kopyalansaydı biri güncellenip diğeri
/// unutulur, aynı maliyet nereden geldiğine göre farklı sınıflanırdı.
///
/// Kullanıcı sınıf seçmez; elle girilen kayıtta bile sınıf kullanıcının
/// seçtiği maliyet türünden eşlenir.
/// </summary>
public static class ProjectCostClassifier
{
    /// <summary>
    /// Kendi personelimizin ücret giderleri. Bordro normalde fatura
    /// olarak girilmez ama hesap planında karşılığı varsa işçilik
    /// sayılmalı.
    /// </summary>
    private static readonly string[] LaborAccountPrefixes =
    [
        "740.01", "770.01", "720"
    ];

    /// <summary>
    /// Dışarıdan sağlanan işçilik = taşeron. Hesap planında ayrı bir
    /// kırılımı var (740.03.11) ve bunu "genel gider" saymak, taşeron
    /// işçiliğini icmalin işçilik bileşeninden gizler; karşılaştırma da
    /// GG&amp;K'yı şişmiş, işçiliği eksik gösterirdi.
    /// </summary>
    private static readonly string[] SubcontractorAccountPrefixes =
    [
        "740.03.11"
    ];

    /// <summary>Depo sarfı her zaman malzemedir.</summary>
    public static ProjectCostClass ForStockIssue() => ProjectCostClass.Material;

    /// <summary>
    /// Tedarikçi faturası. ALIŞ (stok) faturası malzeme; GİDER faturası
    /// kalemin yazıldığı hesaba göre sınıflanır.
    /// </summary>
    public static ProjectCostClass ForSupplierInvoice(
        SupplierInvoiceType invoiceType,
        string? expenseAccountCode)
    {
        if (invoiceType == SupplierInvoiceType.Stock)
            return ProjectCostClass.Material;

        return ForExpenseAccount(expenseAccountCode);
    }

    /// <summary>
    /// Gider hesabı kodundan sınıf. Hesap bilinmiyorsa genel gider
    /// kabul edilir: bilinmeyen bir gideri malzeme ya da işçilik saymak,
    /// karşılaştırmayı sessizce yanıltırdı.
    /// </summary>
    public static ProjectCostClass ForExpenseAccount(string? accountCode)
    {
        var code = accountCode?.Trim();

        if (string.IsNullOrEmpty(code))
            return ProjectCostClass.Overhead;

        if (SubcontractorAccountPrefixes.Any(prefix =>
                code.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return ProjectCostClass.SubcontractorLabor;
        }

        if (LaborAccountPrefixes.Any(prefix =>
                code.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return ProjectCostClass.Labor;
        }

        return ProjectCostClass.Overhead;
    }


    public static string Name(ProjectCostClass costClass) => costClass switch
    {
        ProjectCostClass.Material => "Malzeme",
        ProjectCostClass.Labor => "İşçilik",
        ProjectCostClass.SubcontractorLabor => "İşçilik (Taşeron)",
        ProjectCostClass.Overhead => "Genel Gider",
        _ => costClass.ToString()
    };
}
