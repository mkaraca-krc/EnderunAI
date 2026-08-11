using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.Expenses;

/// <summary>
/// Otomatik kaynakların hangi gider kategorisine düştüğü.
///
/// TEK YER: maliyet defterindeki bir satırın kategorisi buradan
/// çıkar. Eşleme rapor içine gömülseydi, yeni bir kaynak eklendiğinde
/// (ör. araç bakım modülü) kategori ataması raporun içinde kaybolur
/// ve kimse hangi kalemin nereye düştüğünü göremezdi.
/// </summary>
public static class ExpenseSourceCategoryMap
{
    /// <summary>
    /// Maliyet defteri satırının kategorisi. Referans türü biliniyorsa
    /// ondan, bilinmiyorsa maliyet sınıfından çıkar.
    /// </summary>
    public static string ForLedgerRow(string? referenceType, ProjectCostClass costClass) =>
        referenceType switch
        {
            "StockMovement" => ExpenseCategoryCatalog.Material,
            "SubcontractorLedgerEntry" => ExpenseCategoryCatalog.Subcontractor,

            // Görev masrafının üç kalemi ayrı kategorilere düşüyor:
            // "şantiyeye ne kadar yol, ne kadar konaklama, ne kadar
            // harcırah" sorusu tek toplamdan geri üretilemez.
            "PersonnelDutyTravel" => ExpenseCategoryCatalog.Travel,
            "PersonnelDutyAccommodation" => ExpenseCategoryCatalog.Accommodation,
            "PersonnelDutyAllowance" => ExpenseCategoryCatalog.Allowance,

            "ToolServiceRequest" => ExpenseCategoryCatalog.Maintenance,

            _ => ForCostClass(costClass)
        };

    /// <summary>
    /// Maliyet sınıfından kategori. Genel gider "diğer"e düşer:
    /// bilinmeyen bir gideri kiraya ya da yakıta yazmak, kategori
    /// dağılımını sessizce yanıltırdı.
    /// </summary>
    public static string ForCostClass(ProjectCostClass costClass) => costClass switch
    {
        ProjectCostClass.Material => ExpenseCategoryCatalog.Material,
        ProjectCostClass.Labor => ExpenseCategoryCatalog.Labor,
        ProjectCostClass.SubcontractorLabor => ExpenseCategoryCatalog.Subcontractor,
        _ => ExpenseCategoryCatalog.Other
    };

    /// <summary>Kaynağın ekranda görünen adı.</summary>
    public static string SourceLabel(string? referenceType) => referenceType switch
    {
        null or "" => "Proje maliyet kaydı",
        "StockMovement" => "Depo sarfı",
        "SupplierInvoice" => "Tedarikçi faturası",
        "SubcontractorLedgerEntry" => "Taşeron",
        "ToolServiceRequest" => "Alet servisi",
        _ when referenceType.StartsWith("PersonnelDuty", StringComparison.Ordinal)
            => "Görevlendirme",
        _ => referenceType
    };
}
