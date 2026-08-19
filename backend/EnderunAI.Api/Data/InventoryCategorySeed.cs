using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

/// <summary>
/// STOK KATEGORİLERİ VE ÖZELLİK ŞABLONLARI (S1).
///
/// SİSTEM GENELİ: kategori şirkete bağlı değil. "Kablo tavası" her
/// şirkette aynı şeydir; iki ayrı sette tutmak mükerrer bakım ve
/// zamanla ayrışan özellik listeleri doğururdu.
///
/// TOHUM SADECE EKLER, GÜNCELLEMEZ: koddaki liste kullanıcının
/// ekrandan yaptığı değişikliği ezmemeli. Var olan kategori/özellik/
/// değer atlanır; yalnız eksik olanlar eklenir. Kullanıcı yeni
/// kategori ve değer ekleyebilir — bu liste başlangıç, sınır değil.
/// </summary>
public static class InventoryCategorySeed
{
    private sealed record Ozellik(string Code, string Name, string[] Options);

    private sealed record Kategori(
        string Code,
        string Name,
        string[] Units,
        InventoryCategoryKind Kind,
        Ozellik[] Attributes);

    /// <summary>Ölçü ve kaplama listeleri tava ile merdivende ORTAK.</summary>
    private static readonly string[] TavaOlculeri =
        ["50", "100", "200", "300", "400", "500"];

    private static readonly string[] Kaplamalar =
        ["Sıcak Daldırma Galvaniz", "Pregalvaniz", "Paslanmaz", "Boyalı"];

    private static readonly Kategori[] Kategoriler =
    [
        new("KABLO_TAVASI", "Kablo Tavası", ["metre"], InventoryCategoryKind.Standard,
        [
            new("OLCU", "Ölçü", TavaOlculeri),
            new("KALINLIK", "Kalınlık", ["0.8", "1.0", "1.2", "1.5", "2.0"]),
            new("CINS", "Cins", ["Perfore", "Kapalı", "Delikli"]),
            new("KAPLAMA", "Kaplama", Kaplamalar)
        ]),

        // Merdivende ölçü ve kaplama listeleri tavayla AYNI (karar).
        new("KABLO_MERDIVENI", "Kablo Merdiveni", ["metre"], InventoryCategoryKind.Standard,
        [
            new("OLCU", "Ölçü", TavaOlculeri),
            new("KAPLAMA", "Kaplama", Kaplamalar)
        ]),

        new("KABLO", "Kablo", ["metre"], InventoryCategoryKind.Standard,
        [
            new("TIP", "Tip", ["NYY", "NYM", "N2XH", "NHXMH", "N2XSY"]),
            new("KESIT", "Kesit",
                ["3x2.5", "3x4", "3x6", "4x6", "4x10", "4x16", "3x25", "3x35", "3x50"]),
            new("ILETKEN", "İletken", ["Bakır", "Alüminyum"])
        ]),

        new("OTOMAT_SALTER", "Otomat Şalter", ["adet"], InventoryCategoryKind.Standard,
        [
            new("AMPER", "Amper", ["6", "10", "16", "20", "25", "32", "40", "50", "63"]),
            new("KUTUP", "Kutup", ["1P", "3P", "1P+N"]),
            new("EGRI", "Eğri", ["B", "C", "D"])
        ]),

        // KUTUP BURADA 2P/4P — otomatın 1P/3P'si DEĞİL (karar).
        // Kaçak akım rölesi monofazede 2P, trifazede 4P olur.
        new("KACAK_AKIM_ROLESI", "Kaçak Akım Rölesi", ["adet"], InventoryCategoryKind.Standard,
        [
            new("AMPER", "Amper", ["25", "40", "63"]),
            new("HASSASIYET", "Hassasiyet", ["30mA", "300mA"]),
            new("KUTUP", "Kutup", ["2P", "4P"])
        ]),

        new("PRIZ_ANAHTAR", "Priz-Anahtar", ["adet"], InventoryCategoryKind.Standard,
        [
            new("TIP", "Tip",
                ["Topraklı Priz", "UPS Priz", "Anahtar", "Komütatör", "Vaviyen"])
        ]),

        new("ARMATUR_STANDART", "Armatür - Standart", ["adet"], InventoryCategoryKind.Standard,
        [
            new("GUC", "Güç", ["9W", "18W", "24W", "36W", "48W"]),
            new("TIP", "Tip", ["LED", "Floresan", "Downlight", "Projektör"]),
            new("IP", "IP", ["20", "44", "65"])
        ]),

        new("BORU_KANAL", "Boru/Kanal", ["metre"], InventoryCategoryKind.Standard,
        [
            new("CAP", "Çap", ["16", "20", "25", "32", "40", "50"]),
            new("TIP", "Tip", ["PVC Spiral", "Metal", "Kablo Kanalı"])
        ]),

        new("PANO", "Pano", ["adet"], InventoryCategoryKind.Standard,
        [
            new("TIP", "Tip", ["Sıva Altı", "Sıva Üstü", "Dağıtım"]),
            new("SIRA", "Sıra", ["12", "24", "36", "54"])
        ]),

        new("BUSBAR", "Busbar", ["metre"], InventoryCategoryKind.Standard,
        [
            new("AKIM", "Akım", ["63A", "100A", "160A", "250A"])
        ]),

        // İKİ BİRİM: bakır şerit metre, toprak çubuğu adet.
        new("TOPRAKLAMA", "Topraklama", ["adet", "metre"], InventoryCategoryKind.Standard,
        [
            new("TIP", "Tip", ["Bakır Şerit", "Toprak Çubuğu", "Klemens", "Baret"])
        ]),

        // ÜÇ BİRİM: vida adet, kablo bağı paket, bazı sarflar kg.
        new("SARF", "Sarf", ["adet", "paket", "kg"], InventoryCategoryKind.Standard,
        [
            new("TIP", "Tip",
                ["Vida", "Dübel", "Kelepçe", "Klemens", "Kablo Bağı", "İzole Bant"]),
            new("OLCU", "Ölçü", ["Küçük", "Orta", "Büyük"])
        ]),

        // SERBEST TİPLER: ad elle yazılır, mükerrer engeli uygulanmaz.
        new("DEKORATIF_AYDINLATMA", "Dekoratif Aydınlatma", ["adet"],
            InventoryCategoryKind.Free, []),

        new("OZEL_IMALAT", "Özel İmalat", ["adet", "metre", "kg"],
            InventoryCategoryKind.Free, [])
    ];

