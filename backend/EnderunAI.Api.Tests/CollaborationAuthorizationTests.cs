using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.GoodsReceipt;
using EnderunAI.Api.Services.Collaboration;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// YORUM KAPISI — TİP BAŞINA YETKİ.
///
/// KAPATILAN AÇIK: kapı yalnızca VERİ KAPSAMI bakıyordu, İZİN
/// bakmıyordu; `CollaborationController` üzerinde hiç
/// `RequirePermission` yok. Sonuç: hakediş/çek/teklif görme izni
/// OLMAYAN bir kullanıcı, şirket kapsamı yettiği sürece o
/// kayıtların yorumunu okuyabiliyor ve ek dosyasını
/// indirebiliyordu — ekranı hiç açmadan, doğrudan uca giderek.
///
/// "Liste ekranını görebilmek, TEK BİR KAYDIN yorumunu görebilmek
/// demek DEĞİLDİR" ayrımı burada zorlanıyor.
///
/// YETKİSİZ YANIT 404, 403 DEĞİL: 403 "bu kayıt VAR ama sana
/// kapalı" der ve kimlik deneyerek varlık taraması yapmayı mümkün
/// kılar.
/// </summary>
[Collection("Integration")]
public sealed class CollaborationAuthorizationTests(DatabaseFixture fixture)
{
    /*
     * YETKİLİ / YETKİSİZ ROL ÇİFTLERİ — canlıdan ölçülen dağılıma
     * göre seçildi. "Araç Sorumlusu" beş iznin HİÇBİRİNE sahip
     * değil ama `projects.view` var, yani sisteme girebiliyor ve
     * global veri kapsamı alabiliyor: kapsam yetiyor, izin
     * yetmiyor — sınamak istediğimiz tam bu durum.
     */
    /*
     * YETKİSİZ ROL TİP BAŞINA SEÇİLİYOR — TEK SABİT YETMEZ.
     *
     * İlk sürümde "Araç Sorumlusu" tüm tipler için yetkisiz sayılmıştı.
     * `Project` için bu YANLIŞ: Araç Sorumlusu'nda `projects.view`
     * VAR. Tek sabitle devam etseydim `Project` testi yetkili bir
     * kullanıcıyla "yetkisiz" iddiasını sınayacak ve hep düşecekti —
     * ya da beteri, ben onu "yetkisiz" sanıp geçtiğini görecektim.
     *
     * `projects.view` OLMAYAN üç rol var: Formen, Satış Personeli,
     * Şantiye Şefi.
     */
    public static TheoryData<string, string, string> Tipler() => new()
    {
        //  tip                yetkili rol             yetkisiz rol
        { "WorkTask",         "Admin",                "Araç Sorumlusu" },
        { "Project",          "Teknik Ofis",          "Formen" },
        { "ProgressPayment",  "Finans Sorumlusu",     "Araç Sorumlusu" },
        { "PurchaseRequest",  "Satın Alma Sorumlusu", "Araç Sorumlusu" },
        { "GoodsReceipt",     "Depo Sorumlusu",       "Araç Sorumlusu" },
        { "Offer",            "Teknik Ofis",          "Araç Sorumlusu" },
        { "Cheque",           "Finans Sorumlusu",     "Araç Sorumlusu" }
    };

