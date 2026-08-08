using EnderunAI.Api.Models;
namespace EnderunAI.Api.Contracts.Offers;

public sealed record CreateOfferItemRequest(
    string? PositionNumber,

    Guid? EngineeringPositionId,
    Guid? EngineeringRecipeId,
    int? RecipeVersion,

    string Description,

    Guid? ManufacturerPriceListItemId,
    string? ManufacturerName,
    string? ProductCode,
    string? Brand,
    string? Model,

    decimal Quantity,
    string Unit,

    decimal ListPrice,
    decimal DiscountRate,

    decimal FreightRate,
    decimal WasteRate,
    decimal FinanceRate,
    decimal GeneralExpenseRate,
    decimal ProfitRate,

    string? Notes,

    // Satış fiyatının malzeme/montaj/GG dağılımı. Verilmezse tutarın
    // tamamı malzemeye yazılır; toplam değişmez. Eski istemciler bu
    // alanları hiç göndermeden çalışmaya devam eder.
    decimal? MaterialUnitPrice = null,
    decimal? LaborUnitPrice = null,
    decimal? OverheadUnitPrice = null);

/// <summary>
/// Pozdan teklif kalemi üretme isteği.
/// </summary>
/// <param name="EngineeringPositionId">Kaynak poz.</param>
/// <param name="Quantity">Metraj.</param>
/// <param name="Source">Fiyat kaynağı: resmî yıl fiyatı ya da reçete
/// analizi.</param>
/// <param name="Year">Resmî fiyatta yıl; boşsa en yeni yıl.</param>
/// <param name="Institution">Resmî fiyatta kurum; boşsa hepsi.</param>
/// <param name="ProfitRate">Reçete analizinde uygulanacak kâr oranı.</param>
/// <param name="LaborHourRate">Reçete analizinde işçilik saat ücreti.</param>
/// <param name="MachineHourRate">Reçete analizinde makine saat ücreti.</param>
public sealed record OfferItemFromPositionRequest(
    Guid EngineeringPositionId,
    decimal Quantity,
    OfferPositionPriceSource Source,
    int? Year,
    int? Institution,
    decimal ProfitRate,
    decimal LaborHourRate,
    decimal MachineHourRate);

/// <summary>Pozdan kalem üretirken fiyatın nereden geleceği.</summary>
public enum OfferPositionPriceSource
{
    /// <summary>Kurumun yayımladığı yıl birim fiyatı.</summary>
    OfficialYearPrice = 0,

    /// <summary>Pozun reçetesinden malzeme + işçilik analizi.</summary>
    RecipeAnalysis = 1
}

/// <summary>Teklifi icmale aktarma isteği.</summary>
/// <param name="ProjectId">Hedef proje; boşsa teklifin projesi.</param>
/// <param name="Name">İcmal adı; boşsa teklif başlığından üretilir.</param>
public sealed record TransferOfferToBoqRequest(
    Guid? ProjectId,
    string? Name);

public sealed record CreateOfferRequest(
    Guid CompanyId,
    Guid? ProjectId,
    Guid? CustomerId,

    string Title,

    DateTime OfferDate,
    DateTime? ValidUntil,

    string Currency,
    decimal ExchangeRate,

    string? Description,
    string? Notes,

    IReadOnlyCollection<CreateOfferItemRequest> Items,

    // Takip alanları isteğe bağlı ve sonda: mevcut istemciler bu
    // alanları göndermeden aynen çalışmaya devam eder.
    Guid? CounterpartyCurrentAccountId = null,
    OfferCounterpartyRole CounterpartyRole = OfferCounterpartyRole.Unspecified,
    OfferKind Kind = OfferKind.Unspecified);

/// <summary>Teklifin takip künyesi (kime verildi, hangi tipte).</summary>
/// <param name="CounterpartyCurrentAccountId">İşveren ya da ana yüklenici carisi.</param>
/// <param name="CounterpartyRole">Karşı tarafın rolü.</param>
/// <param name="Kind">Birim fiyatlı / anahtar teslim.</param>
public sealed record UpdateOfferTrackingRequest(
    Guid? CounterpartyCurrentAccountId,
    OfferCounterpartyRole CounterpartyRole,
    OfferKind Kind);

/// <summary>Teklif durumu değiştirme isteği.</summary>
/// <param name="Status">Hedef durum.</param>
/// <param name="LostReason">Yalnız Kaybedildi'de zorunlu.</param>
/// <param name="LostReasonNote">Kaybın serbest açıklaması.</param>
/// <param name="Note">Karar gerekçesi.</param>
public sealed record ChangeOfferStatusRequest(
    OfferStatus Status,
    OfferLostReason LostReason = OfferLostReason.None,
    string? LostReasonNote = null,
    string? Note = null);

public sealed record CalculateOfferItemRequest(
    decimal Quantity,
    decimal ListPrice,
    decimal DiscountRate,
    decimal FreightRate,
    decimal WasteRate,
    decimal FinanceRate,
    decimal GeneralExpenseRate,
    decimal ProfitRate);

public sealed record CalculateOfferItemResponse(
    decimal NetPurchasePrice,
    decimal UnitCost,
    decimal UnitSalesPrice,
    decimal CostTotal,
    decimal SalesTotal,
    decimal ProfitTotal);

/// <summary>
/// Kazanılan teklif için sözleşme künyesi ve proje isteği.
///
/// ProjectId doluysa EK İŞtir: o projenin sözleşme künyesi korunur,
/// yalnız ek icmal açılır. Boşsa künye yeni projeyi kurar.
/// </summary>
public sealed record CreateOfferContractRequest(
    Guid? ProjectId,
    Guid? BranchId,
    string? Code,
    string? Name,
    string? ContractNumber,
    DateTime? ContractDate,
    decimal? ContractAmount,
    ProjectContractType? ContractType,
    DateTime? PlannedStartDate,
    DateTime? PlannedEndDate,
    decimal CashRetentionRate = 0m,
    decimal VatRate = 20m,
    decimal WithholdingTaxRate = 0m,
    decimal MaterialDeductionRate = 0m,
    ProjectProgressPaymentPeriod ProgressPaymentPeriod
        = ProjectProgressPaymentPeriod.Unspecified,
    string? PaymentTerms = null,
    string? City = null,
    string? District = null,
    string? Address = null,
    bool TransferToBoq = true,
    string? BoqName = null);