    public static async Task SeedAsync(AppDbContext db)
    {
        var mevcutKodlar = await db.InventoryCategories
            .Select(x => x.Code)
            .ToListAsync();

        var mevcut = mevcutKodlar.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var eklenen = 0;

        for (var i = 0; i < Kategoriler.Length; i++)
        {
            var tanim = Kategoriler[i];

            if (mevcut.Contains(tanim.Code)) continue;

            var kategori = new InventoryCategory
            {
                Code = tanim.Code,
                Name = tanim.Name,
                Kind = tanim.Kind,
                SortOrder = (i + 1) * 10
            };

            for (var u = 0; u < tanim.Units.Length; u++)
            {
                kategori.AllowedUnits.Add(new InventoryCategoryUnit
                {
                    Unit = tanim.Units[u],
                    SortOrder = (u + 1) * 10
                });
            }

            for (var a = 0; a < tanim.Attributes.Length; a++)
            {
                var ozellik = tanim.Attributes[a];

                var attribute = new InventoryAttribute
                {
                    Code = ozellik.Code,
                    Name = ozellik.Name,
                    SortOrder = (a + 1) * 10,
                    IsRequired = true
                };

                for (var o = 0; o < ozellik.Options.Length; o++)
                {
                    attribute.Options.Add(new InventoryAttributeOption
                    {
                        Value = ozellik.Options[o],
                        SortOrder = (o + 1) * 10
                    });
                }

                kategori.Attributes.Add(attribute);
            }

            db.InventoryCategories.Add(kategori);
            eklenen++;
        }

        if (eklenen > 0) await db.SaveChangesAsync();
    }
}
