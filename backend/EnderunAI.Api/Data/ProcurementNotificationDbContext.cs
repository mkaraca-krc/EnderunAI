using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

public sealed class ProcurementNotificationDbContext(DbContextOptions<ProcurementNotificationDbContext> options)
    : DbContext(options)
{
    public DbSet<ProcurementNotification> Notifications => Set<ProcurementNotification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProcurementNotification>(entity =>
        {
            entity.ToTable("procurement_notifications");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.DeduplicationKey).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.UserId, x.ReadAtUtc });
            entity.HasIndex(x => new { x.CompanyId, x.RoleName, x.ReadAtUtc });
            entity.Property(x => x.RoleName).HasMaxLength(100);
            entity.Property(x => x.Title).HasMaxLength(250).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.DocumentType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.DocumentNumber).HasMaxLength(100);
            entity.Property(x => x.ActionUrl).HasMaxLength(500);
            entity.Property(x => x.DeduplicationKey).HasMaxLength(250).IsRequired();
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }
}