    /// <summary>Tipe göre gerçek bir kayıt üretir.</summary>
    private static async Task<Guid> KayitAsync(
        AppDbContext db, string tip, string suffix)
    {
        var proje = await TestDataFactory.CreateProjectAsync(db, $"YTK{suffix}");

        switch (tip)
        {
            case "Project":
                return proje.Id;

            case "WorkTask":
            {
                var x = new WorkTask
                {
                    CompanyId = proje.CompanyId,
                    ProjectId = proje.Id,
                    TaskNumber = $"TEST-YTK-{suffix}",
                    Title = "Yetki testi görevi",
                    Kind = WorkTaskKind.IsEmri,
                    Status = WorkTaskStatus.Open
                };
                db.WorkTasks.Add(x);
                await db.SaveChangesAsync();
                return x.Id;
            }

            case "ProgressPayment":
            {
                var x = new ProgressPayment
                {
                    CompanyId = proje.CompanyId,
                    ProjectId = proje.Id,
                    PeriodNumber = 1,
                    ProgressPaymentDate = DateTime.UtcNow
                };
                db.ProgressPayments.Add(x);
                await db.SaveChangesAsync();
                return x.Id;
            }

            case "PurchaseRequest":
            {
                var x = new PurchaseRequest
                {
                    CompanyId = proje.CompanyId,
                    ProjectId = proje.Id
                };
                db.PurchaseRequests.Add(x);
                await db.SaveChangesAsync();
                return x.Id;
            }

            case "GoodsReceipt":
            {
                /*
                 * ZİNCİR GERÇEK KURULUYOR.
                 *
                 * `goods_receipts` hem `purchase_orders` hem
                 * `warehouses` tablolarına yabancı anahtarla bağlı;
                 * sipariş de `rfqs` ve `current_accounts`'a bağlı.
                 * İlk denemede rastgele GUID verdim ve veritabanı
                 * reddetti — kısayol yok.
                 */
                var tedarikci = new CurrentAccount
                {
                    CompanyId = proje.CompanyId,
                    Code = $"TED-{suffix}",
                    Title = $"Test Tedarikçi {suffix}",
                    Roles = CurrentAccountRoles.Supplier,
                    Status = CurrentAccountStatus.Approved
                };
                db.CurrentAccounts.Add(tedarikci);

                var subeId = await db.Branches
                    .Where(b => b.CompanyId == proje.CompanyId)
                    .Select(b => b.Id)
                    .FirstAsync();

                var depo = new Warehouse
                {
                    CompanyId = proje.CompanyId,
                    BranchId = subeId,
                    Code = $"DEP-{suffix}",
                    Name = "Test Depo",
                    Type = WarehouseType.Central
                };
                db.Warehouses.Add(depo);

                var talep = new PurchaseRequest
                {
                    CompanyId = proje.CompanyId,
                    ProjectId = proje.Id,
                    RequestNumber = $"PR-{suffix}",
                    RequestDate = DateTime.UtcNow.Date,
                    RequestedByName = "Test",
                    Priority = PurchaseRequestPriority.Normal,
                    Status = PurchaseRequestStatus.Approved
                };
                db.PurchaseRequests.Add(talep);
                await db.SaveChangesAsync();

                var rfq = new EnderunAI.Api.Models.Rfq.Rfq
                {
                    CompanyId = proje.CompanyId,
                    PurchaseRequestId = talep.Id,
                    RfqNumber = $"RFQ-{suffix}",
                    Title = "Test RFQ",
                    IssueDate = DateTime.UtcNow.Date,
                    Currency = "TRY"
                };
                db.Rfqs.Add(rfq);
                await db.SaveChangesAsync();

                var siparis = new EnderunAI.Api.Models.PurchaseOrder.PurchaseOrder
                {
                    CompanyId = proje.CompanyId,
                    ProjectId = proje.Id,
                    RfqId = rfq.Id,
                    SupplierCurrentAccountId = tedarikci.Id,
                    OrderNumber = $"PO-{suffix}",
                    OrderDate = DateTime.UtcNow.Date,
                    Currency = "TRY",
                    ExchangeRate = 1m
                };
                db.PurchaseOrders.Add(siparis);
                await db.SaveChangesAsync();

                var x = new GoodsReceipt
                {
                    CompanyId = proje.CompanyId,
                    PurchaseOrderId = siparis.Id,
                    WarehouseId = depo.Id,
                    ReceiptNumber = $"GR-{suffix}",
                    ReceiptDate = DateTime.UtcNow.Date,
                    ReceivedByName = "Depo Sorumlusu"
                };
                db.GoodsReceipts.Add(x);
                await db.SaveChangesAsync();
                return x.Id;
            }

            case "Offer":
            {
                var x = new Offer
                {
                    CompanyId = proje.CompanyId,
                    OfferNumber = $"TKL-{suffix}",
                    Title = "Yetki testi teklifi"
                };
                db.Offers.Add(x);
                await db.SaveChangesAsync();
                return x.Id;
            }

            case "Cheque":
            {
                var x = new Cheque
                {
                    CompanyId = proje.CompanyId,
                    Amount = 1000m,
                    AmountTry = 1000m,
                    IssueDate = DateTime.UtcNow,
                    DueDate = DateTime.UtcNow.AddDays(30)
                };
                db.Cheques.Add(x);
                await db.SaveChangesAsync();
                return x.Id;
            }

            default:
                throw new InvalidOperationException($"Bilinmeyen tip: {tip}");
        }
    }

