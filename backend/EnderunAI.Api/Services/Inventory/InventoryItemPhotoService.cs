using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Upload;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Inventory;

public sealed record InventoryItemPhotoRow(
    Guid Id,
    string OriginalName,
    string ContentType,
    long Size,
    bool IsCover,
    string? Caption,
    DateTime UploadedAtUtc);

/// <summary>
/// STOK KARTI GÖRSEL GALERİSİ.
///
/// KAPAK GÜVENCESİ TEK YERDE: galeri boş değilse mutlaka bir kapak
/// vardır ve en fazla bir tanedir. Bu kural üç ayrı yerde (yükleme,
/// silme, kapak seçme) tekrarlanmak yerine burada toplandı; dağıtılsaydı
/// bir yol atlanır ve liste ekranı kapaksız — yani görselsiz — kalırdı.
/// </summary>
public sealed class InventoryItemPhotoService(AppDbContext db, IUploadService uploads)
{
    private const string UploadCategory = "stok-kartlari";

    /// <summary>
    /// GALERİ YALNIZ GÖRSEL ALIR. Paylaşılan `IUploadService` PDF ve
    /// Excel'e de izin veriyor (belge modülleri onu kullanıyor);
    /// daraltmak onları kırardı, bu yüzden şart BURADA.
    /// </summary>
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".heic"
        };

    public async Task<IReadOnlyList<InventoryItemPhotoRow>> ListAsync(
        Guid inventoryItemId, CancellationToken cancellationToken) =>
        await db.InventoryItemPhotos
            .AsNoTracking()
            .Where(x => x.InventoryItemId == inventoryItemId)
            // Kapak önce: ekran ilk sırayı kapak sanabilsin.
            .OrderByDescending(x => x.IsCover)
            .ThenBy(x => x.CreatedAtUtc)
            .Select(x => new InventoryItemPhotoRow(
                x.Id, x.OriginalName, x.ContentType, x.Size,
                x.IsCover, x.Caption, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<InventoryItemPhotoRow> AddAsync(
        Guid inventoryItemId,
        Microsoft.AspNetCore.Http.IFormFile file,
        string? caption,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var itemExists = await db.InventoryItems
            .AnyAsync(x => x.Id == inventoryItemId, cancellationToken);

        if (!itemExists)
            throw new KeyNotFoundException("Malzeme kartı bulunamadı.");

        var extension = Path.GetExtension(file?.FileName ?? string.Empty);

        if (!ImageExtensions.Contains(extension))
        {
            throw new InvalidOperationException(
                "Yalnız görsel yüklenebilir (jpg, jpeg, png, webp, heic).");
        }

        var saved = await uploads.SaveAsync(file!, UploadCategory, cancellationToken);

        // İLK GÖRSEL KENDİLİĞİNDEN KAPAK olur: kullanıcıyı tek görselli
        // kartta ayrıca "kapak yap" demeye zorlamak, listeyi görselsiz
        // bırakan en sık hataydı.
        var hasCover = await db.InventoryItemPhotos
            .AnyAsync(x => x.InventoryItemId == inventoryItemId && x.IsCover, cancellationToken);

        var photo = new InventoryItemPhoto
        {
            InventoryItemId = inventoryItemId,
            StoredName = saved.StoredName,
            OriginalName = saved.OriginalName,
            ContentType = saved.ContentType,
            Size = saved.Size,
            IsCover = !hasCover,
            Caption = caption?.Trim(),
            CreatedByUserId = userId
        };

        db.InventoryItemPhotos.Add(photo);
        await db.SaveChangesAsync(cancellationToken);

        return new InventoryItemPhotoRow(
            photo.Id, photo.OriginalName, photo.ContentType, photo.Size,
            photo.IsCover, photo.Caption, photo.CreatedAtUtc);
    }

    public async Task SetCoverAsync(Guid photoId, Guid? userId, CancellationToken cancellationToken)
    {
        var photo = await db.InventoryItemPhotos
            .SingleOrDefaultAsync(x => x.Id == photoId, cancellationToken)
            ?? throw new KeyNotFoundException("Görsel bulunamadı.");

        var siblings = await db.InventoryItemPhotos
            .Where(x => x.InventoryItemId == photo.InventoryItemId)
            .ToListAsync(cancellationToken);

        // Tek kapak: hepsini indirip yalnız seçileni kaldırıyoruz.
        foreach (var sibling in siblings)
        {
            var shouldBeCover = sibling.Id == photoId;
            if (sibling.IsCover == shouldBeCover) continue;

            sibling.IsCover = shouldBeCover;
            sibling.UpdatedAtUtc = DateTime.UtcNow;
            sibling.UpdatedByUserId = userId;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid photoId, Guid? userId, CancellationToken cancellationToken)
    {
        var photo = await db.InventoryItemPhotos
            .SingleOrDefaultAsync(x => x.Id == photoId, cancellationToken)
            ?? throw new KeyNotFoundException("Görsel bulunamadı.");

        photo.IsDeleted = true;
        photo.DeletedAtUtc = DateTime.UtcNow;
        photo.DeletedByUserId = userId;

        // KAPAK SİLİNİRSE SIRADAKİ DEVRALIR. Devralmasaydı galeri dolu
        // ama kapaksız kalır, liste ekranı da görselsiz görünürdü —
        // kullanıcı görselin silindiğini değil kaybolduğunu sanardı.
        if (photo.IsCover)
        {
            var next = await db.InventoryItemPhotos
                .Where(x => x.InventoryItemId == photo.InventoryItemId && x.Id != photoId)
                .OrderBy(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (next is not null)
            {
                next.IsCover = true;
                next.UpdatedAtUtc = DateTime.UtcNow;
                next.UpdatedByUserId = userId;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // Diskteki dosya kaydın ardından siliniyor: önce silinip kayıt
        // yazılamasaydı galeri var olmayan bir dosyayı gösterirdi.
        uploads.DeleteFile(UploadCategory, photo.StoredName);
    }

    public async Task<FileDownloadResult> GetFileAsync(
        Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await db.InventoryItemPhotos
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == photoId, cancellationToken)
            ?? throw new KeyNotFoundException("Görsel bulunamadı.");

        return uploads.GetFile(UploadCategory, photo.StoredName)
            ?? throw new KeyNotFoundException("Görsel dosyası diskte bulunamadı.");
    }
}
