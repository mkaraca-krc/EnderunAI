namespace EnderunAI.Api.Services.Accounting;

using ClosedXML.Excel;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Search;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

public sealed record HesapPlaniAktarimHatasi(
    int RowNumber, string? AccountCode, string Message);

/// <summary>
/// Ön yüzdeki `ImportResult` ile birebir. `UpdatedCount` HER ZAMAN
/// sıfır: aktarım mevcut hesabı güncellemiyor. Alan yine de dönüyor,
/// çünkü ekran onu okuyor — kaldırmak ekranı kırardı.
/// </summary>
public sealed record HesapPlaniAktarimSonucu(
    bool Preview,
    int TotalRowCount,
    int ValidRowCount,
    int CreatedCount,
    int UpdatedCount,
    int UnchangedCount,
    int SkippedCount,
    int ErrorCount,
    IReadOnlyList<HesapPlaniAktarimHatasi> Errors,
    string Message);

/// <summary>
/// HESAP KODU HİYERARŞİSİ — SAF KARAR.
///
/// Tek düzen hesap planında hiyerarşi kodun KENDİSİNDE: "150.01.02"
/// hesabının üstü "150.01". Ayrı bir üst-hesap sütunu istemek, kodla
/// çelişebilecek ikinci bir doğruluk kaynağı açardı.
/// </summary>
public static class HesapKoduHiyerarsisi
{
    public static string? UstKod(string? kod)
    {
        if (string.IsNullOrWhiteSpace(kod))
            return null;

        var son = kod.LastIndexOf('.');
        return son <= 0 ? null : kod[..son];
    }

    public static int Seviye(string? kod) =>
        string.IsNullOrWhiteSpace(kod) ? 0 : kod.Count(c => c == '.') + 1;
}

public interface IHesapPlaniAktarimService
{
    Task<HesapPlaniAktarimSonucu> AktarAsync(
        Guid companyId, Stream excel, bool preview, CancellationToken cancellationToken);
}

