using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Notifications.Sources;

/// <summary>
/// ARAÇ YENİLEME HATIRLATMALARI: muayene, sigorta, kasko, MTV ve
/// periyodik bakım.
///
/// HER TÜR AYRI BİLDİRİM: "aracın 3 işi var" diye toplamak özet için
/// iyi olurdu ama hatırlatma için değil — sigorta yenilenince o satır
/// kapanmalı, muayene açık kalmalı.
///
/// OTOMATİK KAPANIŞ TARİHTEN GELİR: kullanıcı araç kartındaki yenileme
/// tarihini ileri aldığında o tür aday üretmemeye başlar ve motor
/// <see cref="OwnedTypes"/> sayesinde kaydı kapatır. Ayrı bir "kapat"
/// düğmesi olsaydı yenileme yapılır ama bildirim açık kalır, ya da
/// tersine bildirim kapatılır ama yenileme unutulurdu.
///
/// TEKİLLEŞTİRME (Tür, KaynakId=araç, Dönem=hedef tarih): aynı aracın
/// aynı yenilemesi için tek kayıt. Dönem anahtarı tarih olduğu için
/// gelecek yılın muayenesi YENİ bir bildirimdir, eskisinin tekrarı
/// değil.
///
/// TUTAR TAŞIMAZ: araç yenileme tarihleri tutar içermiyor, bu yüzden
/// AmountDetail boş. Kira/MTV tutarı gider kaydının işi.
/// </summary>
public sealed class VehicleRenewalNotificationSource(AppDbContext db) : INotificationSource
{
    public const string InspectionTypeKey = "vehicle.inspection.due";
    public const string InsuranceTypeKey = "vehicle.insurance.due";
    public const string CascoTypeKey = "vehicle.casco.due";
    public const string MotorTaxTypeKey = "vehicle.motortax.due";
    public const string MaintenanceTypeKey = "vehicle.maintenance.due";

    public string Key => "arac_yenileme";

    public IReadOnlyCollection<string> OwnedTypes =>
    [
        InspectionTypeKey,
        InsuranceTypeKey,
        CascoTypeKey,
        MotorTaxTypeKey,
        MaintenanceTypeKey
    ];

    public async Task<IReadOnlyList<NotificationCandidate>> BuildAsync(
        NotificationScanContext context, CancellationToken cancellationToken)
    {
        // Yenileme işleri vade kalemi gibi davranır: 7 gün kala uyarı,
        // yaklaştıkça şiddetlenir, geçmişse kritik. Eşik burada
        // uydurulmuyor, NotificationWindow'dan geliyor.
        var until = context.Today.AddDays(NotificationWindow.DueEarlyDays);

        var vehicles = await db.Vehicles
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == context.CompanyId &&
                x.IsActive &&
                (x.InspectionDueDate != null && x.InspectionDueDate <= until ||
                 x.InsuranceRenewalDate != null && x.InsuranceRenewalDate <= until ||
                 x.CascoRenewalDate != null && x.CascoRenewalDate <= until ||
                 x.MotorTaxDueDate != null && x.MotorTaxDueDate <= until ||
                 x.NextMaintenanceDate != null && x.NextMaintenanceDate <= until))
            .Select(x => new
            {
                x.Id,
                x.PlateNumber,
                x.InspectionDueDate,
                x.InsuranceRenewalDate,
                x.CascoRenewalDate,
                x.MotorTaxDueDate,
                x.NextMaintenanceDate
            })
            .ToListAsync(cancellationToken);

        var candidates = new List<NotificationCandidate>();

        foreach (var vehicle in vehicles)
        {
            void Add(string type, DateTime? date, string label)
            {
                if (date is not DateTime due || due.Date > until)
                    return;

                var days = (due.Date - context.Today.Date).Days;

                candidates.Add(new NotificationCandidate(
                    type,
                    vehicle.Id,

                    // Dönem anahtarı hedef tarih: gelecek yılın aynı
                    // işi YENİ bildirimdir.
                    due.ToString("yyyy-MM-dd"),

                    $"{vehicle.PlateNumber} — {label} {NotificationWindow.DueLabel(days)}",
                    $"Son tarih {due:dd.MM.yyyy}",
                    NotificationWindow.SeverityForDue(days),
                    "/filo",
                    due,
                    null,
                    null,
                    PermissionCatalog.Keys.VehicleView));
            }

            Add(InspectionTypeKey, vehicle.InspectionDueDate, "muayene");
            Add(InsuranceTypeKey, vehicle.InsuranceRenewalDate, "sigorta yenileme");
            Add(CascoTypeKey, vehicle.CascoRenewalDate, "kasko yenileme");
            Add(MotorTaxTypeKey, vehicle.MotorTaxDueDate, "MTV ödemesi");
            Add(MaintenanceTypeKey, vehicle.NextMaintenanceDate, "periyodik bakım");
        }

        return candidates;
    }
}
