namespace EnderunAI.Api.Contracts.Accounting;

public sealed record AccountingAccountListItemResponse(
    Guid Id,
    Guid CompanyId,
    Guid? ParentAccountId,
    string Code,
    string Name,
    int Nature,
    int Level,
    bool IsPostingAllowed,
    bool RequiresProject,
    bool RequiresCostCenter,
    string? CurrencyCode,
    bool IsActive,
    int ChildCount);

public sealed record AccountingAccountDetailResponse(
    Guid Id,
    Guid CompanyId,
    Guid? ParentAccountId,
    string Code,
    string Name,
    string? Description,
    int Nature,
    int Level,
    bool IsPostingAllowed,
    bool RequiresProject,
    bool RequiresCostCenter,
    string? CurrencyCode,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,

    /// <summary>
    /// KAYIT SÜRÜMÜ — `xmin` sistem sütununun tel üzerindeki hâli
    /// (HP/1 · K8). İkinci bir belirteç DEĞİL, aynı belirtecin
    /// istemciye taşınmış hâli.
    ///
    /// İstemci güncelleme isteğinde bunu GERİ VERİR; sunucu onu
    /// veritabanındaki güncel sürümle karşılaştırır. Taşınmasaydı
    /// yalnız istek içi pencere korunurdu — kullanıcının 10 dakika
    /// önce açtığı formdaki kayıp güncelleme yakalanamazdı.
    /// </summary>
    DateTime Surum);

public sealed record CreateAccountingAccountRequest(
    Guid CompanyId,
    Guid? ParentAccountId,
    string Code,
    string Name,
    string? Description,
    int Nature,
    bool IsPostingAllowed,
    bool RequiresProject,
    bool RequiresCostCenter,
    string? CurrencyCode);

/// <summary>
/// HESAP GÜNCELLEME — KOD VE AKTİFLİK BURADA YOK (HP/1 · K1, K3).
///
/// KOD YOK: hesap kodu oluşturulduktan sonra HİÇBİR KOŞULDA
/// değişmez. Yanlış açılmış bir hesap pasife alınır, doğrusu yeni
/// hesap olarak açılır. Kod, fişte ve dış mutabakatta görünen
/// kimliktir; değiştiğinde veritabanı bağları kopmaz (30 referansın
/// hepsi Id üzerinden) ama İNSANIN ELİNDEKİ KAYIT kopar.
///
/// AKTİFLİK YOK: pasife alma ve geri alma kendi uçlarında
/// (`deactivate` / `activate`). Burada da bulunması İKİ KAPI
/// demekti ve biri diğerini sessizce ezerdi — güncelleme formu
/// eski `IsActive` değeriyle gönderildiğinde, az önce pasife
/// alınmış bir hesap geri açılırdı.
/// </summary>
public sealed record UpdateAccountingAccountRequest(
    Guid? ParentAccountId,
    string Name,
    /// <summary>
    /// ZORUNLU. Eksikse istek REDDEDİLİR — atlanmaz.
    ///
    /// "Yoksa kontrolü atla" davranışı, alanı göndermeyen herkese
    /// eşzamanlılık korumasını kapatma yolu açardı (Kural 39:
    /// zorunlu olmayan + doğrulanmayan + davranış üreten alan).
    ///
    /// YAYIN PENCERESİ: sunucu ve ön yüz AYNI yayında çıkıyor, ama
    /// yayın anında sayfası AÇIK olan kullanıcının tarayıcısındaki
    /// paket eskidir ve sürüm göndermez. Reddedilmesi DOĞRU (kapalı
    /// tarafa düşüyor); mesajın ne yapılacağını söylemesi şart —
    /// "sürüm zorunludur" geliştiriciye anlamlı, kullanıcıya değil.
    /// </summary>
    DateTime? Surum,
    string? Description,
    int Nature,
    bool IsPostingAllowed,
    bool RequiresProject,
    bool RequiresCostCenter,
    string? CurrencyCode);
