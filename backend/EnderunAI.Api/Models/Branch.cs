namespace EnderunAI.Api.Models;

public sealed class Branch : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }

    public bool IsHeadOffice { get; set; }

    /// <summary>
    /// Muhasebe masraf merkezi kodu. Fiş satırlarındaki
    /// <c>CostCenterCode</c> alanına yazılır; şantiye giderlerinde proje
    /// kodu ne ise merkez giderlerinde de bu kod odur.
    ///
    /// Boşsa şube kodu kullanılır — ayrı bir kod tutulmasının nedeni,
    /// masraf merkezi kırılımının şube kodundan bağımsız
    /// değiştirilebilmesi (ör. iki şube tek masraf merkezine yazılabilir).
    /// </summary>
    public string? CostCenterCode { get; set; }

    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
}
