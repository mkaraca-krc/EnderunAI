using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// KAPSAM DİKİŞİ BEKÇİSİ — "unutulan uç = sızıntı" sorununu derleme
/// zamanında yakalar.
///
/// Veri kapsamı bir GÜVENLİK SINIRI. Kuralı tek yerde tanımlamak
/// (CurrentDataScopeSnapshot.Apply) yetmiyor: kontrolcünün onu ÇAĞIRMAYI
/// hatırlamasına bağlı kalırsa, yeni eklenen bir uç sessizce kapsamsız
/// olur ve kimse fark etmez.
///
/// EF global sorgu süzgeci (HasQueryFilter) çalışma zamanında garanti
/// verirdi ama bu kod tabanında yanlış araç — gerekçeler
/// Security/ScopedData.cs docstring'inde, en somutu şu: kimlik numarası
/// tekillik kontrolü kapsamı BİLEREK atlıyor, global süzgeç altında
/// mükerrer TC sessizce oluşurdu.
///
/// Bu test onun yerine şunu garanti ediyor: kapsamı uygulanmış varlıklara
/// kontrolcülerden ham erişim, ancak GEREKÇESİ YAZILI bir istisna
/// listesindeyse mümkün. Liste görünür ve sayılabilir; her R3a yığını
/// onu kısaltır.
/// </summary>
public sealed class DataScopeSeamTests
{
    private static string ControllersPath()
    {
        var dir = AppContext.BaseDirectory;

        while (dir is not null &&
               !Directory.Exists(Path.Combine(dir, "EnderunAI.Api", "Controllers")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!, "EnderunAI.Api", "Controllers");
    }

    /// <summary>
    /// Ham <c>db.Personnel</c> erişimine izin verilen kontrolcüler ve
    /// GEREKÇELERİ.
    ///
    /// Bu liste BORÇ. Kısalması beklenir; uzaması bir kararın kaydı
    /// olmalı, kazara olmamalı.
    /// </summary>
    private static readonly Dictionary<string, string> RawAccessAllowed = new()
    {
        ["PersonnelController.cs"] =
            "Yazma yolları ve TEKİLLİK kontrolleri. Kimlik numarası ve " +
            "sicil tekilliği TÜM personeli taramak zorunda: kapsamla " +
            "sınırlanırsa aynı TC iki şirkette iki kez açılır. Okuma " +
            "uçları (liste, detay, veri-eksikleri) dikişe taşındı.",

        ["UserManagementController.cs"] =
            "Kullanıcı-personel eşleştirmesi; yönetim ekranı zaten " +
            "user-management izniyle korunuyor ve kapsam atamasını " +
            "BURADA kuruyor — kendi kurduğu kapsamla sınırlanamaz.",

        ["RehireCheckController.cs"] =
            "Yeniden işe alım kontrolü: kişinin GEÇMİŞTE herhangi bir " +
            "şirkette çıkışı olup olmadığına bakıyor. Kapsam uygulanırsa " +
            "kara liste delinir.",

        // --- Aşağıdakiler HENÜZ TAŞINMADI (R3a sonraki yığınlar) ---
        ["PayrollReadinessController.cs"] = "R3a yığın 2 — bordro ailesi.",
        ["HrCareerController.cs"] =
            "OKUMA UÇLARI DİKİŞTE (R3a yığın 2). Kalan ham erişim yazma "
            + "yolunda: kariyer hareketi AÇMAK `personnel.create` ister "
            + "ve o izin şantiye kapsamlı rollerde yok.",
        ["HrAssetsController.cs"] =
            "Personel analizi dikişte (R3a yığın 2). Kalan ham erişim "
            + "zimmet DEVRİNDE hedef personelin varlığını doğruluyor; "
            + "o uç `personnel.edit` istiyor.",
        ["HrProjectLaborCostsController.cs"] =
            "TAMAMEN DİKİŞTE (R3a yığın 2) — liste adları ve maliyet "
            + "yazma yolu. Liste bu dosyada kalıyor çünkü bekçi test "
            + "yorumları soyduktan sonra eşleşme kalmıyor; kayıt "
            + "gelecekte ham erişim eklenirse fark edilsin diye duruyor.",
        ["PersonnelOvertimeController.cs"] =
            "DİKİŞE TAŞINDI (R3a yığın 2). Kayıt, ham erişim geri "
            + "eklenirse fark edilsin diye duruyor.",
        ["PersonnelExtraPaymentsController.cs"] = "R3a yığın 2 — elden ödeme.",
        ["PersonnelDutiesController.cs"] =
            "Liste ve görev açma dikişte (R3a yığın 2). Kalan ham "
            + "erişim `db.PersonnelDuties` üzerinde — o varlık kapsam "
            + "taşımıyor, personel üzerinden süzülüyor.",
        ["PersonnelDocumentsController.cs"] = "R3a yığın 2 — personel evrakı.",
        ["PersonnelTerminationsController.cs"] = "R3a yığın 2 — çıkış.",
        ["SubcontractorContractsController.cs"] = "R3a yığın 3 — taşeron.",
        ["ProjectSchedulesController.cs"] =
            "Zaten CurrentDataScope uyguluyor (kapsam uygulayan 10 " +
            "kontrolcüden biri); dikişe taşınması ayrı iş.",
        ["ProjectSitesController.cs"] =
            "Zaten CurrentDataScope uyguluyor.",
        ["ToolAssetsController.cs"] =
            "Zaten CurrentDataScope uyguluyor.",
        ["IsgDashboardController.cs"] = "R3a yığın 3 — İSG.",
        ["AttendanceSheetController.cs"] = "R3a yığın 2 — puantaj cetveli.",
        ["LeaveBalanceController.cs"] = "R3a yığın 2 — izin bakiyesi.",
        ["PersonnelCashPaymentsController.cs"] =
            "R3a yığın 2 — elden ödeme. DİKKAT: bu uç zaten " +
            "extra_payment izniyle korunuyor ve tutar maskeleme " +
            "projeksiyon katmanında; kapsam ayrıca uygulanmalı.",
        ["HrRecruitmentController.cs"] =
            "KARAR VERİLDİ: işe alım MERKEZİ bir İK işlevi. Üç katman " +
            "kuruldu — (1) okuma uçları personnel.view'dan " +
            "personnel.manage'e çekildi (Şantiye Şefi/Formen/İSG " +
            "erişimi kaybetti, etki ölçüldü), (2) aday listesi dikişte " +
            "ŞİRKET kapsamıyla süzülüyor (JobCandidate yalnız CompanyId " +
            "taşır; ilan projeye bağlanabilir, aday havuzu ortak), " +
            "(3) TC kimlik numarası maskeli (personnel.create ister, " +
            "fail-closed). Kalan ham db erişimi: ilan/başvuru/mülakat " +
            "uçları — R3a yığın 2'de dikişe alınacak.",
    };

    [Fact]
    public void KontrolculerdeHamPersonelErisimi_YalnizcaGerekcesiYaziliOlanlarda()
    {
        var path = ControllersPath();
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);

            // Yorumları soy: gerekçe metinleri "db.Personnel" yazabilir.
            var code = Regex.Replace(text, @"/\*[\s\S]*?\*/", " ");
            code = Regex.Replace(code, @"//[^\n]*", " ");

            if (!Regex.IsMatch(code, @"\bdb\.Personnel\b"))
                continue;

            var name = Path.GetFileName(file);
            if (!RawAccessAllowed.ContainsKey(name))
                offenders.Add(name);
        }

