namespace EnderunAI.Api.Services.Inventory;

using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.EntityFrameworkCore;

/// ALAN ADLARI ÖN YÜZDEKİ SÖZLEŞMEDEN ALINDI.
///
/// `hr-asset.service.ts` bu uçları zaten çağırıyordu (uç yazılmadığı
/// için kırık çağrı çizgisinde duruyorlardı). Sunucu tarafına ayrı
/// bir adlandırma koymak, aynı iş için İKİ SÖZLEŞME yaşatırdı.
///
/// `Miktar` ön yüzde YOKTU — tek kalem varsayılıyordu. Zorunlu
/// yapmak mevcut çağrıyı kırardı; bu yüzden isteğe bağlı ve
/// varsayılanı 1. Sarf malzemede (10 çift eldiven) miktar gerekli.
public sealed record DepodanZimmetIstegi(
    Guid CompanyId,
    Guid PersonnelId,
    Guid WarehouseId,
    Guid InventoryItemId,
    DateTime AssignmentDate,
    Guid? ProjectId = null,
    decimal? Miktar = null,
    string? SerialNumber = null,
    DateTime? PlannedReturnDate = null,
    string? ConditionAtAssignment = null,
    string? DocumentPath = null,
    string? Notes = null);

public sealed record ZimmetIadeIstegi(
    DateTime? ReturnDate,
    string? ConditionAtReturn,
    string? DocumentPath,
    string? Notes,
    DateTime? RowVersion);

/// <summary>
/// Ön yüzdeki `AssetInventoryActionResponse` ile birebir.
/// </summary>
public sealed record ZimmetSonucu(
    Guid AssetAssignmentId,
    Guid WarehouseId,
    Guid InventoryItemId,
    Guid StockMovementId,
    string ReferenceNumber,
    string Message);

/// <summary>
/// İPTAL — yanlış kişiye verilmiş kaydın düzeltilmesi.
///
/// Gerekçe ZORUNLU. İptal, bu akıştaki en çok suistimal edilebilecek
/// eylem: malzeme kişide kalırken kayıt silinmiş gibi görünebilir.
/// Gerekçesiz iptale izin vermek, denetim kaydını "birisi iptal etti"
/// düzeyinde bırakırdı.
/// </summary>
public sealed record ZimmetIptalIstegi(
    string Gerekce,
    DateTime? RowVersion);

public interface IDepodanZimmetService
{
    Task<ZimmetSonucu> ZimmetVerAsync(DepodanZimmetIstegi istek, CancellationToken cancellationToken);

    Task IadeAlAsync(Guid zimmetId, ZimmetIadeIstegi istek, CancellationToken cancellationToken);

    Task IptalEtAsync(Guid zimmetId, ZimmetIptalIstegi istek, CancellationToken cancellationToken);
}

