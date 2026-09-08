using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Upload;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Messaging;

public sealed record MesajEkiOzeti(
    Guid Id,
    Guid MesajId,
    string Ad,
    string ContentType,
    long BoyutBayt);

public sealed record EkIndirmeSonucu(
    string TamYol,
    string ContentType,
    string Ad);

public interface IMesajEkiServisi
{
    Task<IReadOnlyList<MesajEkiOzeti>> EkleAsync(
        Guid mesajId, IReadOnlyList<IFormFile> dosyalar, CancellationToken ct);

    Task<IReadOnlyList<MesajEkiOzeti>> ListeleAsync(
        IReadOnlyList<Guid> mesajIdleri, CancellationToken ct);

    Task<EkIndirmeSonucu?> IndirAsync(Guid ekId, CancellationToken ct);
}

/// <summary>
/// MESAJ EKLERİ — YÜKLEME VE YETKİLİ İNDİRME (MESAJ/3 Parça 3).
///
/// ═══ NEDEN YENİ TABLO YOK ═══
///
/// Depoda zaten genel bir `Attachment` varlığı var
/// (`EntityType` + `EntityId`) ve Sekretarya ile İşbirliği onu
/// kullanıyor. Mesajlar için ikinci bir tablo açmak, aynı kuralın
/// (yol üretme, yumuşak silme, kapsam) ikinci kez yazılması demekti
/// (Kural 79). `EntityType = "Message"`, `EntityId = mesajId`.
///
/// ═══ İKİ AYRI KAPI, İKİSİ DE ŞART ═══
///
/// 1. ÜYELİK — çağıran BU konuşmanın tarafı mı (`ApplyMembership`)
/// 2. İZİN   — çağıranda `mesajlar.view` var mı, KANONİK çözücüden
///
/// İkincisi ayrı duruyor çünkü M3/2c-2'de ölçüldü: kendi EF
/// sorgumu yazsaydım `user_permission_overrides` üzerinden verilen
/// Deny kaydını HİÇ görmezdim — rolünde izin duran ama kişisel
/// olarak yasaklanmış kullanıcı eki indirmeye devam ederdi.
///
/// ═══ DOSYA ADI ═══
///
/// Diskteki ad `IUploadService`in ürettiği `zamandamgası_GUID.uzantı`.
/// Kullanıcının verdiği ad YALNIZCA gösterim alanında saklanıyor ve
/// yol oluşturmada ASLA kullanılmıyor.
/// </summary>
public sealed class MesajEkiServisi(
    AppDbContext db,
    IUploadService uploads,
    ICurrentUserService currentUser,
    IUserAuthorizationService yetkiCozucu) : IMesajEkiServisi
{
    public async Task<IReadOnlyList<MesajEkiOzeti>> EkleAsync(
        Guid mesajId, IReadOnlyList<IFormFile> dosyalar, CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId)
            throw new UnauthorizedAccessException("Oturum bulunamadı.");

        if (dosyalar.Count == 0)
            throw new InvalidOperationException("Dosya seçilmedi.");

        // C1: MESAJ BAŞINA SINIR — SUNUCUDA. İstemcideki sınır
        // kullanıcıya yardım eder; koruma buradaki sınırdır.
        if (dosyalar.Count > MesajEkiKurallari.MesajBasinaEnFazlaDosya)
        {
            throw new InvalidOperationException(
                $"Bir mesaja en fazla {MesajEkiKurallari.MesajBasinaEnFazlaDosya} dosya eklenebilir.");
        }

        // ÜYELİK: mesaj çağıranın üyesi olduğu bir konuşmaya mı ait.
        var mesaj = await db.Messages
            .AsNoTracking()
            .ApplyMembership(userId)
            .Where(x => x.Id == mesajId)
            .Select(x => new { x.Id, x.CompanyId })
            .SingleOrDefaultAsync(ct);

        if (mesaj is null)
            throw new UnauthorizedAccessException("Mesaj bulunamadı.");

        var sonuc = new List<MesajEkiOzeti>(dosyalar.Count);

        foreach (var dosya in dosyalar)
        {
            // KAPI 1: ad ve boyut.
            var karar = MesajEkiKurallari.Denetle(dosya.FileName, dosya.Length);
            if (karar != MesajEkiKurallari.Karar.Kabul)
                throw new InvalidOperationException(MesajEkiKurallari.Mesaj(karar));

            // KAPI 2: İÇERİK. İstemcinin bildirdiği MIME'a ve uzantıya
            // GÜVENİLMİYOR; dosyanın ilk baytları okunuyor.
            var uzanti = Path.GetExtension(dosya.FileName);
            var bas = new byte[DosyaIcerikDenetimi.OkunacakBayt];
            int okunan;

            await using (var akis = dosya.OpenReadStream())
            {
                okunan = await akis.ReadAsync(bas, ct);
            }

            var icerik = DosyaIcerikDenetimi.Denetle(uzanti, bas.AsSpan(0, okunan));

            if (icerik != DosyaIcerikDenetimi.Sonuc.Uyuyor)
            {
                throw new InvalidOperationException(
                    "Dosyanın içeriği uzantısıyla uyuşmuyor; güvenlik nedeniyle kabul edilmedi.");
            }

            var kayit = await uploads.SaveAsync(dosya, MesajEkiKurallari.Kategori, ct);

            var ek = new Attachment
            {
                CompanyId = mesaj.CompanyId,
                EntityType = MesajEkiKurallari.VarlikTuru,
                EntityId = mesaj.Id,
                Category = MesajEkiKurallari.Kategori,
                StoredName = kayit.StoredName,
                OriginalName = kayit.OriginalName,
                ContentType = kayit.ContentType,
                SizeBytes = kayit.Size,
                UploadedByUserId = userId
            };

            db.Attachments.Add(ek);
            sonuc.Add(new MesajEkiOzeti(
                ek.Id, mesaj.Id, ek.OriginalName, ek.ContentType, ek.SizeBytes));
        }

        await db.SaveChangesAsync(ct);
        return sonuc;
    }

    /// <summary>
    /// ÇAĞIRANIN MESAJLARI GÖRME İZNİ VAR MI — KANONİK ÇÖZÜCÜDEN.
    ///
    /// TEK YERDE, çünkü iki çağıranı var (`ListeleAsync`,
    /// `IndirAsync`) ve ikisinde ayrı yazılsaydı biri unutulurdu
    /// (Kural 79). Nitekim ilk yazımda `ListeleAsync`te HİÇ YOKTU
    /// ve bunu C7 sondası yakaladı: izin kapısı kaldırıldığında
    /// Kol B kırmızı vermedi, çünkü o kol izne hiç bakmayan bir
    /// yoldan ölçüyordu.
    ///
    /// Kontrol denetleyicideki `[RequirePermission]` ile ÇİFTLENİYOR
    /// ve bu bilerek: servis başka bir yerden (SignalR, bir başka
    /// servis) çağrılırsa öznitelik koşmaz.
    /// </summary>
    private async Task<bool> MesajlariGorebilirMi(Guid userId, CancellationToken ct)
    {
        var yetki = await yetkiCozucu.GetAsync(userId, ct);

        return yetki is not null
               && yetki.IsActive
               && yetki.Permissions.Contains(PermissionCatalog.Keys.MesajlarView);
    }

    public async Task<IReadOnlyList<MesajEkiOzeti>> ListeleAsync(
        IReadOnlyList<Guid> mesajIdleri, CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId || mesajIdleri.Count == 0)
            return [];

        if (!await MesajlariGorebilirMi(userId, ct)) return [];

        /*
         * KAPI, OKUMANIN KENDİSİNDE.
         *
         * Önceki biçim iki adımlıydı: önce üyelik süzgeciyle görünür
         * mesaj kimlikleri toplanıyor, sonra AYRI bir sorgu ekleri o
         * listeye göre süzüyordu. Davranış doğruydu ama yapı yanlıştı:
         * ekleri okuyan sorgunun kendi kapısı yoktu, önceki sorgunun
         * çıktısına güveniyordu.
         *
         * Kapsam çırcırı (CoverageBaselineTests) bunu KAPISIZ okuma
         * sayıp düştü ve HAKLIYDI — bekçi okumanın kendisine bakıyor,
         * ondan önce ne olduğuna değil. Bekçiyi susturmak için
         * sorguyu kaydırmadım, istisna listesine de eklemedim:
         * okuma artık üyelik süzgecinin UÇUNDAN başlıyor, ekler
         * oradan geziliyor. Kapı, sorgudan sökülemez hâlde.
         *
         * ApplyScope BİLEREK KULLANILMADI: küresel kapsamlı kullanıcı
         * (Admin, Genel Müdür) için sorguyu olduğu gibi geçirir ve
         * herkesin özel konuşmasının eklerini açardı. ApplyMembership
         * daha dar — M3/1'de verilen karar bu.
         */
        return await db.Messages
            .AsNoTracking()
            .ApplyMembership(userId)
            .Where(x => mesajIdleri.Contains(x.Id))
            .SelectMany(x => db.Attachments
                .Where(a => a.EntityType == MesajEkiKurallari.VarlikTuru
                            && a.EntityId == x.Id))
            .Select(a => new MesajEkiOzeti(
                a.Id, a.EntityId, a.OriginalName, a.ContentType, a.SizeBytes))
            .ToListAsync(ct);
    }

    /// <summary>
    /// EKİ İNDİR — C5'İN KALBİ.
    ///
    /// `null` dönerse çağıran 404 veriyor. "Yetkin yok" ile "yok"
    /// AYNI cevabı alıyor: hangi eklerin VAR olduğunu sızdırmamak
    /// için. Bir saldırgan 403 ile 404 farkından ek kimliklerini
    /// sayabilirdi.
    /// </summary>
    public async Task<EkIndirmeSonucu?> IndirAsync(Guid ekId, CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId) return null;

        /*
         * KAPI 2 — İZİN, KANONİK ÇÖZÜCÜDEN.
         *
         * Üyelikten ÖNCE bakılıyor: izni olmayan biri için konuşma
         * sorgusu hiç koşmasın. Sıra bir başarım tercihi değil —
         * iki kapının da geçilmesi gerektiği için sonuç aynı.
         */
        if (!await MesajlariGorebilirMi(userId, ct)) return null;

        // KAPI 1 — ÜYELİK. Ek, çağıranın üyesi olduğu bir konuşmanın
        // mesajına mı ait.
        var ek = await db.Attachments
            .AsNoTracking()
            .Where(x => x.Id == ekId && x.EntityType == MesajEkiKurallari.VarlikTuru)
            .SingleOrDefaultAsync(ct);

        if (ek is null) return null;

        var uyeMi = await db.Messages
            .AsNoTracking()
            .ApplyMembership(userId)
            .AnyAsync(x => x.Id == ek.EntityId, ct);

        if (!uyeMi) return null;

        var dosya = uploads.GetFile(ek.Category, ek.StoredName);
        if (dosya is null) return null;

        return new EkIndirmeSonucu(dosya.FullPath, ek.ContentType, ek.OriginalName);
    }
}
