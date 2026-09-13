using EnderunAI.Api.Data;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ZEMİN, ÜRETİMİN HESAP YAPILANDIRMASINDAN SESSİZCE AYRIŞAMAZ.
///
/// ═══ NEDEN VAR (Kural 81'in en saf hâli, 2026-09-13) ═══
///
/// `TestDataFactory` stok hesaplarını boyut bayrakları OLMADAN
/// kuruyordu. Stok muhasebesi testleri yeşildi; üretimde aynı hat
/// HİÇ ÇALIŞMAMIŞTI — canlıda 150/153/770/740.03.09/379.01 hesaplarına
/// ait tek fiş satırı yoktu. Fark yalnız canlıda görünüyordu ve orada
/// da kimse bakmıyordu.
///
/// Kusur bir testte değil ZEMİNDEYDİ: yeşil, "ürün çalışıyor" değil
/// "zemin ürünü sınamıyor" demekti.
///
/// ═══ BU MUHAFIZ NE ÖLÇER ═══
///
/// Fikstürün KURDUĞU hesapların boyut bayrakları, üretimin beyan edilmiş
/// yapılandırmasıyla (`UretimHesapYapilandirmasi`) BİREBİR mi. Biri
/// sessizce değişirse kırmızı yanar.
///
/// DÜRÜST SINIR: beyan bir BELGEDİR, canlı sorgusu değil. Canlı değişip
/// beyan güncellenmezse muhafız yine yeşil kalır. Beyanın tazeliği
/// insanın sorumluluğunda; muhafızın işi ZEMİN ile BEYAN arasındaki
/// sessiz ayrışmayı kesmek.
/// </summary>
[Collection("Integration")]
public sealed class FiksturHesapYapilandirmasiTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task FiksturHesaplari_UretimBeyaniylaBirebir()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ek = Guid.NewGuid().ToString("N")[..8];
        var proje = await TestDataFactory.CreateProjectAsync(db, ek);
        await TestDataFactory.EnsureStockAccountsAsync(db, proje.CompanyId);

        var kurulan = await db.AccountingAccounts
            .Where(x => x.CompanyId == proje.CompanyId)
            .Select(x => new { x.Code, x.RequiresProject, x.RequiresCostCenter })
            .ToListAsync();

        /*
         * ÖLÇÜM SAĞLIĞI (Kural 48). Fikstür hiç hesap kurmadıysa
         * aşağıdaki karşılaştırma BOŞ KÜME üzerinde koşar ve yeşil
         * yanar — muhafız ölmüş, dünya değişmemiş olur.
         */
        Assert.True(
            kurulan.Count >= UretimHesapYapilandirmasi.Ayarlar.Count,
            $"Fikstür yalnız {kurulan.Count} hesap kurdu; beyan " +
            $"{UretimHesapYapilandirmasi.Ayarlar.Count} hesap içeriyor. " +
            "Karşılaştırma bu hâlde bir şey söylemez.");

        var ayrisan = new List<string>();

        foreach (var beyan in UretimHesapYapilandirmasi.Ayarlar)
        {
            var hesap = kurulan.SingleOrDefault(x => x.Code == beyan.Kod);

            if (hesap is null)
            {
                ayrisan.Add($"{beyan.Kod}: fikstürde HİÇ KURULMAMIŞ");
                continue;
            }

            if (hesap.RequiresProject != beyan.ProjeZorunlu)
            {
                ayrisan.Add(
                    $"{beyan.Kod}: proje zorunluluğu zemin={hesap.RequiresProject} " +
                    $"beyan={beyan.ProjeZorunlu} ({beyan.Gerekce})");
            }

            if (hesap.RequiresCostCenter != beyan.MasrafMerkeziZorunlu)
            {
                ayrisan.Add(
                    $"{beyan.Kod}: masraf merkezi zorunluluğu zemin={hesap.RequiresCostCenter} " +
                    $"beyan={beyan.MasrafMerkeziZorunlu} ({beyan.Gerekce})");
            }
        }

        Assert.True(
            ayrisan.Count == 0,
            "ZEMİN ÜRETİMDEN AYRIŞTI — testler bu hâlde ürünü değil kendini " +
            "sınar:\n  " + string.Join("\n  ", ayrisan));
    }

    /// <summary>
    /// POZİTİF KONTROL: beyan gerçekten OKUNUYOR mu ve içinde boyut
    /// zorunluluğu olan hesap VAR mı? Beyan tümüyle `false` olsaydı
    /// yukarıdaki karşılaştırma, bayrakları hiç kurmayan eski fikstürle
    /// de yeşil kalırdı — yani kusuru görmezdi.
    /// </summary>
    [Fact]
    public void Beyan_BoyutZorunluluguIceriyor()
    {
        Assert.Contains(UretimHesapYapilandirmasi.Ayarlar, x => x.ProjeZorunlu);
        Assert.Contains(UretimHesapYapilandirmasi.Ayarlar, x => x.MasrafMerkeziZorunlu);

        // Bilanço hesapları boyutsuz OLMALI — kararın kendisi.
        foreach (var kod in new[] { "150", "153", "379.01" })
        {
            var ayar = UretimHesapYapilandirmasi.Bul(kod);
            Assert.NotNull(ayar);
            Assert.False(ayar!.ProjeZorunlu, $"{kod} bilanço hesabı; proje zorunlu olmamalı.");
        }

        // 770 merkez gideri: projesi yok, masraf merkezi ZORUNLU.
        var merkez = UretimHesapYapilandirmasi.Bul("770");
        Assert.NotNull(merkez);
        Assert.False(merkez!.ProjeZorunlu);
        Assert.True(merkez.MasrafMerkeziZorunlu);
    }
}
