using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// İŞ EMRİ KAPISI — SUNUCU TARAFI.
///
/// NEDEN VAR: İŞEMRİ/1 paketi iş emri ekranını menüde öne çıkarıyor.
/// Paketin kendisi ön yüzde: etiket, menü sırası, boş ekran bağlantısı.
/// Ön yüzde bir düğmeyi gizlemek GÜVENLİK ÖNLEMİ DEĞİLDİR — asıl kapı
/// burada, uçta. Ekranı görünür kılmadan önce kapının gerçekten kapalı
/// olduğunu kanıtlıyoruz.
///
/// İki iddia, ikisi de sabote edilerek doğrulandı (S1, S2b):
///   S1  başlıksız iş emri açılamaz.
///   S2b tasks.manage olmayan kullanıcı POST atarsa 403 alır.
/// </summary>
[Collection("Integration")]
public sealed class IsEmriKapisiTests(DatabaseFixture fixture)
{
    /*
     * ROL ÖLÇÜLDÜ, SEÇİLMEDİ.
     *
     * Canlı veritabanında `tasks.view` ve `tasks.manage` yalnızca iki
     * rolde var: Admin ve Genel Müdür. Diğer on üç rolde ikisi de yok.
     * "Şantiye Şefi" bunlardan biri — sahada iş emrini en çok
     * ilgilendiren rol olduğu için seçildi.
     */
    private const string YetkisizRol = "Şantiye Şefi";

    private static async Task<Project> ProjeAsync(DatabaseFixture fixture, string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await TestDataFactory.CreateProjectAsync(db, suffix);
    }

    [Fact]
    public async Task S1_BosBaslikliIsEmriAcilamaz()
    {
        var proje = await ProjeAsync(fixture, "ISEMRI-S1");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync("/api/tasks", new
        {
            companyId = proje.CompanyId,
            projectId = proje.Id,
            title = "   ",
            priority = (int)WorkTaskPriority.Normal,
        });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);

        /*
         * TAM CÜMLE ARANIYOR — KÖK DEĞİL.
         *
         * İlk yazdığımda `Contains("başlık")` aradım ve test kırmızı
         * verdi. Uç DOĞRU cevabı vermişti:
         *   {"message":"Görev başlığı zorunludur."}
         *
         * Türkçede son ünsüz yumuşar: başlık -> başlığı. "başlık"
         * dizisi o cümlede GEÇMEZ. Arıza kodda değil, aramadaydı.
         * Kök arayan bir iddia Türkçede sessizce yanlış yere düşer.
         */
        var govde = await yanit.Content.ReadAsStringAsync();
        Assert.Contains("Görev başlığı zorunludur.", govde);
    }

    [Fact]
    public async Task S1b_BaslikliIsEmriAcilir_PozitifKontrol()
    {
        /*
         * POZİTİF KONTROL: yukarıdaki test, uç TAMAMEN kırık olsa da
         * (her isteğe 400 dönse de) yeşil kalırdı. Bu test o ihtimali
         * kapatıyor: aynı istek, yalnızca başlık dolu.
         */
        var proje = await ProjeAsync(fixture, "ISEMRI-S1B");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync("/api/tasks", new
        {
            companyId = proje.CompanyId,
            projectId = proje.Id,
            title = "Sonda iş emri",
            priority = (int)WorkTaskPriority.Normal,
        });

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    }

    [Fact]
    public async Task S2b_TasksManageOlmayanKullaniciIsEmriAcamaz()
    {
        var proje = await ProjeAsync(fixture, "ISEMRI-S2");

        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "isemri-yetkisiz", [YetkisizRol]);

        var yanit = await client.PostAsJsonAsync("/api/tasks", new
        {
            companyId = proje.CompanyId,
            projectId = proje.Id,
            title = "Yetkisiz iş emri",
            priority = (int)WorkTaskPriority.Normal,
        });

        Assert.Equal(HttpStatusCode.Forbidden, yanit.StatusCode);
    }

    [Fact]
    public async Task S3b_TasksViewOlmayanKullaniciListeyiGoremez()
    {
        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "isemri-liste-yetkisiz", [YetkisizRol]);

        var yanit = await client.GetAsync("/api/tasks");

        Assert.Equal(HttpStatusCode.Forbidden, yanit.StatusCode);
    }
}
