using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.Accounting;

/// <summary>
/// Çek düzenlemesinde bir alanın hangi sınıfa girdiği (ÇEK/2 · K4).
/// </summary>
public enum ChequeAlanSinifi
{
    /// <summary>
    /// MALİ VE KİMLİK ALANI. Kapanmış ya da işlem görmüş çekte
    /// değiştirilemez; değişmesi ya defteri ya da çekin kim olduğunu
    /// değiştirir.
    /// </summary>
    Kilitli = 0,

    /// <summary>
    /// TANIMLAYICI ALAN. Her durumda düzeltilebilir; muhasebeye,
    /// bakiyeye ve çekin kimliğine dokunmaz. Yazım hatası bu sınıftadır.
    /// </summary>
    Tanimlayici = 1,

    /// <summary>
    /// TAŞIYICI ALAN — verinin kendisi değil, isteğin zarfı
    /// (eşzamanlılık damgası, düzeltme gerekçesi).
    ///
    /// BU SINIF BİR SAKLANMA YERİ DEĞİLDİR: testi (`KontrolSinifi
    /// SabitKalir`) bu kümenin TAM olarak neyden oluştuğunu
    /// sabitliyor, yani yeni bir veri alanı buraya sessizce
    /// atılamaz — atılırsa test kırmızı verir.
    /// </summary>
    Kontrol = 2
}

/// <summary>
/// Bir alanın sınıfı ve denetim kaydında görünen etiketi — TEK KAYIT.
/// İkisi ayrı sözlüklerde tutulsaydı biri güncellenip diğeri
/// unutulabilirdi.
/// </summary>
public sealed record ChequeAlanTanimi(ChequeAlanSinifi Sinif, string Etiket);

/// <summary>
/// ÇEK ALAN SINIFI — TEK TANIM (ÇEK/2 · K4).
///
/// Kapanmış çekte kaydın TAMAMI kilitliydi ve tek çare "iptal edip
/// yeniden girin"di. Bir yazım hatasını düzeltmek için mali kaydı
/// iptal edip yeniden üretmek, hatanın kendisinden zararlıdır.
///
/// SINIF İKİ AYRI LİSTEDE TUTULMUYOR. Kilit kararı da, denetim kaydı
/// da, ekranın "bu alan açık mı" bilgisi de bu tek sözlükten çıkıyor.
/// İki liste olsaydı biri güncellenip diğeri unutulurdu — ÇEK/1'de
/// tam olarak bu yaşandı (liste ile toplam ayrı süzgeçlerden
/// besleniyordu, Kural 25).
///
/// YENİ ALAN SINIFSIZ KALAMAZ: <c>AlanSinifiTestleri</c> içindeki
/// yansıma testi <see cref="UpdateChequeRequest"/> üzerindeki HER
/// özelliğin burada karşılığı olmasını şart koşuyor. Sözlüğe
/// eklemeyi unutan kırmızı görür.
/// </summary>
public static class ChequeAlanSiniflari
{
    /// <summary>
    /// TAŞIYICI ALANLAR — sabit küme. Testte birebir doğrulanıyor.
    /// </summary>
    public static readonly string[] KontrolAlanlari =
    [
        nameof(UpdateChequeRequest.RowVersion),
        nameof(UpdateChequeRequest.EditReason)
    ];

