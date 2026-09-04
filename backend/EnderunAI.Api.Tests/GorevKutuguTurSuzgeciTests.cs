using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// GÖREV KÜTÜĞÜ — TÜR SÜZGECİ (TUR 1.3).
///
/// İDDİA: `/api/tasks` VARSAYILAN olarak yalnız iş emri döndürür.
/// Hızır hatırlatmaları kişinin kendine koyduğu notlar; iş kütüğünde
/// durunca liste iş takibi için okunamaz hâle geliyordu.
///
/// BU BİR GİZLEME DEĞİL DARALTMA — ve fark ölçülebilir olmalı, yoksa
/// "kayıt kayboldu" ile "kayıt süzüldü" aynı görünür. Üç test tam da
/// bunu ayırıyor:
///   · süzgeçsiz  -> yalnız iş emri
///   · kind=2     -> yalnız hatırlatma
///   · kind=0     -> ikisi de   (POZİTİF KONTROL: kayıtlar duruyor)
///
/// Üçüncüsü olmadan ilk ikisi, hatırlatmanın hiç YAZILMAMIŞ olmasıyla
/// da yeşil kalırdı. Kural 48: boş sonuç yokluğun kanıtı değildir.
/// </summary>
[Collection("Integration")]
public sealed class GorevKutuguTurSuzgeciTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Aynı projeye bir iş emri ve bir hatırlatma yazar, ikisinin
    /// görev numarasını döndürür. Kütükte başka testlerin kayıtları da
    /// var; iddialar bu İKİ numara üzerinden kuruluyor, liste
    /// uzunluğu üzerinden değil — sıra bağımlı test yazmamak için.
    /// </summary>
    private async Task<(string isEmri, string hatirlatma)> IkiGorevAsync(string ek)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(db, ek);

        var isEmri = $"TEST-SZG-{ek}-IE";
        var hatirlatma = $"TEST-SZG-{ek}-HT";

        db.WorkTasks.Add(new WorkTask
        {
            CompanyId = proje.CompanyId,
            ProjectId = proje.Id,
            TaskNumber = isEmri,
            Title = "Süzgeç testi — iş emri",
            Kind = WorkTaskKind.IsEmri,
            Status = WorkTaskStatus.Open
        });

        db.WorkTasks.Add(new WorkTask
        {
            CompanyId = proje.CompanyId,
            ProjectId = proje.Id,
            TaskNumber = hatirlatma,
            Title = "Süzgeç testi — hatırlatma",
            Kind = WorkTaskKind.Hatirlatma,
            Status = WorkTaskStatus.Open
        });

        await db.SaveChangesAsync();
        return (isEmri, hatirlatma);
    }

    private static async Task<HashSet<string>> NumaralarAsync(
        HttpClient client, string yol)
    {
        var yanit = await client.GetAsync(yol);
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        var govde = await yanit.Content.ReadFromJsonAsync<JsonElement>();

        return govde.GetProperty("items")
            .EnumerateArray()
            .Select(x => x.GetProperty("taskNumber").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public async Task Varsayilan_YalnizIsEmri()
    {
        var (isEmri, hatirlatma) = await IkiGorevAsync("VRS");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var numaralar = await NumaralarAsync(client, "/api/tasks?pageSize=200");

        Assert.Contains(isEmri, numaralar);
        Assert.DoesNotContain(hatirlatma, numaralar);
    }

    [Fact]
    public async Task KindIki_YalnizHatirlatma()
    {
        var (isEmri, hatirlatma) = await IkiGorevAsync("KND2");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var numaralar = await NumaralarAsync(client, "/api/tasks?kind=2&pageSize=200");

        Assert.Contains(hatirlatma, numaralar);
        Assert.DoesNotContain(isEmri, numaralar);
    }

    [Fact]
    public async Task KindSifir_IkisiDe_POZITIF_KONTROL()
    {
        /*
         * BU TEST OLMADAN DİĞER İKİSİ YALAN SÖYLEYEBİLİRDİ.
         *
         * Hatırlatma hiç yazılamamış olsaydı (kısıt, zorunlu alan,
         * kapsam) `Varsayilan_YalnizIsEmri` yine yeşil olurdu ve
         * ölçtüğü şey süzgeç değil, kendi kurulumunun sessiz düşüşü
         * olurdu. `kind=0` ikisinin de KÜTÜKTE DURDUĞUNU gösteriyor.
         */
        var (isEmri, hatirlatma) = await IkiGorevAsync("KND0");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var numaralar = await NumaralarAsync(client, "/api/tasks?kind=0&pageSize=200");

        Assert.Contains(isEmri, numaralar);
        Assert.Contains(hatirlatma, numaralar);
    }
}
