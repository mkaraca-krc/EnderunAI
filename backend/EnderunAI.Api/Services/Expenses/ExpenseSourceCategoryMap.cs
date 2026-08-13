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

    /// <summary>
    /// Gider kategorisinin MALİYET SINIFI — elle gider kaydı proje
    /// maliyetine girerken hangi bileşene sayılacağı.
    ///
    /// TERS EŞLEMENİN YANINDA DURUYOR: <see cref="ForCostClass"/> sınıftan
    /// kategoriye, bu kategoriden sınıfa çevirir. İkisi ayrı dosyalara
    /// dağılsaydı biri değişip diğeri unutulur ve aynı gider raporda
    /// başka, maliyet analizinde başka bileşende görünürdü.
    ///
    /// ELLE GİRİLEBİLEN KATEGORİLERİN HEPSİ GENEL GİDERDİR: kira,
    /// faturalar, kırtasiye, araç/yakıt, bakım, yemek, konaklama,
    /// harcırah, diğer — hiçbiri imalata doğrudan girmez. Malzeme,
    /// işçilik ve taşeron zaten elle girilemiyor (otomatik kaynaklardan
    /// gelir); yine de savunma amaçlı eşlenmiş durumdalar, çünkü bir
    /// kategori sonradan elle girişe açılırsa yanlış bileşende sessizce
    /// toplanması bu eşlemenin eksikliğinden olurdu.
    /// </summary>
    public static ProjectCostClass CostClassForCategory(string? categoryCode) =>
        categoryCode switch
        {
            ExpenseCategoryCatalog.Material => ProjectCostClass.Material,
            ExpenseCategoryCatalog.Labor => ProjectCostClass.Labor,
            ExpenseCategoryCatalog.Subcontractor => ProjectCostClass.SubcontractorLabor,
            _ => ProjectCostClass.Overhead
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