    private static string YorumYolu(string tip, Guid id) =>
        $"/api/collaboration/comments?entityType={tip}&entityId={id}";

    // ---------------------------------------------------------------
    // 1) YETKİLİ OKUR
    // ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Tipler))]
    public async Task Yetkili_YorumlariOkuyabilir(string tip, string yetkiliRol, string _)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var kayitId = await KayitAsync(db, tip, suffix);

        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "yetkili", [yetkiliRol]);

        var yanit = await client.GetAsync(YorumYolu(tip, kayitId));

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    }

    // ---------------------------------------------------------------
    // 2) YETKİSİZ OKUYAMAZ
    // ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Tipler))]
    public async Task Yetkisiz_YorumlariOkuyamaz(string tip, string _, string yetkisizRol)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var kayitId = await KayitAsync(db, tip, suffix);

        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "yetkisiz", [yetkisizRol]);

        var yanit = await client.GetAsync(YorumYolu(tip, kayitId));

        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);
    }

    // ---------------------------------------------------------------
    // 3) YETKİSİZ YAZAMAZ
    // ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Tipler))]
    public async Task Yetkisiz_YorumYazamaz(string tip, string _, string yetkisizRol)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var kayitId = await KayitAsync(db, tip, suffix);

        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "yazamaz", [yetkisizRol]);

        var yanit = await client.PostAsJsonAsync(
            "/api/collaboration/comments",
            new { entityType = tip, entityId = kayitId, body = "girmemeli" });

        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);

        // Gerçekten yazılmadı — durum kodu yeterli değil.
        Assert.False(
            db.TaskComments.Any(x => x.EntityId == kayitId),
            $"{tip}: yetkisiz kullanıcının yorumu KAYDEDİLMİŞ.");
    }

    // ---------------------------------------------------------------
    // 4) YETKİSİZ EK DOSYAYI DOĞRUDAN URL İLE İNDİREMEZ
    // ---------------------------------------------------------------

    /// <summary>
    /// EKRAN HİÇ AÇILMADAN, DOĞRUDAN İNDİRME ADRESİNE GİDİLİYOR.
    ///
    /// G3/1b'de sızıntı tam olarak burada ekrandan dosyaya taşınmıştı:
    /// liste ucu kapatılmış, dışa aktarım ucu açık kalmıştı. Bu test
    /// listeye hiç bakmıyor.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tipler))]
    public async Task Yetkisiz_EkDosyayiDogrudanIndiremez(string tip, string yetkiliRol, string yetkisizRol)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var kayitId = await KayitAsync(db, tip, suffix);

        // Ek dosyayı YETKİLİ kullanıcı yüklüyor — gerçek akış.
        var yetkili = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "yukleyen", [yetkiliRol]);

        using var form = new MultipartFormDataContent();
        /*
         * .pdf — .txt DEĞİL.
         *
         * `UploadService.AllowedExtensions` metin dosyasını kabul
         * etmiyor. İlk denemede `.txt` yükledim ve test yükleme
         * adımında düştü; hata kapıda değil KURULUMDAYDI.
         */
        var icerik = new ByteArrayContent("%PDF-1.4 gizli belge"u8.ToArray());
        icerik.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");

        form.Add(new StringContent(tip), "entityType");
        form.Add(new StringContent(kayitId.ToString()), "entityId");
        form.Add(icerik, "file", "gizli.pdf");

        var yukleme = await yetkili.PostAsync("/api/collaboration/attachments", form);
        yukleme.EnsureSuccessStatusCode();

        var ek = await yukleme.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var ekId = ek.GetProperty("id").GetString();

        // YETKİSİZ kullanıcı doğrudan indirme adresine gidiyor.
        var yetkisiz = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "indiremez", [yetkisizRol]);

        var indirme = await yetkisiz.GetAsync(
            $"/api/collaboration/attachments/{ekId}/download");

        Assert.Equal(HttpStatusCode.NotFound, indirme.StatusCode);
    }

    // ---------------------------------------------------------------
    // 5) EŞLEME TABLOSU KAPALI TARAFA DÜŞER
    // ---------------------------------------------------------------

    /// <summary>
    /// Çözümleyicinin desteklediği HER tipin izin karşılığı olmalı.
    /// Karşılığı olmayan tip reddedilir (kapalı tarafa düşer), yani
    /// özellik sessizce açık kalmaz — ama sessizce ÇALIŞMAMASI da
    /// istenmiyor. Bu bekçi, tip eklerken tabloyu unutmayı yakalar.
    /// </summary>
    [Fact]
    public void DesteklenenHerTipin_IzinKarsiligiOlmali()
    {
        var eksik = EntityContextResolver.SupportedTypes
            .Where(tip => CollaborationPermissions.GerekenIzin(tip) is null)
            .ToList();

        Assert.True(
            eksik.Count == 0,
            "Bu tipler çözümleyicide destekleniyor ama izin tablosunda yok:\n  " +
            string.Join("\n  ", eksik) +
            "\n\nCollaborationPermissions'a ekleyin. Eklenmezse tip kapalı " +
            "tarafa düşer ve yorum o kayıtta HİÇ çalışmaz.");
    }

    /// <summary>
    /// Tanımsız tip REDDEDİLİR — tablo "izin ver"e düşmez.
    /// </summary>
    [Fact]
    public void TanimsizTip_Reddedilir()
    {
        Assert.Null(CollaborationPermissions.GerekenIzin("HicVarOlmayanTip"));
        Assert.Null(CollaborationPermissions.GerekenIzin(""));
        Assert.Null(CollaborationPermissions.GerekenIzin(null));
    }

    /// <summary>
    /// KAPALI TARAF — TÜM İZİNLERE SAHİP KULLANICIYLA SINANIYOR.
    ///
    /// `izinVarMi` olarak `_ => true` geçiliyor: yani "her izne
    /// sahip" bir kullanıcı taklit ediliyor. Reddin sebebi yetersiz
    /// izin OLAMAZ; tek sebep tipin tabloda olmamasıdır.
    ///
    /// BU TEST BİR AÇIĞI KAPATIYOR. Önce kapalı-taraf varsayılanı
    /// yalnız uçtan sınanıyordu ve sabotaj ("varsayılanı serbest
    /// yap") testleri KIRMADI — çünkü bilinmeyen tipi
    /// `EntityContextResolver` de reddediyor ve uç yine 404
    /// dönüyordu. İki bariyer aynı sonucu verince, hangisinin
    /// çalıştığı ölçülemiyordu. Asıl tehlike ise
    /// `SupportedTypes`'a tip eklenip bu tablonun unutulmasıydı;
    /// o durumda çözümleyici tipi TANIR ve serbest varsayılan kapıyı
    /// ardına kadar açardı.
    /// </summary>
    [Theory]
    [InlineData("HicVarOlmayanTip")]
    [InlineData("Invoice")]
    [InlineData("")]
    [InlineData(null)]
    public void TabloDaOlmayanTip_TumIzinlereSahipKullaniciyaBileKapali(string? tip)
    {
        Assert.False(
            CollaborationPermissions.ErisebilirMi(tip, _ => true),
            $"\"{tip ?? "(null)"}\" tabloda yok ama erişim VERİLDİ. " +
            "Kapı kapalı tarafa düşmeli: tabloya eklenmemiş bir tip, " +
            "`SupportedTypes`'a eklenmiş olsa bile açılmamalı.");
    }

    /// <summary>
    /// Ters yön: tabloda OLAN tip, izin varsa açılıyor. Bu olmasaydı
    /// yukarıdaki test, fonksiyon her zaman `false` dönse bile yeşil
    /// kalırdı.
    /// </summary>
    [Fact]
    public void TablodakiTip_IzinVarsaAcilir()
    {
        foreach (var tip in CollaborationPermissions.Tumu.Keys)
        {
            Assert.True(
                CollaborationPermissions.ErisebilirMi(tip, _ => true),
                $"{tip} tabloda var ve kullanıcının izni var ama kapalı.");

            Assert.False(
                CollaborationPermissions.ErisebilirMi(tip, _ => false),
                $"{tip} için izin YOKKEN erişim verildi.");
        }
    }

    // ---------------------------------------------------------------
    // 6) BİLİNMEYEN TİP UÇTA DA REDDEDİLİR
    // ---------------------------------------------------------------

    /// <summary>
    /// TABLO TESTİ YETMEZ — UÇ SINANIYOR.
    ///
    /// `TanimsizTip_Reddedilir` yalnız eşleme fonksiyonunu sınıyor;
    /// o fonksiyonun DOĞRU KULLANILDIĞINI kanıtlamıyor. Kapıda
    /// `gerekenIzin is null` dalı yanlışlıkla "serbest bırak"a
    /// çevrilse tablo testi YEŞİL kalır, uç ise açılırdı.
    ///
    /// Burada gerçek bir HTTP isteği atılıyor ve çağıran kullanıcı
    /// TAM YETKİLİ (Admin): reddin sebebi yetersiz izin değil,
    /// TİPİN TANINMAMASI olsun diye.
    /// </summary>
    [Theory]
    [InlineData("HicVarOlmayanTip")]
    [InlineData("Invoice")]        // gerçek bir kavram ama tabloda yok
    [InlineData("worktask ")]      // sondaki boşluk: Trim çalışıyor mu
    [InlineData("'; DROP TABLE")]  // çöp girdi
    public async Task BilinmeyenTip_UctaReddedilir(string tip)
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.GetAsync(
            $"/api/collaboration/comments?entityType={Uri.EscapeDataString(tip)}" +
            $"&entityId={Guid.NewGuid()}");

        // "worktask " Trim sonrası TANINIR; kayıt yok, yine 404.
        // Diğerleri tabloda yok, yine 404. İkisi de REDDEDİLMİŞ demek.
        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);
    }

    /// <summary>
    /// BİLİNMEYEN TİPE YAZILAMAZ DA.
    ///
    /// Okuma reddedilirken yazmanın açık kalması, tabloyu yalnız
    /// listeleme yolunda kontrol etmenin klasik sonucudur.
    /// </summary>
    [Fact]
    public async Task BilinmeyenTip_UctaYazilamaz()
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var kayitId = Guid.NewGuid();

        var yanit = await client.PostAsJsonAsync(
            "/api/collaboration/comments",
            new { entityType = "HicVarOlmayanTip", entityId = kayitId, body = "girmemeli" });

        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(
            db.TaskComments.Any(x => x.EntityId == kayitId),
            "Bilinmeyen tipe yazılan yorum KAYDEDİLMİŞ.");
    }
}
