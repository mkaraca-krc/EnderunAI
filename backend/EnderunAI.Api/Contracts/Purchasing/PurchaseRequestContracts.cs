namespace EnderunAI.Api.Contracts.Purchasing;

public sealed record CreatePurchaseRequestItemRequest(
    string MaterialDescription,
    decimal Quantity,
    string Unit,
    DateTime? RequestedDeliveryDate,
    string? Notes,
    /// <summary>
    /// Talep edilen stok kartı. Opsiyonel ve en sonda: katalogda olmayan
    /// malzeme de talep edilebilmeli, mevcut çağıranlar da bozulmamalı.
    /// Seçilirse zincir mal kabule kadar kopmadan taşınır.
    /// </summary>
    Guid? InventoryItemId = null,
    /// <summary>
    /// Talebin dayandığı poz. Seçilirse ad ve birim pozdan kopyalanır;
    /// stok kartından bağımsızdır, ikisi bir arada da verilebilir.
    /// </summary>
    Guid? EngineeringPositionId = null,
    /// <summary>
    /// Talep edenin istediği marka. Tedarikçinin teklif ettiği
    /// markadan ayrıdır.
    /// </summary>
    string? RequestedBrand = null,
    /// <summary>
    /// Muadil kabul ediliyor mu.
    ///
    /// VARSAYILAN TRUE, çünkü bu alan sonradan eklendi ve varsayılanı
    /// false yapmak, alanı hiç göndermeyen eski çağıranların
    /// taleplerini bir anda geçersiz kılardı. Arayüz üç durumu da
    /// açıkça gönderiyor; boş geçme yalnızca eski çağıranlar için
    /// güvenli taraf.
    /// </summary>
    bool BrandIrrelevant = true);

public sealed record CreatePurchaseRequestRequest(
    Guid CompanyId,
    Guid ProjectId,
    DateTime RequestDate,
    DateTime? NeededByDate,
    string RequestedByName,
    string? Description,
    int Priority,
    IReadOnlyCollection<CreatePurchaseRequestItemRequest> Items);

public sealed record UpdatePurchaseRequestRequest(
    DateTime RequestDate,
    DateTime? NeededByDate,
    string RequestedByName,
    string? Description,
    int Priority,
    IReadOnlyCollection<CreatePurchaseRequestItemRequest> Items);

public sealed record CancelPurchaseRequestRequest(
    string? Reason);

/// <summary>
/// Talep red / düzeltmeye iade kararı.
/// </summary>
/// <param name="Reason">
/// Gerekçe — her iki kararda da ZORUNLU. Gerekçesiz red talep
/// sahibine neyi yanlış yaptığını söylemez; gerekçesiz iade ise
/// talebi ne yapacağı belli olmadan bekletir.
/// </param>
public sealed record PurchaseRequestDecisionRequest(string Reason);
