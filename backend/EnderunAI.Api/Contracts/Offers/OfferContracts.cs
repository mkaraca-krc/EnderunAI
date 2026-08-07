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

    IReadOnlyCollection<CreateOfferItemRequest> Items);

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
