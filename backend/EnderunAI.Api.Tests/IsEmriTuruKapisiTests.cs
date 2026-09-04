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
/// GÖREV TÜRÜ VE PERSONEL ATAMASI — UÇTAN UCA KAPI.
///
/// `GorevAtamaKuraliTests` kuralın DOĞRU olduğunu gösteriyor; bu dosya
/// kuralın gerçekten ÇAĞRILDIĞINI. İkisi ayrı iddia: doğru bir kural
/// hiç çağrılmadan da yeşil kalabilir — bu kod tabanında tam olarak bu
/// oldu (`2d90c946`, atama kapısının sessizce silinmesi).
///
/// ÜÇ YAZMA YOLU VAR: POST, PUT ve Hızır'ın hatırlatma aracı. Hızır
/// denetleyiciyi hiç görmüyor; onun türü `HizirGorevTuruTests` içinde
/// ayrıca ölçülüyor. Bir kapı eksiği bulunduğunda aynı kaynağın BÜTÜN
/// yazma fiilleri aynı turda sınanır — ACIL/2'nin dersi.
///
/// ÖLÇÜMÜN DOĞUŞU (2026-09-02, canlı): 79 aktif personel, 13 kullanıcı
/// hesabı, aralarında SIFIR bağ. Personelin ezici çoğunluğuna
/// `AssignedToUserId` ile iş verilemiyordu; `AssignedToPersonnelId` bu
/// yüzden var.
/// </summary>
[Collection("Integration")]
public sealed class IsEmriTuruKapisiTests(DatabaseFixture fixture)
{
    private static async Task<Project> ProjeAsync(DatabaseFixture fixture, string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await TestDataFactory.CreateProjectAsync(db, suffix);
    }

    private static async Task<Personnel> PersonelAsync(
        DatabaseFixture fixture, Guid companyId, string suffix,
        PersonnelStatus durum = PersonnelStatus.Active)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var personel = await TestDataFactory.CreatePersonnelAsync(db, companyId, suffix);

        if (durum != PersonnelStatus.Active)
        {
            personel.Status = durum;
            await db.SaveChangesAsync();
        }

