namespace EnderunAI.Api.Models;

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Kullanıcının kendi seçtiği hitap: "Bey" veya "Hanım". Sistemde
    /// cinsiyet tutulmuyor ve isimden tahmin edilmiyor; boşsa nötr
    /// "Sayın" biçimi kullanılır.
    /// </summary>
    public string? Honorific { get; set; }
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;

    /// <summary>
    /// Parolanın son değiştirilme zamanı (UTC). Boş = hiç
    /// değiştirilmemiş (kurulumdan beri aynı).
    ///
    /// NEDEN VAR: parola değişince o kullanıcının DİĞER OTURUMLARI
    /// düşer. Jetonlar durumsuz olduğu için "bu jeton değişimden önce
    /// mi üretildi" sorusunun cevabı bir yerde durmak zorunda.
    ///
    /// Kimlik doğrulamada bu alan VERİTABANINDAN OKUNMUYOR; açılışta
    /// belleğe alınıp orada güncelleniyor (bkz. OturumGecerliligi).
    /// Sütun, sürecin yeniden başlamasından sonra da doğru cevabı
    /// verebilmek için var.
    /// </summary>
    public DateTime? PasswordChangedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>
    /// Kullanıcı bazlı kalıcı istisna: true ise rol bazlı mesai penceresi
    /// bu kullanıcı için hiç uygulanmaz (Admin/Genel Müdür zaten kod
    /// içinde her zaman istisnadır, bu alan DİĞER roller için).
    /// </summary>
    public bool WorkHoursExempt { get; set; } = false;

    /// <summary>
    /// Bu kullanıcının kendi personel kaydı. Self-servis ekranlarının
    /// dayanağı: "benim İSG belgelerim" gibi uçlar kimin verisini
    /// döndüreceğini buradan bilir.
    ///
    /// Boş olabilir — her kullanıcı personel değildir (dış danışman,
    /// sistem hesabı). Boşsa self-servis ekranları veri döndürmez;
    /// asla "en yakın personeli" tahmin etmez.
    /// </summary>
    public Guid? PersonnelId { get; set; }
    public Personnel? Personnel { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
