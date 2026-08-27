using EnderunAI.Api.Data;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// KOD BENZERSİZLİĞİ SÜZGECİ — DOKUZ TABLONUN HEPSİ (Kural 49).
///
/// PARAMETRELİ ve doğrudan SQL ile çalışıyor. Gerekçe: dokuz tablo
/// dokuz farklı varlık, dokuz farklı zorunlu alan demek; her biri
/// için elle nesne kurmak testi kırılgan ve uzun yapardı. Sorulan
/// şey varlıkların davranışı değil, İNDEKSİN ŞEKLİ.
///
/// İKİ YÖNÜ DE ÖLÇÜYOR:
///   - silinmiş kaydın kodu yeniden kullanılabilir (süzgeç çalışıyor),
///   - iki AKTİF kayıt aynı kodu alamaz (benzersizlik gevşemedi).
/// İkincisi olmadan "süzgeç ekledik" diye kısıtı tümden kaldırmış
/// olabilirdik ve kimse fark etmezdi.
///
/// LİSTE DIŞARIDA BIRAKILANLARI DA SABİTLİYOR: eşleştirme anahtarı
/// olan kodlar (muhasebe hesabı, proje, stok kalemi, depo, şirket,
/// kasa, şube, cari, mühendislik pozisyonu) SÜZGEÇSİZ kalmalı ve
/// ikinci test bunu ayrıca doğruluyor.
/// </summary>
[Collection("Integration")]
public sealed class YumusakSilmeKodBenzersizligiTests(DatabaseFixture fixture)
{
    /// <summary>Süzgeçli olması GEREKEN dokuz kalem (tablo, indeks).</summary>
    public static TheoryData<string, string> SuzgecliOlmali() => new()
    {
        { "document_categories", "IX_document_categories_CompanyId_Code" },
        { "hr_departments", "IX_hr_departments_CompanyId_Code" },
        { "hr_positions", "IX_hr_positions_CompanyId_Code" },
        { "hr_positions", "IX_hr_positions_DepartmentId_Code" },
        { "hr_shift_definitions", "IX_hr_shift_definitions_CompanyId_Code" },
        { "inventory_attributes", "IX_inventory_attributes_InventoryCategoryId_Code" },
        { "inventory_categories", "IX_inventory_categories_Code" },
        { "warehouse_zones", "IX_warehouse_zones_WarehouseId_Code" },
        { "warehouse_shelves", "IX_warehouse_shelves_WarehouseZoneId_Code" },
        { "warehouse_shelf_levels", "IX_warehouse_shelf_levels_WarehouseShelfId_Code" }
    };

    /// <summary>
    /// SÜZGEÇSİZ KALMASI GEREKENLER — kod bir eşleştirme anahtarı.
    /// Bu liste, paketin KAPSAMINI da sabitliyor: biri sessizce
    /// süzgeçliye çevrilirse test kırmızı verir.
    /// </summary>
    public static TheoryData<string, string> SuzgecsizKalmali() => new()
    {
        { "accounting_accounts", "IX_accounting_accounts_CompanyId_Code" },
        { "projects", "IX_projects_CompanyId_Code" },
        { "inventory_items", "IX_inventory_items_CompanyId_Code" },
        { "warehouses", "IX_warehouses_CompanyId_Code" },
        { "companies", "IX_companies_Code" },
        { "cash_accounts", "IX_cash_accounts_CompanyId_Code" },
        { "branches", "IX_branches_CompanyId_Code" },
        { "current_accounts", "IX_current_accounts_CompanyId_Code" },
        { "engineering_positions", "IX_engineering_positions_CompanyId_Code" }
    };

    private static async Task<string?> IndeksTanimiAsync(AppDbContext db, string indeks)
    {
        await using var komut = db.Database.GetDbConnection().CreateCommand();
        komut.CommandText =
            "select indexdef from pg_indexes where schemaname='public' and indexname=@ad";
        var p = komut.CreateParameter();
        p.ParameterName = "ad"; p.Value = indeks;
        komut.Parameters.Add(p);

        await db.Database.OpenConnectionAsync();
        var sonuc = await komut.ExecuteScalarAsync();
        return sonuc as string;
    }

    [Theory]
    [MemberData(nameof(SuzgecliOlmali))]
    public async Task KullaniciKodu_SuzgecliBenzersizOlmali(string tablo, string indeks)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tanim = await IndeksTanimiAsync(db, indeks);

        Assert.True(tanim is not null, $"{tablo}: '{indeks}' indeksi YOK.");
        Assert.Contains("UNIQUE", tanim!);
        Assert.True(tanim.Contains("WHERE"),
            $"{tablo}: '{indeks}' SÜZGEÇSİZ. Silinen kaydın kodu rehin kalır (Kural 49/a). " +
            "Tanım: " + tanim);
        Assert.Contains("IsDeleted", tanim);
    }

    [Theory]
    [MemberData(nameof(SuzgecsizKalmali))]
    public async Task EslestirmeAnahtari_SuzgecsizKalmali(string tablo, string indeks)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tanim = await IndeksTanimiAsync(db, indeks);

        Assert.True(tanim is not null, $"{tablo}: '{indeks}' indeksi YOK.");
        Assert.Contains("UNIQUE", tanim!);
        Assert.False(tanim.Contains("WHERE"),
            $"{tablo}: '{indeks}' SÜZGEÇLİ olmuş. Bu kod bir EŞLEŞTİRME ANAHTARI; " +
            "silinen kaydın kodu bir daha verilmemeli (Kural 49/b). Tanım: " + tanim);
    }
}