        return personel;
    }

    private static Dictionary<string, object?> Govde(
        Project proje, string baslik, int? tur, Guid? personel = null,
        Guid? kullanici = null)
    {
        var govde = new Dictionary<string, object?>
        {
            ["companyId"] = proje.CompanyId,
            ["projectId"] = proje.Id,
            ["title"] = baslik,
            ["priority"] = (int)WorkTaskPriority.Normal,
        };

        // TÜR HİÇ GÖNDERİLMEYEN DURUM AYRI SINANIYOR: `null` geçmek ile
        // alanı GÖNDERMEMEK aynı şey değil. Gerçek istemci alanı hiç
        // göndermez; kapı o durumu da yakalamalı.
        if (tur.HasValue) govde["kind"] = tur.Value;
        if (personel.HasValue) govde["assignedToPersonnelId"] = personel.Value;
        if (kullanici.HasValue) govde["assignedToUserId"] = kullanici.Value;

        return govde;
    }

    // ───────── S1: tür zorunlu ─────────

    [Fact]
    public async Task S1_TurGonderilmezse_Reddedilir()
    {
        var proje = await ProjeAsync(fixture, "TUR-S1");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks", Govde(proje, "Türsüz iş emri", tur: null));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "Görev türü zorunludur",
            await yanit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task S1b_TurGonderilirse_Kabul_POZITIF_KONTROL()
    {
        /*
         * POZİTİF KONTROL: S1, uç her isteğe 400 dönse de yeşil
         * kalırdı. Aynı istek, yalnızca tür eklenmiş.
         */
        var proje = await ProjeAsync(fixture, "TUR-S1B");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks",
            Govde(proje, "Türlü iş emri", (int)WorkTaskKind.IsEmri));

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    }

    [Fact]
    public async Task S1c_SifirTuru_AcikcaGonderilse_De_Reddedilir()
    {
        /*
         * ALANI GÖNDERMEMEK İLE SIFIR GÖNDERMEK AYNI SONUCU VERMELİ.
         *
         * `Belirsiz = 0` geçerli bir seçim gibi görünüyor: enum'da
         * tanımlı, sayısı var. Kapı onu reddetmeseydi, tür alanı
         * "seçmedim" değerini SAKLAYABİLİR ve hiçbir şey ölçmezdi.
         */
        var proje = await ProjeAsync(fixture, "TUR-S1C");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks", Govde(proje, "Sıfır türlü", tur: 0));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    // ───────── S2: iki atama birden ─────────

    [Fact]
    public async Task S2_KullaniciVePersonelBirlikte_Reddedilir()
    {
        var proje = await ProjeAsync(fixture, "TUR-S2");
        var personel = await PersonelAsync(fixture, proje.CompanyId, "TUR-S2");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks",
            Govde(proje, "İki atamalı", (int)WorkTaskKind.IsEmri,
                personel: personel.Id, kullanici: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "ikisi birden seçilemez",
            await yanit.Content.ReadAsStringAsync());
    }

    // ───────── S3: personel kapısı ─────────

    [Fact]
    public async Task S3_OlmayanPersonel_Reddedilir()
    {
        var proje = await ProjeAsync(fixture, "TUR-S3");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks",
            Govde(proje, "Hayalet personel", (int)WorkTaskKind.IsEmri,
                personel: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "personel bulunamadı",
            await yanit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task S3b_IstenAyrilmisPersonel_Reddedilir()
    {
        /*
         * CANLIDA ÖLÇÜLDÜ: 81 personelin 2'si `Terminated`. İşten
         * ayrılmış birine iş emri açmak, kapanmayacak bir görev üretir.
         */
        var proje = await ProjeAsync(fixture, "TUR-S3B");
        var personel = await PersonelAsync(
            fixture, proje.CompanyId, "TUR-S3B", PersonnelStatus.Terminated);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks",
            Govde(proje, "Ayrılmışa iş emri", (int)WorkTaskKind.IsEmri,
                personel: personel.Id));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "aktif çalışan değil",
            await yanit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task S3c_AktifPersonel_Kabul_VE_AdiYapacakSlotunda_POZITIF_KONTROL()
    {
        /*
         * İKİ İDDİA BİR ARADA — BİLEREK.
         *
         * S3 ve S3b, uç her personel atamasını reddetse de yeşil
         * kalırdı. Bu test kabulü gösteriyor.
         *
         * AMA KABUL YETMİYOR: paketin asıl amacı detay ekranındaki BOŞ
         * "Yapacak" slotunu doldurmak. Kayıt yazılıp adı çözülmezse
         * ekran yine boş kalır ve "çalışıyor" derdik. O yüzden aynı
         * turda `assignedToDisplayName` de ölçülüyor.
         */
        var proje = await ProjeAsync(fixture, "TUR-S3C");
        var personel = await PersonelAsync(fixture, proje.CompanyId, "TUR-S3C");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks",
            Govde(proje, "Personele iş emri", (int)WorkTaskKind.IsEmri,
                personel: personel.Id));

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        using var belge = JsonDocument.Parse(
            await yanit.Content.ReadAsStringAsync());
        var kok = belge.RootElement;

        Assert.Equal(
            personel.Id,
            kok.GetProperty("assignedToPersonnelId").GetGuid());

        Assert.Equal(
            $"{personel.FirstName} {personel.LastName}",
            kok.GetProperty("assignedToDisplayName").GetString());

        // KULLANICI ALANI BOŞ KALMALI: tek kaynak iddiasının öteki yarısı.
        Assert.Equal(
            JsonValueKind.Null,
            kok.GetProperty("assignedToName").ValueKind);
    }

    [Fact]
    public async Task S3d_MerkezsizGorevde_De_PersonelAdiCozulur()
    {
        /*
         * ═══ KURULUM DEĞİŞTİ — KURAL-KATMAN/1 (2026-09-04) ═══
         *
         * Bu test merkezsiz bir görevi `sourceModule` KAÇIŞINDAN
         * geçerek açıyordu. Kaçış kapatıldı (merkez artık koşulsuz
         * zorunlu), dolayısıyla o kurulum artık 400 döner.
         *
         * Karar önceden kayıtlıydı: "S3d KORUNACAK, kurulumu
         * değişecek" — çünkü bu test bir KUSURU değil bir DAVRANIŞI
         * sabitliyor. (Kusuru sabitleyen `ACIK_KAPI` testi ise
         * tersine çevrildi.)
         *
         * YENİ KURULUM: görev doğrudan veritabanına yazılıyor.
         * Merkezsiz bir görev API'den artık AÇILAMAZ, ama CANLIDA
         * VAR OLABİLİR — kaçış kapanmadan önce açılmış kayıtlar
         * duruyor. Ekranın onları doğru göstermesi gerekiyor.
         *
         * ── BU TEST ERKEN ÇIKIŞI SINAMIYOR ──
         *
         * `AssignedByUserId` BİLEREK DOLU: erken çıkış koşulu
         * sağlanmasın diye. Erken çıkışın kendisi `S3e`'de sınanıyor
         * ve orada tüm kullanıcı alanları boş.
         *
         * Bu ayrım ölçümle öğrenildi: sonda G ilan edilen kırmızıyı
         * vermedi, çünkü S3d'nin sandığım şeyi sınamadığı ortaya
         * çıktı.
         */
        Project proje;
        Personnel personel;
        Guid gorevId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            proje = await TestDataFactory.CreateProjectAsync(db, "TUR-S3D");
            personel = await TestDataFactory.CreatePersonnelAsync(
                db, proje.CompanyId, "TUR-S3D");

            var yonetici = await db.Users.SingleAsync(
                x => x.Username == AuthHelper.AdminUsername);

            var gorev = new WorkTask
            {
                CompanyId = proje.CompanyId,
                TaskNumber = "GRV-SONDA-S3D",
                Title = "Merkezsiz, yalnız personele atanmış",
                Kind = WorkTaskKind.IsEmri,
                Status = WorkTaskStatus.Open,
                AssignedToPersonnelId = personel.Id,

                // MERKEZ YOK — testin konusu bu.
                ProjectId = null,
                BranchId = null,
                ProjectSiteId = null,

                // KULLANICI ALANI DOLU: erken çıkış tetiklenmesin
                // (o, S3e'nin konusu).
                AssignedByUserId = yonetici.Id,
            };

            db.WorkTasks.Add(gorev);
            await db.SaveChangesAsync();
            gorevId = gorev.Id;
        }

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var yanit = await client.GetAsync($"/api/tasks/{gorevId}");

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        using var belge = JsonDocument.Parse(
            await yanit.Content.ReadAsStringAsync());

        Assert.Equal(
            $"{personel.FirstName} {personel.LastName}",
            belge.RootElement.GetProperty("assignedToDisplayName").GetString());
    }

    [Fact]
    public async Task S3e_KullanicisiHicOlmayanGorevde_PersonelAdiCozulur()
    {
        /*
         * ERKEN ÇIKIŞ — GERÇEKTEN BURADA SINANIYOR.
         *
         * `AdlariGetirAsync` çözülecek hiçbir şey yoksa erken çıkıyor.
         * O liste bir kez zaten eksik kalmıştı: merkezi olan ama
         * kullanıcısı olmayan görevde merkez adı çözülmüyordu. Personel
         * eklenince aynı hatanın üçüncü biçimi doğdu.
         *
         * NEDEN KAYIT DOĞRUDAN YAZILIYOR, UÇTAN GEÇİLMİYOR: uçlar
         * `AssignedByUserId`'yi HER ZAMAN yazıyor, dolayısıyla erken
         * çıkışın koşulu API üzerinden hiç sağlanamıyor (S3d'nin
         * sondası bunu gösterdi). Koşulu sağlayan tek şekil, hiçbir
         * kullanıcı kimliği taşımayan bir kayıt.
         *
         * BU ŞEKİL BUGÜN ÜRETİLMİYOR AMA MÜMKÜN: bir içe aktarma ya da
         * arka plan işi, isteyeni olmayan bir görevi personele
         * atayabilir. Savunma o gün için duruyor — ve o gün geldiğinde
         * TESTSİZ olmasın diye bu test var. Bu kod tabanında testsiz
         * savunma tam olarak böyle sessizce siliniyor (`2d90c946`).
         */
        Project proje;
        Personnel personel;
        Guid gorevId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            proje = await TestDataFactory.CreateProjectAsync(db, "TUR-S3E");
            personel = await TestDataFactory.CreatePersonnelAsync(
                db, proje.CompanyId, "TUR-S3E");

            var gorev = new WorkTask
            {
                CompanyId = proje.CompanyId,
                TaskNumber = "GRV-SONDA-S3E",
                Title = "Kullanıcısız, yalnız personele atanmış",
                Kind = WorkTaskKind.IsEmri,
                Status = WorkTaskStatus.Open,
                AssignedToPersonnelId = personel.Id,
                // KULLANICI ALANLARININ HEPSİ BOŞ — erken çıkışın
                // koşulunu sağlayan tek şekil bu.
                AssignedToUserId = null,
                AssignedByUserId = null,
                SourceModule = "TEST",
            };
            db.WorkTasks.Add(gorev);
            await db.SaveChangesAsync();
            gorevId = gorev.Id;
        }

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var yanit = await client.GetAsync($"/api/tasks/{gorevId}");

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        using var belge = JsonDocument.Parse(
            await yanit.Content.ReadAsStringAsync());

        Assert.Equal(
            $"{personel.FirstName} {personel.LastName}",
            belge.RootElement.GetProperty("assignedToDisplayName").GetString());
    }

    // ───────── S4: PUT aynı kapıdan geçer ─────────

    [Fact]
    public async Task S4_PUT_TurBelirsizeCevrilemez()
    {
        /*
         * ACIL/2'NİN DERSİ. Kapı yalnız POST'a konsaydı, tür güncelleme
         * ile `Belirsiz`e çevrilebilir ve alan bir gün sessizce boşalırdı.
         */
        var proje = await ProjeAsync(fixture, "TUR-S4");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var olustur = await client.PostAsJsonAsync(
            "/api/tasks",
            Govde(proje, "Güncellenecek", (int)WorkTaskKind.IsEmri));
        Assert.Equal(HttpStatusCode.OK, olustur.StatusCode);

        using var belge = JsonDocument.Parse(
            await olustur.Content.ReadAsStringAsync());
        var id = belge.RootElement.GetProperty("id").GetGuid();

        var yanit = await client.PutAsJsonAsync($"/api/tasks/{id}", new
        {
            title = "Türü silinmiş",
            priority = (int)WorkTaskPriority.Normal,
            projectId = proje.Id,
            // kind BİLEREK YOK
        });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "Görev türü zorunludur",
            await yanit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task S4b_PUT_IkiAtamaBirden_Reddedilir()
    {
        var proje = await ProjeAsync(fixture, "TUR-S4B");
        var personel = await PersonelAsync(fixture, proje.CompanyId, "TUR-S4B");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var olustur = await client.PostAsJsonAsync(
            "/api/tasks",
            Govde(proje, "İki atamaya çevrilecek", (int)WorkTaskKind.IsEmri));
        Assert.Equal(HttpStatusCode.OK, olustur.StatusCode);

        using var belge = JsonDocument.Parse(
            await olustur.Content.ReadAsStringAsync());
        var id = belge.RootElement.GetProperty("id").GetGuid();

        var yanit = await client.PutAsJsonAsync($"/api/tasks/{id}", new
        {
            title = "İki atamalı güncelleme",
            priority = (int)WorkTaskPriority.Normal,
            projectId = proje.Id,
            kind = (int)WorkTaskKind.IsEmri,
            assignedToUserId = Guid.NewGuid(),
            assignedToPersonnelId = personel.Id,
        });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "ikisi birden seçilemez",
            await yanit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task S4c_PUT_PersoneleAtama_Kabul_POZITIF_KONTROL()
    {
        var proje = await ProjeAsync(fixture, "TUR-S4C");
        var personel = await PersonelAsync(fixture, proje.CompanyId, "TUR-S4C");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var olustur = await client.PostAsJsonAsync(
            "/api/tasks",
            Govde(proje, "Personele devredilecek", (int)WorkTaskKind.IsEmri));
        Assert.Equal(HttpStatusCode.OK, olustur.StatusCode);

        using var belge = JsonDocument.Parse(
            await olustur.Content.ReadAsStringAsync());
        var id = belge.RootElement.GetProperty("id").GetGuid();

        var yanit = await client.PutAsJsonAsync($"/api/tasks/{id}", new
        {
            title = "Personele devredildi",
            priority = (int)WorkTaskPriority.Normal,
            projectId = proje.Id,
            kind = (int)WorkTaskKind.Hatirlatma,
            assignedToPersonnelId = personel.Id,
        });

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        using var sonuc = JsonDocument.Parse(
            await yanit.Content.ReadAsStringAsync());

        Assert.Equal(
            (int)WorkTaskKind.Hatirlatma,
            sonuc.RootElement.GetProperty("kind").GetInt32());

        Assert.Equal(
            $"{personel.FirstName} {personel.LastName}",
            sonuc.RootElement.GetProperty("assignedToDisplayName").GetString());
    }

    // ───────── MERKEZ ZORUNLULUĞU TÜRE BAĞLI (AK-1) ─────────

    /// <summary>
    /// Merkezsiz gövde: şirket ve başlık var, proje/şube/şantiye YOK.
    /// </summary>
    private static Dictionary<string, object?> MerkezsizGovde(
        Guid sirket, string baslik, int tur) =>
        new()
        {
            ["companyId"] = sirket,
            ["title"] = baslik,
            ["priority"] = (int)WorkTaskPriority.Normal,
            ["kind"] = tur
        };

    /// <summary>
    /// İDDİA: merkezsiz İŞ EMRİ uçtan reddedilir.
    ///
    /// `MasrafMerkeziKuraliTests` kuralın doğru olduğunu gösteriyor;
    /// bu test kuralın gerçekten ÇAĞRILDIĞINI. İkisi ayrı iddia.
    /// </summary>
    [Fact]
    public async Task MerkezsizIsEmri_UctanREDDEDILIR()
    {
        var proje = await ProjeAsync(fixture, "MRK-IE");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks",
            MerkezsizGovde(proje.CompanyId, "Merkezsiz iş emri",
                           (int)WorkTaskKind.IsEmri));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);

        var govde = await yanit.Content.ReadAsStringAsync();
        Assert.Contains("Masraf merkezi zorunludur", govde);
    }

    /// <summary>
    /// İDDİA: merkezsiz HATIRLATMA uçtan KABUL edilir.
    ///
    /// BU TEST OLMADAN ÜSTTEKİ YALAN SÖYLEYEBİLİRDİ: merkez her tür
    /// için zorunlu olsaydı da o test yeşil kalırdı. İkisi birlikte
    /// "tür okunuyor mu" sorusunu cevaplıyor.
    ///
    /// Ayrıca bu, AK-1'in düzeltilmiş kararının canlıdaki karşılığı:
    /// şube kapsamı olan kullanıcı 0/13 olduğu için merkez
    /// zorunluluğu hatırlatmaya uygulansaydı özellik herkeste ölürdü.
    /// </summary>
    [Fact]
    public async Task MerkezsizHatirlatma_UctanKABUL_EDILIR()
    {
        var proje = await ProjeAsync(fixture, "MRK-HT");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks",
            MerkezsizGovde(proje.CompanyId, "Merkezsiz hatırlatma",
                           (int)WorkTaskKind.Hatirlatma));

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    }
}