    private static readonly Dictionary<string, ChequeAlanTanimi> Sozluk = new(StringComparer.Ordinal)
    {
        // ── KİLİTLİ: mali ve kimlik alanları (K1) ─────────────────
        // Çekin kim olduğu ve deftere ne yazdığı. Kapanmış çekte
        // değişmeleri gerçekleşmiş bir fişi sessizce tutarsız bırakır.
        [nameof(UpdateChequeRequest.ChequeNumber)] = new(ChequeAlanSinifi.Kilitli, "Çek numarası"),
        [nameof(UpdateChequeRequest.BankName)] = new(ChequeAlanSinifi.Kilitli, "Banka"),
        [nameof(UpdateChequeRequest.CurrentAccountId)] = new(ChequeAlanSinifi.Kilitli, "Cari"),
        [nameof(UpdateChequeRequest.ProjectId)] = new(ChequeAlanSinifi.Kilitli, "Proje"),
        [nameof(UpdateChequeRequest.CostCenterCode)] = new(ChequeAlanSinifi.Kilitli, "Masraf merkezi"),
        [nameof(UpdateChequeRequest.Amount)] = new(ChequeAlanSinifi.Kilitli, "Tutar"),
        [nameof(UpdateChequeRequest.IssueDate)] = new(ChequeAlanSinifi.Kilitli, "Keşide tarihi"),
        [nameof(UpdateChequeRequest.DueDate)] = new(ChequeAlanSinifi.Kilitli, "Vade"),
        [nameof(UpdateChequeRequest.ProgressPaymentId)] = new(ChequeAlanSinifi.Kilitli, "Hakediş"),
        [nameof(UpdateChequeRequest.SupplierInvoiceId)] = new(ChequeAlanSinifi.Kilitli, "Tedarikçi faturası"),
        [nameof(UpdateChequeRequest.CurrencyCode)] = new(ChequeAlanSinifi.Kilitli, "Para birimi"),
        [nameof(UpdateChequeRequest.ExchangeRate)] = new(ChequeAlanSinifi.Kilitli, "Kur"),

        // ── TANIMLAYICI: her durumda düzeltilebilir (K2) ──────────
        //
        // BANKA ADI NEDEN BURADA DEĞİL: şube ve keşideci çekin
        // üzerindeki tanımlayıcı yazılardır, banka ise çekin hangi
        // yaprak olduğunu söyler ve ödeme hesabıyla eşleştirilen
        // alandır (canlıdaki 805088 uyuşmazlığı tam olarak bu
        // eşleşmeydi). Kimlik sayıldı — Mehmet'in K2 listesinde
        // "keşideci, şube, açıklama/not" var, banka yok.
        [nameof(UpdateChequeRequest.Drawer)] = new(ChequeAlanSinifi.Tanimlayici, "Keşideci"),
        [nameof(UpdateChequeRequest.BankBranch)] = new(ChequeAlanSinifi.Tanimlayici, "Şube"),
        [nameof(UpdateChequeRequest.Description)] = new(ChequeAlanSinifi.Tanimlayici, "Açıklama"),

        // ── TAŞIYICI ──────────────────────────────────────────────
        [nameof(UpdateChequeRequest.RowVersion)] = new(ChequeAlanSinifi.Kontrol, "Sürüm damgası"),
        [nameof(UpdateChequeRequest.EditReason)] = new(ChequeAlanSinifi.Kontrol, "Düzeltme gerekçesi")
    };

    /// <summary>Sözlüğün okunur kopyası — testler ve raporlar için.</summary>
    public static IReadOnlyDictionary<string, ChequeAlanTanimi> Tumu => Sozluk;

    /// <summary>
    /// Alanın tanımı. SINIFSIZ ALAN SESSİZ GEÇMEZ: bilinmeyen ad
    /// istisna atar, çünkü "bilmiyorum" hâlinde varsayılan seçmek
    /// (hangisi olursa olsun) yanlış tarafa düşmektir.
    /// </summary>
    public static ChequeAlanTanimi Tanim(string alan) =>
        Sozluk.TryGetValue(alan, out var tanim)
            ? tanim
            : throw new InvalidOperationException(
                $"'{alan}' alanının düzenleme sınıfı tanımlı değil. " +
                "ChequeAlanSiniflari sözlüğüne ekleyin.");

    /// <summary>Alanın sınıfı — bilinmeyen ad istisna atar.</summary>
    public static ChequeAlanSinifi Sinif(string alan) => Tanim(alan).Sinif;

    /// <summary>
    /// DENETİM KAYDINDA GÖRÜNEN ETİKET — sınıfla AYNI yerden.
    ///
    /// Etiketler eskiden `UpdateAsync` içindeki `Track` çağrılarına
    /// tek tek yazılıydı. Alan sınıfı ayrı bir yerde tutulsaydı, yeni
    /// alan eklerken birine yazıp diğerine yazmamak mümkün olurdu —
    /// K4'ün kapattığı şey tam olarak bu.
    /// </summary>
    public static string Etiket(string alan) => Tanim(alan).Etiket;

