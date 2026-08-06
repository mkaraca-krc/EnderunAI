namespace EnderunAI.Api.Models.Market;

/// <summary>
/// Bir projenin kalan bakır maruziyeti.
///
/// Asıl kaynak elle girilen tonajdır: sahayı bilen kişi "bu projede
/// daha şu kadar kablo döşenecek" der. İcmal kalemlerine bakır içeriği
/// katsayısı girilmişse sistem tonajı metrajdan da toplayabilir; ikisi
/// de yoksa maruziyet BİLİNMİYOR sayılır ve sıfır kabul edilmez —
/// sıfır, "bakır riski yok" demektir ve bu yanlış bir güven verir.
/// </summary>
public sealed class ProjectCopperExposure : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    /// <summary>Elle girilen kalan bakır tonajı. Boşsa icmalden türetilir.</summary>
    public decimal? RemainingTons { get; set; }

    /// <summary>
    /// Karşılaştırma tabanı. Boşsa projenin sözleşme tarihi, o da yoksa
    /// arşivdeki en eski fiyat kullanılır ve hangisi kullanıldığı
    /// yanıtta yazar.
    /// </summary>
    public DateTime? BaselineDate { get; set; }

    public string? Note { get; set; }
}
