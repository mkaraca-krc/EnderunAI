using System.Net.Http.Json;
using EnderunAI.Api.Tests.Infrastructure;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// SAĞLIK UCU SÜRÜM TAŞIR — "CANLIDA HANGİ KOD VAR" SORUSU ÇAĞRIYLA
/// CEVAPLANIR (SÜRÜM/1).
///
/// ═══ NEDEN VAR ═══
///
/// ÖLÇÜLDÜ (2026-09-13): `/api/health` yalnız `status/service/utc`
/// döndürüyordu; canlıya "sen hangi kodsun?" diye sorulamıyordu.
/// "Şu düzeltme canlıda mı?" sorusu dosya tarihleriyle cevaplanmak
/// zorunda kaldı — OTURUM/1'de `.next` parça tarihlerine bakıldı ve o
/// dizinde BAYAT ARTIKLAR vardı. Dosya tarihi ipucudur, kanıt değildir.
///
/// Bu test alanların VARLIĞINI tutuyor; değerin doğruluğunu değil.
/// Sürüm ancak yayında `-p:SourceRevisionId` ile gömülür, test
/// ortamında "bilinmiyor" olabilir — o hâl de geçerlidir ve
/// SESSİZ DEĞİLDİR (alan var, "bilinmiyor" yazar).
/// </summary>
[Collection("Integration")]
public sealed class SurumUcuTests(DatabaseFixture fixture)
{
    private sealed record SaglikYaniti(
        string? Status, string? Service, DateTime? Utc,
        string? Surum, DateTime? YapiUtc);

    [Fact]
    public async Task SaglikUcu_SurumVeYapiZamaniTasir()
    {
        var istemci = fixture.Factory.CreateClient();

        var yanit = await istemci.GetAsync("/api/health");
        yanit.EnsureSuccessStatusCode();

        var govde = await yanit.Content.ReadFromJsonAsync<SaglikYaniti>();

        Assert.NotNull(govde);
        Assert.Equal("ok", govde!.Status);

        // ALAN VAR VE BOŞ DEĞİL. Boş bırakmak, sürümü bilmediğimizi
        // bildiğimiz hâli gizlerdi.
        Assert.False(string.IsNullOrWhiteSpace(govde.Surum),
            "Sağlık ucu `surum` alanı taşımıyor — canlıya hangi kod olduğu sorulamaz.");

        Assert.NotNull(govde.YapiUtc);
        Assert.True(govde.YapiUtc > new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            $"Yapı zamanı makul değil: {govde.YapiUtc}");
    }

    [Fact]
    public async Task SaglikUcu_JETONSUZ_Cagrilabilir_POZITIF_KONTROL()
    {
        // safe-deploy bu uca jetonsuz bakıyor. Sürüm alanı eklerken
        // ucu yanlışlıkla yetkiye bağlamak, HER YAYINI düşürürdü.
        var istemci = fixture.Factory.CreateClient();
        var yanit = await istemci.GetAsync("/api/health");
        Assert.True(yanit.IsSuccessStatusCode,
            $"Sağlık ucu jetonsuz çağrılamıyor: {yanit.StatusCode}");
    }
}
