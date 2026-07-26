using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

public sealed class RfqInvitationDbContext(DbContextOptions<RfqInvitationDbContext> options) : DbContext(options)
{
    public DbSet<RfqSupplierInvitation> Invitations => Set<RfqSupplierInvitation>();
    public DbSet<RfqInvitationEvent> Events => Set<RfqInvitationEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RfqSupplierInvitation>(entity =>
        {
            entity.ToTable("rfq_supplier_invitations");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.RfqId, x.SupplierCurrentAccountId, x.Status });
            entity.Property(x => x.RecipientEmail).HasMaxLength(250).IsRequired();
            entity.Property(x => x.RecipientName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.LastError).HasMaxLength(2000);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<RfqInvitationEvent>(entity =>
        {
            entity.ToTable("rfq_invitation_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.InvitationId, x.EventDateUtc });
            entity.Property(x => x.EventType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.IpAddress).HasMaxLength(80);
            entity.Property(x => x.UserAgent).HasMaxLength(500);
            entity.Property(x => x.Detail).HasMaxLength(2000);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }
}