    /// <summary>
    /// İSTEKTE DEĞİŞTİRİLMEK İSTENEN KİLİTLİ ALANLARIN ADLARI.
    ///
    /// SAF: veritabanına bakmaz, hiçbir şey yazmaz. Kapanmış çekte
    /// düzenlemeye izin verilip verilmeyeceği bu listenin boş olup
    /// olmamasına bakar.
    ///
    /// HER KİLİTLİ ALAN BURADA KARŞILAŞTIRILMAK ZORUNDA. Sözlüğe
    /// eklenip buraya eklenmeyen bir alan, kilitli görünüp fiilen
    /// serbest kalırdı — sınıflandırmanın en tehlikeli hâli. Bunu
    /// yansıma testi kapatıyor: her kilitli alan tek tek değiştirilip
    /// bu metodun onu bildirdiği doğrulanıyor.
    /// </summary>
    public static IReadOnlyList<string> DegisenKilitliAlanlar(
        Cheque mevcut, UpdateChequeRequest istek)
    {
        var degisenler = new List<string>();

        void Karsilastir(string alan, string? once, string? sonra)
        {
            if (Sinif(alan) != ChequeAlanSinifi.Kilitli) return;
            if (!string.Equals(once, sonra, StringComparison.Ordinal))
                degisenler.Add(alan);
        }

        /*
         * ÇEK NUMARASI HAM HÂLİYLE KARŞILAŞTIRILIYOR — NORMALİZE
         * DEĞİL. Bu bilinçli ve bir sonda sonucudur.
         *
         * İlk yazımda normalize karşılaştırmıştım ("12 345" = "12345",
         * sırf boşluk farkı düzenlemeyi reddetmesin diye) ve ortaya
         * çıkan boşluğu `UpdateAsync` içinde İKİNCİ bir bariyerle
         * kapatmıştım: denetim kaydına kilitli alan düştüyse patlat.
         *
         * SONDA BUNU YAKALADI. Birinci kapıyı devre dışı bıraktığımda
         * yeni testlerin hiçbiri kırmızıya dönmedi — ikinci bariyer
         * aynı isteği yine reddediyordu. Yani kilit iki yerde
         * kuruluydu ve hangisinin koruduğu ölçülemiyordu (Kural 25:
         * aynı gözlemi üreten iki örtüşen bariyer).
         *
         * ÇÖZÜM SAPMAYI YAKALAMAK DEĞİL, İMKÂNSIZ KILMAK: ham
         * karşılaştırma `UpdateAsync`'in atamasıyla birebir aynı
         * değere bakar, dolayısıyla ikisi ayrışamaz ve ikinci
         * bariyere gerek kalmaz.
         *
         * BEDELİ: kapanmış çekte yalnız boşluk farkı taşıyan bir çek
         * numarası da reddedilir. Doğru olan da bu — kilitli bir alan
         * "aslında aynı" gerekçesiyle yazılamaz.
         */
        Karsilastir(nameof(istek.ChequeNumber),
            mevcut.ChequeNumber.Trim(),
            (istek.ChequeNumber ?? string.Empty).Trim());

        // Boş gönderilen banka adı "değiştirme" demektir — mevcut
        // davranış (UpdateAsync boşsa eskisini koruyor) burada da
        // aynen geçerli, yoksa boş bırakan ekran kilide takılırdı.
        Karsilastir(nameof(istek.BankName),
            mevcut.BankName,
            string.IsNullOrWhiteSpace(istek.BankName) ? mevcut.BankName : istek.BankName.Trim());

        Karsilastir(nameof(istek.CurrentAccountId),
            mevcut.CurrentAccountId?.ToString(), istek.CurrentAccountId?.ToString());

        Karsilastir(nameof(istek.ProjectId),
            mevcut.ProjectId?.ToString(), istek.ProjectId?.ToString());

        Karsilastir(nameof(istek.CostCenterCode),
            Bosluksuz(mevcut.CostCenterCode), Bosluksuz(istek.CostCenterCode));

        Karsilastir(nameof(istek.Amount),
            decimal.Round(mevcut.Amount, 2).ToString("0.00"),
            decimal.Round(istek.Amount, 2).ToString("0.00"));

        Karsilastir(nameof(istek.IssueDate),
            mevcut.IssueDate.ToString("yyyy-MM-dd"),
            istek.IssueDate.ToString("yyyy-MM-dd"));

        Karsilastir(nameof(istek.DueDate),
            mevcut.DueDate.ToString("yyyy-MM-dd"),
            istek.DueDate.ToString("yyyy-MM-dd"));

        Karsilastir(nameof(istek.ProgressPaymentId),
            mevcut.ProgressPaymentId?.ToString(), istek.ProgressPaymentId?.ToString());

        Karsilastir(nameof(istek.SupplierInvoiceId),
            mevcut.SupplierInvoiceId?.ToString(), istek.SupplierInvoiceId?.ToString());

        // Para birimi ve kur da boş gönderilince mevcut değeri korur
        // (UpdateAsync'teki davranış).
        Karsilastir(nameof(istek.CurrencyCode),
            mevcut.CurrencyCode.ToUpperInvariant(),
            string.IsNullOrWhiteSpace(istek.CurrencyCode)
                ? mevcut.CurrencyCode.ToUpperInvariant()
                : istek.CurrencyCode.Trim().ToUpperInvariant());

        Karsilastir(nameof(istek.ExchangeRate),
            decimal.Round(mevcut.ExchangeRate, 6).ToString("0.000000"),
            decimal.Round(istek.ExchangeRate ?? mevcut.ExchangeRate, 6).ToString("0.000000"));

        return degisenler;
    }

    private static string? Bosluksuz(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