/// <summary>
/// DEPODAN ZİMMET — malzeme depo stoğundan düşer, şirket varlığından ÇIKMAZ.
///
/// ─────────────────────────────────────────────────────────────────
/// "ZİMMET KONUMU" DİYE BİR YER AÇILMADI — ÖLÇÜMLE VAZGEÇİLDİ
///
/// İstenen "malzeme zimmet konumuna taşınsın" idi ve üç seviyeli
/// konum yapısının (bölge/raf/kat) buna uygun olduğu düşünülüyordu.
/// Ölçüm bunu çürüttü: o üç seviye MİKTAR TUTMUYOR. Miktar yalnız
/// `warehouse_stocks` üzerinde (depo, kalem) çiftinde duruyor;
/// bölge/raf/kat ise stok kartının YERLEŞİM bilgisi. Oraya bir
/// miktar taşımak mümkün değil.
///
/// Ayrı bir "Zimmet" deposu açmak da düşünüldü ve bırakıldı: o, tam
/// olarak "yeni mekanizma kurma" demek olurdu.
///
/// Bunun yerine açık zimmet KAYDININ KENDİSİ konumdur. İstenen her
/// şey karşılanıyor ve tek doğruluk kaynağı var:
///
///   depo mevcudu   = warehouse_stocks.Quantity          (düştü)
///   zimmette       = açık zimmetlerin çıkış hareketleri (arttı)
///   şirket varlığı = ikisinin toplamı                   (DEĞİŞMEDİ)
///
/// Miktar için `HrAssetAssignment`e yeni bir alan AÇILMADI: miktar
/// zaten çıkış hareketinde duruyor ve kayıt ona `IssueStockMovementId`
/// ile bağlı. İkinci bir kopya, zamanla sapabilecek ikinci bir
/// doğruluk kaynağı olurdu.
/// ─────────────────────────────────────────────────────────────────
/// </summary>
public sealed class DepodanZimmetService(
    AppDbContext db,
    IStockSaleIssuer stockIssuer,
    IStockConsumptionPoster consumptionPoster,
    ICurrentDataScopeService dataScope,
    ICurrentUserService currentUser) : IDepodanZimmetService
{
    public async Task<ZimmetSonucu> ZimmetVerAsync(
        DepodanZimmetIstegi istek, CancellationToken cancellationToken)
    {
        var miktar = istek.Miktar ?? 1m;

        if (miktar <= 0m)
            throw new InvalidOperationException("Zimmet miktarı sıfırdan büyük olmalıdır.");

        var kapsam = await KapsamAsync(cancellationToken);

        // KAPSAM KAPISI ÜÇ EKSENDE: şirket, depo, personel.
        //
        // Üçü ayrı ayrı gerekli. Yalnız şirkete bakmak, kullanıcının
        // göremediği bir depodan çıkış yapmasına izin verirdi.
        var depo = await db.Warehouses
            .AsNoTracking()
            .ApplyScope(kapsam)
            .SingleOrDefaultAsync(
                x => x.Id == istek.WarehouseId && x.CompanyId == istek.CompanyId,
                cancellationToken)
            ?? throw new InvalidOperationException("Depo bulunamadı veya erişim kapsamı dışında.");

        var kalem = await db.InventoryItems
            .AsNoTracking()
            .ApplyScope(kapsam)
            .Where(x => x.Id == istek.InventoryItemId && x.CompanyId == istek.CompanyId)
            .Select(x => new { x.Id, x.Code, x.Name, x.Type })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Stok kartı bulunamadı veya erişim kapsamı dışında.");

        var personelVar = await db.Personnel
            .AsNoTracking()
            .ApplyScope(kapsam)
            .AnyAsync(
                x => x.Id == istek.PersonnelId && x.CompanyId == istek.CompanyId,
                cancellationToken);

        if (!personelVar)
            throw new InvalidOperationException("Personel bulunamadı veya erişim kapsamı dışında.");

        var tarih = istek.AssignmentDate == default
            ? DateTime.UtcNow
            : istek.AssignmentDate.ToUniversalTime();

        await using var islem = await db.Database.BeginTransactionAsync(cancellationToken);

        // SATIR KİLİDİ ARTIK BURADA DEĞİL — `StockSaleIssuer` alıyor.
        //
        // Bu akışa özel `FOR UPDATE` cümlesi vardı; kilidi bir taraf
        // alıp perakende satış ile stoklu satış faturası almadığı
        // sürece koruma yarım kalıyordu. Kilit stok değiştiren her
        // yolun ortak geçtiği yere (`IStokSatirKilidi`) taşındı;
        // burada tekrarlansaydı aynı kararın iki ayrı yerde
        // yaşayan iki kopyası olurdu (Kural 25).

        var satirlar = new List<StockSaleLine>
        {
            new(istek.InventoryItemId, miktar, $"{kalem.Code} zimmet", ZimmetBelgeNo(tarih))
        };

        var maliyetler = await stockIssuer.IssueAsync(
            istek.CompanyId, depo.Id, satirlar, tarih, currentUser.UserId, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        var cikisHareketi = await db.StockMovements
            .ApplyScope(kapsam)
            .Where(x => x.WarehouseId == depo.Id
                        && x.InventoryItemId == kalem.Id
                        && x.Type == StockMovementType.Issue)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstAsync(cancellationToken);

        // GİDER YAZILIR MI — karar tek yerde (ZimmetGiderKurali).
        var giderYazilir = ZimmetGiderKurali.GiderYazilir(kalem.Type);

        if (giderYazilir && maliyetler.Count > 0)
        {
            cikisHareketi.AccountingVoucherId = await consumptionPoster.PostIssueAsync(
                istek.CompanyId,
                maliyetler[0],
                istek.ProjectId,
                projectCode: null,
                reference: cikisHareketi.ReferenceNumber,
                movementDate: tarih,
                movementId: cikisHareketi.Id,
                cancellationToken);
        }

        var zimmet = new HrAssetAssignment
        {
            CompanyId = istek.CompanyId,
            PersonnelId = istek.PersonnelId,
            ProjectId = istek.ProjectId,
            InventoryItemId = kalem.Id,
            WarehouseId = depo.Id,
            IssueStockMovementId = cikisHareketi.Id,
            AssetType = kalem.Type.ToString(),
            AssetCode = kalem.Code,
            AssetName = kalem.Name,
            AssignmentDate = tarih,
            PlannedReturnDate = istek.PlannedReturnDate?.ToUniversalTime(),
            ConditionAtAssignment = istek.ConditionAtAssignment?.Trim(),
            SerialNumber = istek.SerialNumber?.Trim(),
            DocumentPath = istek.DocumentPath?.Trim(),
            Status = HrAssetAssignmentStatus.Assigned,
            Notes = istek.Notes?.Trim(),
            CreatedByUserId = currentUser.UserId
        };

        db.HrAssetAssignments.Add(zimmet);

        DenetimYaz("DepodanZimmetVerildi", zimmet.Id, new
        {
            zimmet.CompanyId,
            zimmet.PersonnelId,
            Depo = depo.Id,
            Kalem = kalem.Code,
            Miktar = miktar,
            GiderYazildi = giderYazilir,
            Gerekce = ZimmetGiderKurali.Gerekce(kalem.Type)
        });

        await db.SaveChangesAsync(cancellationToken);
        await islem.CommitAsync(cancellationToken);

        return new ZimmetSonucu(
            zimmet.Id, depo.Id, kalem.Id, cikisHareketi.Id,
            cikisHareketi.ReferenceNumber, "Zimmet verildi.");
    }

    public Task IadeAlAsync(
        Guid zimmetId, ZimmetIadeIstegi istek, CancellationToken cancellationToken) =>
        StokGeriAlAsync(
            zimmetId,
            istek.RowVersion,
            HrAssetAssignmentStatus.Returned,
            istek.ReturnDate,
            istek.ConditionAtReturn,
            istek.Notes,
            gerekce: null,
            cancellationToken);

    public Task IptalEtAsync(
        Guid zimmetId, ZimmetIptalIstegi istek, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(istek.Gerekce))
            throw new InvalidOperationException("İptal gerekçesi zorunludur.");

        return StokGeriAlAsync(
            zimmetId,
            istek.RowVersion,
            HrAssetAssignmentStatus.Cancelled,
            iadeTarihi: null,
            durum: null,
            not: null,
            gerekce: istek.Gerekce.Trim(),
            cancellationToken);
    }

    /// <summary>
    /// İADE VE İPTAL AYNI STOK İŞİNİ YAPIYOR — TEK YERDE.
    ///
    /// İkisi de malzemeyi depoya geri koyuyor ve çıkışta gider
    /// yazıldıysa ters kaydı atıyor. Ayrı ayrı yazılsalardı biri ters
    /// kaydı atarken diğeri unutabilirdi; fark stok-muhasebe
    /// mutabakatında çıkardı ve hangi akıştan geldiği belli olmazdı.
    ///
    /// Ayrıldıkları tek yer kaydın DURUMU ve denetim satırı.
    /// </summary>
    private async Task StokGeriAlAsync(
        Guid zimmetId,
        DateTime? rowVersion,
        HrAssetAssignmentStatus yeniDurum,
        DateTime? iadeTarihi,
        string? durum,
        string? not,
        string? gerekce,
        CancellationToken cancellationToken)
    {
        var kapsam = await KapsamAsync(cancellationToken);

        var zimmet = await db.HrAssetAssignments
            .ApplyScope(kapsam)
            .Where(x => x.Id == zimmetId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Zimmet kaydı bulunamadı veya erişim kapsamı dışında.");

        SurumuDogrula(zimmet, rowVersion);

        if (zimmet.Status != HrAssetAssignmentStatus.Assigned)
            throw new InvalidOperationException("Bu zimmet zaten kapatılmış.");

        if (zimmet.InventoryItemId is null || zimmet.WarehouseId is null ||
            zimmet.IssueStockMovementId is null)
        {
            throw new InvalidOperationException(
                "Bu kayıt depodan zimmet değil; stok iadesi yapılamaz.");
        }

        var cikis = await db.StockMovements
            .AsNoTracking()
            .ApplyScope(kapsam)
            .SingleAsync(x => x.Id == zimmet.IssueStockMovementId, cancellationToken);

        var tarih = (iadeTarihi ?? DateTime.UtcNow).ToUniversalTime();

        await using var islem = await db.Database.BeginTransactionAsync(cancellationToken);

        // MALİYET ÇIKIŞTAKİYLE AYNI DÖNÜYOR.
        //
        // Bugünün ortalama maliyetiyle geri almak, aradaki fiyat
        // değişimini zimmet iadesinin üzerine yıkardı: aynı malzeme
        // çıkıp geri geldiğinde stok değeri değişirdi.
        var birimMaliyet = cikis.UnitCost ?? 0m;

        var satirlar = new List<StockSaleLine>
        {
            new(zimmet.InventoryItemId.Value, cikis.Quantity,
                $"{zimmet.AssetCode} {(yeniDurum == HrAssetAssignmentStatus.Cancelled ? "iptal" : "iade")}",
                IadeBelgeNo(tarih))
        };

        await stockIssuer.ReturnAsync(
            zimmet.CompanyId,
            zimmet.WarehouseId.Value,
            satirlar,
            new Dictionary<Guid, decimal> { [zimmet.InventoryItemId.Value] = birimMaliyet },
            tarih,
            currentUser.UserId,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        var girisHareketi = await db.StockMovements
            .ApplyScope(kapsam)
            .Where(x => x.WarehouseId == zimmet.WarehouseId
                        && x.InventoryItemId == zimmet.InventoryItemId
                        && x.Type == StockMovementType.Receipt)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstAsync(cancellationToken);

        // ÇIKIŞTA GİDER YAZILDIYSA GERİ DÖNÜŞTE TERS KAYIT ŞART.
        //
        // Yazılmazsa malzeme hem gider yazılmış hem stokta durur
        // sayılır: stok-muhasebe mutabakatı fark verir.
        if (cikis.AccountingVoucherId is not null)
        {
            girisHareketi.AccountingVoucherId = await consumptionPoster.PostAdjustmentAsync(
                zimmet.CompanyId,
                new StockSaleCost(
                    zimmet.InventoryItemId.Value,
                    birimMaliyet,
                    decimal.Round(birimMaliyet * cikis.Quantity, 2)),
                surplus: true,
                zimmet.ProjectId,
                projectCode: null,
                reference: girisHareketi.ReferenceNumber,
                movementDate: tarih,
                movementId: girisHareketi.Id,
                cancellationToken);
        }

        zimmet.Status = yeniDurum;
        zimmet.ActualReturnDate = tarih;
        zimmet.ConditionAtReturn = durum?.Trim() ?? zimmet.ConditionAtReturn;
        zimmet.Notes = not?.Trim() ?? zimmet.Notes;
        zimmet.ReturnStockMovementId = girisHareketi.Id;
        zimmet.UpdatedAtUtc = DateTime.UtcNow;
        zimmet.UpdatedByUserId = currentUser.UserId;

        DenetimYaz(
            yeniDurum == HrAssetAssignmentStatus.Cancelled
                ? "DepodanZimmetIptalEdildi"
                : "DepodanZimmetIadeAlindi",
            zimmet.Id,
            new
            {
                zimmet.CompanyId,
                zimmet.PersonnelId,
                Depo = zimmet.WarehouseId,
                Kalem = zimmet.AssetCode,
                Miktar = cikis.Quantity,
                TersKayit = cikis.AccountingVoucherId is not null,
                Gerekce = gerekce
            });

        await db.SaveChangesAsync(cancellationToken);
        await islem.CommitAsync(cancellationToken);
    }

    private async Task<CurrentDataScopeSnapshot> KapsamAsync(CancellationToken cancellationToken) =>
        await dataScope.GetAsync(cancellationToken)
        ?? throw new UnauthorizedAccessException("Kullanıcı veri kapsamı bulunamadı.");

    /// <summary>
    /// Milisaniye hassasiyetinde karşılaştırılıyor: PostgreSQL
    /// mikrosaniye tutuyor, JSON'a giden değer milisaniyede kesiliyor.
    /// Tam eşitlik aranırsa her istek çakışma verirdi.
    /// </summary>
    private static void SurumuDogrula(HrAssetAssignment zimmet, DateTime? surum)
    {
        if (surum is null)
            throw new InvalidOperationException("Kayıt sürümü (RowVersion) zorunludur.");

        var guncel = zimmet.UpdatedAtUtc ?? zimmet.CreatedAtUtc;

        var a = new DateTime(guncel.Ticks - (guncel.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
        var b = surum.Value.ToUniversalTime();
        b = new DateTime(b.Ticks - (b.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);

        if (a != b)
        {
            throw new DbUpdateConcurrencyException(
                "Kayıt başka bir kullanıcı tarafından değiştirilmiş. Sayfayı yenileyip tekrar deneyin.");
        }
    }

    private void DenetimYaz(string eylem, Guid zimmetId, object ayrinti) =>
        db.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            ActorUserId = currentUser.UserId,
            ActorUsername = currentUser.Username,
            Action = eylem,
            EntityType = nameof(HrAssetAssignment),
            EntityId = zimmetId,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(ayrinti)
        });

    private static string ZimmetBelgeNo(DateTime tarih) =>
        $"ZIM-{tarih:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    private static string IadeBelgeNo(DateTime tarih) =>
        $"ZIA-{tarih:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
}
