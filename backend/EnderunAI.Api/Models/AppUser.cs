namespace EnderunAI.Api.Models;

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>
    /// Kullanıcı bazlı kalıcı istisna: true ise rol bazlı mesai penceresi
    /// bu kullanıcı için hiç uygulanmaz (Admin/Genel Müdür zaten kod
    /// içinde her zaman istisnadır, bu alan DİĞER roller için).
    /// </summary>
    public bool WorkHoursExempt { get; set; } = false;

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
