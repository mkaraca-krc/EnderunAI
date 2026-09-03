using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// DEVRETME — DÖRDÜNCÜ YAZMA YOLU.
///
/// NEDEN VAR: İŞEMRİ/2'nin kapsamı "üç yazma yolu" olarak konmuştu
/// (POST, PUT, Hızır). ÖLÇÜM DÖRDÜNCÜSÜNÜ GÖSTERDİ: `delegate` de
/// `AssignedToUserId` yazıyor ve tür/atama kuralından geçmiyor.
///
/// SESSİZ SONUCU ŞUYDU: personele atanmış bir görev bir kullanıcıya
/// devredilince İKİ ATAMA ALANI DA dolu kalırdı. `GorevAtamaKurali`
/// isteğin içindeki çelişkiyi reddediyor, ama bu yol çelişkiyi KAYDIN
/// İÇİNDE üretiyordu — ve "Yapacak" slotu sessizce kullanıcıyı seçip
/// personeli gizlerdi.
///
/// DERS, ACIL/2'NİN DERSİNİN GENİŞLETİLMİŞ HÂLİ: bir alanın kapısını
/// kurarken o alanı YAZAN bütün fiiller aranır — yalnız POST ve PUT
/// değil, alanı yazan her uç. "Üç yazma yolu" bir sayımdı ve eksikti;
/// `grep` ile doğrulanabilir bir sayım olsaydı dördüncüsü baştan
/// görünürdü.
/// </summary>
[Collection("Integration")]
public sealed class DevretmeAtamaTekilligiTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task PersoneleAtanmisGorevDevredilince_PersonelAlaniTemizlenir()
    {
        Project proje;
        Personnel personel;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            proje = await TestDataFactory.CreateProjectAsync(db, "DEVRET-1");
            personel = await TestDataFactory.CreatePersonnelAsync(
                db, proje.CompanyId, "DEVRET-1");
        }

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var olustur = await client.PostAsJsonAsync("/api/tasks", new
        {
            companyId = proje.CompanyId,
            projectId = proje.Id,
            title = "Personelden devredilecek",
            priority = (int)WorkTaskPriority.Normal,
            kind = (int)WorkTaskKind.IsEmri,
            assignedToPersonnelId = personel.Id,
        });
        Assert.Equal(HttpStatusCode.OK, olustur.StatusCode);

        using var belge = JsonDocument.Parse(
            await olustur.Content.ReadAsStringAsync());
        var id = belge.RootElement.GetProperty("id").GetGuid();

        // DEVRALAN: görevi görebilen bir kullanıcı olmalı. Admin'e
        // devrediyoruz — kapı "görebilen kişi" arıyor, "başkası" değil.
        Guid benimId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            benimId = (await db.Users.AsNoTracking()
                .SingleAsync(x => x.Username == AuthHelper.AdminUsername)).Id;
        }

        var devret = await client.PostAsJsonAsync($"/api/tasks/{id}/delegate", new
        {
            toUserId = benimId,
            reason = "Saha işi ofise alındı",
        });
        Assert.Equal(HttpStatusCode.OK, devret.StatusCode);

        /*
         * ASIL İDDİA — KAYITTAN OKUNUYOR, CEVAPTAN DEĞİL.
         *
         * Cevap gövdesi doğru görünse bile kayıt çelişkili kalabilirdi;
         * ölçüm veritabanına bakıyor.
         */
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var kayit = await db.WorkTasks.AsNoTracking()
                .SingleAsync(x => x.Id == id);

            Assert.Equal(benimId, kayit.AssignedToUserId);
            Assert.Null(kayit.AssignedToPersonnelId);
        }
    }

    [Fact]
    public async Task KullaniciyaAtanmisGorevDevredilince_DavranisDegismedi_POZITIF_KONTROL()
    {
        /*
         * POZİTİF KONTROL: yukarıdaki test, devretme HER ZAMAN atamayı
         * boşaltsa da yeşil kalırdı. Bu test normal devretmenin hâlâ
         * çalıştığını gösteriyor — düzeltme mevcut davranışı bozmadı.
         */
        Project proje;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            proje = await TestDataFactory.CreateProjectAsync(db, "DEVRET-2");
        }

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Guid benimId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            benimId = (await db.Users.AsNoTracking()
                .SingleAsync(x => x.Username == AuthHelper.AdminUsername)).Id;
        }

        var olustur = await client.PostAsJsonAsync("/api/tasks", new
        {
            companyId = proje.CompanyId,
            projectId = proje.Id,
            title = "Kullanıcıdan devredilecek",
            priority = (int)WorkTaskPriority.Normal,
            kind = (int)WorkTaskKind.IsEmri,
        });
        Assert.Equal(HttpStatusCode.OK, olustur.StatusCode);

        using var belge = JsonDocument.Parse(
            await olustur.Content.ReadAsStringAsync());
        var id = belge.RootElement.GetProperty("id").GetGuid();

        var devret = await client.PostAsJsonAsync($"/api/tasks/{id}/delegate", new
        {
            toUserId = benimId,
            reason = "Normal devretme",
        });

        Assert.Equal(HttpStatusCode.OK, devret.StatusCode);

        using var sonuc = JsonDocument.Parse(
            await devret.Content.ReadAsStringAsync());

        Assert.Equal(
            benimId,
            sonuc.RootElement.GetProperty("assignedToUserId").GetGuid());
    }
}