/// <summary>
/// HESAP PLANI AKTARIMI — EKLER YA DA ATLAR, ASLA DEĞİŞTİRMEZ.
///
/// 1. MEVCUT HESAP KODU GELİRSE GÜNCELLENMEZ. Satır atlanır ve
///    "zaten var" sayılır. Muhasebe hesabını aktarımla değiştirmek
///    elle yapılacak bir iştir: bir dosyada yanlış yazılmış tek bir
///    ad, hesabın anlamını sessizce değiştirir ve fark edilmez.
///
/// 2. ÜST HESAP YOKSA OLUŞTURULMAZ. Hata verilir, satır atlanır.
///    Sessizce ara hesap üretmek hesap planını bozar — üretilen
///    hesabın borç/alacak karakteri TAHMİN edilmiş olur ve mali
///    tabloda yanlış yerde toplanır.
///
/// İkisi de veri DEĞİŞTİRMİYOR: aktarım ya ekler ya atlar. Bu yüzden
/// geri alması kolay — yanlış giden satır hiç eklenmemiştir.
/// </summary>
public sealed class HesapPlaniAktarimService(
    AppDbContext db,
    ICurrentDataScopeService dataScope) : IHesapPlaniAktarimService
{
    public async Task<HesapPlaniAktarimSonucu> AktarAsync(
        Guid companyId, Stream excel, bool preview, CancellationToken cancellationToken)
    {
        // KAPSAM KAPISI.
        //
        // Şirket kimliği İSTEKTEN geliyor. Doğrulanmasaydı, A
        // şirketinin muhasebecisi B şirketinin hesap planına toplu
        // hesap yazabilirdi — üstelik tek bir dosyayla.
        var kapsam = await dataScope.GetAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("Kullanıcı veri kapsamı bulunamadı.");

        if (!kapsam.HasGlobalAccess && !kapsam.CompanyIds.Contains(companyId))
            throw new UnauthorizedAccessException("Şirket erişim kapsamı dışında.");

        var satirlar = SatirlariOku(excel, out var okumaHatasi);

        if (okumaHatasi is not null)
        {
            return new HesapPlaniAktarimSonucu(
                preview, 0, 0, 0, 0, 0, 0, 1,
                [new HesapPlaniAktarimHatasi(0, null, okumaHatasi)],
                okumaHatasi);
        }

        var mevcut = await db.AccountingAccounts
            .AsNoTracking()
            .ApplyScope(kapsam)
            .Where(x => x.CompanyId == companyId)
            .Select(x => new { x.Id, x.Code })
            .ToListAsync(cancellationToken);

        var kodToId = mevcut.ToDictionary(x => x.Code, x => x.Id, StringComparer.OrdinalIgnoreCase);
        var bilinen = new HashSet<string>(kodToId.Keys, StringComparer.OrdinalIgnoreCase);

        var hatalar = new List<HesapPlaniAktarimHatasi>();
        var eklenecek = new List<AccountingAccount>();
        var zatenVar = 0;

        // KODA GÖRE SIRALANIYOR: üst hesap alt hesaptan ÖNCE gelsin.
        //
        // Aynı dosyada hem "150" hem "150.01" varsa, dosya sırasıyla
        // işlense "150.01" önce gelip "üst hesap yok" hatası alırdı —
        // oysa üstü aynı dosyada, bir alt satırda duruyor.
        foreach (var satir in satirlar.OrderBy(x => x.Kod, StringComparer.Ordinal))
        {
            if (bilinen.Contains(satir.Kod))
            {
                zatenVar++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(satir.Ad))
            {
                hatalar.Add(new(satir.SatirNo, satir.Kod, "Hesap adı boş — satır atlandı."));
                continue;
            }

            var ustKod = HesapKoduHiyerarsisi.UstKod(satir.Kod);

            if (ustKod is not null && !bilinen.Contains(ustKod))
            {
                hatalar.Add(new(satir.SatirNo, satir.Kod,
                    $"Üst hesap ({ustKod}) bulunamadı — satır atlandı, üst hesap oluşturulmadı."));
                continue;
            }

            var hesap = new AccountingAccount
            {
                CompanyId = companyId,
                Code = satir.Kod,
                Name = satir.Ad.Trim(),
                Level = HesapKoduHiyerarsisi.Seviye(satir.Kod),
                ParentAccountId = ustKod is not null && kodToId.TryGetValue(ustKod, out var ustId)
                    ? ustId
                    : null
            };

            eklenecek.Add(hesap);
            bilinen.Add(satir.Kod);
            kodToId[satir.Kod] = hesap.Id;
        }

        // ÖN İZLEME HİÇBİR ŞEY YAZMAZ.
        if (!preview && eklenecek.Count > 0)
        {
            db.AccountingAccounts.AddRange(eklenecek);
            await db.SaveChangesAsync(cancellationToken);
        }

        var mesaj = preview
            ? $"Ön izleme: {eklenecek.Count} hesap eklenecek, {zatenVar} kod zaten var, {hatalar.Count} satır hatalı. Veritabanında değişiklik yapılmadı."
            : $"{eklenecek.Count} yeni hesap oluşturuldu. {zatenVar} kod zaten vardı ve GÜNCELLENMEDİ, {hatalar.Count} satır atlandı.";

        return new HesapPlaniAktarimSonucu(
            Preview: preview,
            TotalRowCount: satirlar.Count,
            ValidRowCount: satirlar.Count - hatalar.Count,
            CreatedCount: eklenecek.Count,

            // HER ZAMAN SIFIR — aktarım güncelleme yapmıyor.
            UpdatedCount: 0,

            UnchangedCount: zatenVar,
            SkippedCount: zatenVar + hatalar.Count,
            ErrorCount: hatalar.Count,
            Errors: hatalar,
            Message: mesaj);
    }

    private sealed record OkunanSatir(int SatirNo, string Kod, string Ad);

    /// <summary>
    /// BAŞLIK SATIRI ARANIR, VARSAYILMAZ.
    ///
    /// Sabit "A sütunu kod, B sütunu ad" kabulü, sütunları farklı
    /// sıralanmış bir dosyada SESSİZCE yanlış hesap üretirdi: ad
    /// alanına kod yazılır ve kimse fark etmez. Başlık bulunamazsa
    /// aktarım hiç başlamıyor.
    /// </summary>
    private static List<OkunanSatir> SatirlariOku(Stream excel, out string? hata)
    {
        hata = null;
        var sonuc = new List<OkunanSatir>();

        using var kitap = new XLWorkbook(excel);
        var sayfa = kitap.Worksheets.FirstOrDefault();

        if (sayfa is null)
        {
            hata = "Dosyada sayfa bulunamadı.";
            return sonuc;
        }

        int? kodSutun = null, adSutun = null, baslikSatir = null;

        foreach (var satir in sayfa.RowsUsed().Take(20))
        {
            foreach (var hucre in satir.CellsUsed())
            {
                var metin = TurkishSearch.Fold(hucre.GetString());

                if (kodSutun is null && metin.Contains("kod"))
                    kodSutun = hucre.Address.ColumnNumber;
                else if (adSutun is null && (metin.Contains("ad") || metin.Contains("unvan")))
                    adSutun = hucre.Address.ColumnNumber;
            }

            if (kodSutun is not null && adSutun is not null)
            {
                baslikSatir = satir.RowNumber();
                break;
            }

            kodSutun = null;
            adSutun = null;
        }

        if (baslikSatir is null)
        {
            hata = "Başlık satırı bulunamadı: 'Kod' ve 'Ad' sütunları olan bir satır gerekiyor.";
            return sonuc;
        }

        foreach (var satir in sayfa.RowsUsed().Where(x => x.RowNumber() > baslikSatir))
        {
            var kod = satir.Cell(kodSutun!.Value).GetString().Trim();
            var ad = satir.Cell(adSutun!.Value).GetString().Trim();

            if (string.IsNullOrWhiteSpace(kod) && string.IsNullOrWhiteSpace(ad))
                continue;

            sonuc.Add(new OkunanSatir(satir.RowNumber(), kod, ad));
        }

        return sonuc;
    }
}
