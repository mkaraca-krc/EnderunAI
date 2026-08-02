namespace EnderunAI.Api.Models;

public enum RoleDataScopePolicy
{
    All = 0,
    SiteOnly = 1
}

public sealed class AppRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Bu role sahip kullanıcılar için Kullanıcı Yönetimi ekranında
    /// varsayılan/beklenen veri kapsamı davranışı (Yetki Matrisi'nde
    /// "Veri Kapsamı" sütunu). Gerçek erişim kısıtlaması her zaman
    /// kullanıcının kendi UserDataScope kayıtlarından gelir — bu alan
    /// sadece admin ekranına yol gösterir (ör. SiteOnly ise kullanıcı
    /// oluştururken şantiye seçimi zorunlu kılınır).
    /// </summary>
    public RoleDataScopePolicy DataScopePolicy { get; set; } = RoleDataScopePolicy.All;

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
