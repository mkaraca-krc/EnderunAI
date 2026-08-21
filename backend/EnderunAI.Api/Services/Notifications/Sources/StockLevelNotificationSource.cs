using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Inventory;

namespace EnderunAI.Api.Services.Notifications.Sources;

/// <summary>
/// ASGARİ STOK SEVİYESİ UYARISI.
///
/// Kaynağı <see cref="StockLevelAlertService"/>: uyarının eşiği burada
/// yeniden yazılmıyor, ekranla AYNI hesaptan okunuyor. İkinci bir eşik
/// açılsaydı ekranda kritik görünen kalem bildirimde sessiz kalabilirdi.
///
/// DÖNEM ANAHTARI SABİT ("acik"): asgari stok bir VADE değil, bir DURUM.
/// Vade kalemlerinde dönem anahtarı hedef tarihtir çünkü gelecek yılın
/// muayenesi yeni bir iştir. Burada öyle bir tarih yok; anahtar güne
/// bağlansaydı aynı malzeme için her gece yeni bir kayıt açılır,
/// "okundu" bilgisi her gece kaybolur ve bildirim merkezi aynı kalemin
/// kopyalarıyla dolardı.
///
/// KENDİLİĞİNDEN KAPANIR: mal girince kalem aday üretmez, motor
/// <see cref="OwnedTypes"/> üzerinden kaydı kapatır. Ayrı bir "kapat"
/// düğmesi olsaydı stok yerine geldiği hâlde uyarı açık kalırdı.
/// </summary>
public sealed class StockLevelNotificationSource(StockLevelAlertService alerts)
    : INotificationSource
{
    public const string BelowMinimumTypeKey = "inventory.below_minimum";

    public string Key => "stok_seviyesi";

    public IReadOnlyCollection<string> OwnedTypes => [BelowMinimumTypeKey];

    public async Task<IReadOnlyList<NotificationCandidate>> BuildAsync(
        NotificationScanContext context,
        CancellationToken cancellationToken)
    {
        var rows = await alerts.BuildAsync(
            context.CompanyId,
            warehouseId: null,
            belowMinimumOnly: true,
            cancellationToken);

        return rows
            .Select(row =>
            {
                // Tükenmiş stok kritik, asgarinin altındaki uyarı.
                // Tek kademe olsaydı "3 adet kaldı" ile "hiç kalmadı"
                // aynı renkte görünür ve önce hangisine koşulacağı
                // kaybolurdu.
                var severity = row.IsDepleted
                    ? NotificationSeverity.Critical
                    : NotificationSeverity.Warning;

                var detail = row.IsDepleted
                    ? $"{row.WarehouseName} deposunda kalmadı (asgari {row.MinimumQuantity:0.####} {row.Unit})."
                    : $"{row.WarehouseName} deposunda {row.CurrentQuantity:0.####} {row.Unit} kaldı " +
                      $"(asgari {row.MinimumQuantity:0.####}).";

                var suggestion = row.SuggestedQuantity is decimal quantity
                    ? $" Önerilen sipariş: {quantity:0.####} {row.Unit}."
                    : " Azami seviye tanımlı olmadığı için sipariş miktarı önerilemedi.";

                return new NotificationCandidate(
                    BelowMinimumTypeKey,

                    // Kaynak seviye SATIRI: aynı malzemenin iki farklı
                    // depodaki eksiği iki ayrı bildirimdir, biri
                    // kapanırken diğeri açık kalmalı.
                    row.Id,
                    "acik",
                    $"{row.ItemCode} — {row.ItemName} asgari stoğun altında",
                    detail + suggestion,
                    severity,
                    "/depo-stok/stok-seviyeleri",
                    null,
                    null,
                    null,
                    PermissionCatalog.Keys.InventoryView);
            })
            .ToList();
    }
}
