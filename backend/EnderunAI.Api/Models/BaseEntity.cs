namespace EnderunAI.Api.Models;

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }
}