        Assert.True(
            offenders.Count == 0,
            "Bu kontrolcüler kapsam taşıyan Personnel varlığına HAM erişiyor " +
            "ve istisna listesinde yok: " + string.Join(", ", offenders) +
            ". Okuma ise IScopedData.PersonnelAsync kullanın; kapsamı " +
            "bilerek atlaması gerekiyorsa DataScopeSeamTests içindeki " +
            "listeye GEREKÇESİYLE ekleyin.");
    }

    /// <summary>
    /// PersonnelController'ın OKUMA uçları dikişe bağlı kalmalı.
    ///
    /// Dosya istisna listesinde (yazma ve tekillik kontrolleri için), o
    /// yüzden yukarıdaki test onu korumuyor. Bu test okuma uçlarının
    /// geri kaymasını yakalar.
    /// </summary>
    [Fact]
    public void PersonnelControllerOkumaUclari_DikisiKullaniyor()
    {
        var file = Path.Combine(ControllersPath(), "PersonnelController.cs");
        var text = File.ReadAllText(file);

        Assert.Contains("IScopedData", text);
        Assert.Contains("scoped.PersonnelAsync(", text);

        // Kapsam kuralını kontrolcüde tekrar kurmuyor.
        Assert.DoesNotContain("ICurrentDataScopeService", text);
    }

    /// <summary>
    /// DİKİŞ FAIL-CLOSED OLMALI.
    ///
    /// Kapsam çözülemezse (kullanıcı yok / yetkilendirme pasif) dikiş
    /// BOŞ döner. "Kısıtlama yok" diye geçmek, kapsamın çalışmadığı anda
    /// tüm veriyi açmak olurdu.
    /// </summary>
    [Fact]
    public void Dikis_KapsamCozulemezseBosDoner()
    {
        var dir = AppContext.BaseDirectory;

        while (dir is not null &&
               !File.Exists(Path.Combine(dir, "EnderunAI.Api", "Security", "ScopedData.cs")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.NotNull(dir);

        var text = File.ReadAllText(
            Path.Combine(dir!, "EnderunAI.Api", "Security", "ScopedData.cs"));

        Assert.Matches(@"scope is null[\s\S]{0,120}Where\(_ => false\)", text);
    }
}
