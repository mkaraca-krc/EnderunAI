using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

public sealed class ProcurementApprovalDbContext(DbContextOptions<ProcurementApprovalDbContext> options) : DbContext(options)
{
    public DbSet<ProcurementApprovalRule> Rules => Set<ProcurementApprovalRule>();
    public DbSet<ProcurementApprovalRuleStep> RuleSteps => Set<ProcurementApprovalRuleStep>();
    public DbSet<ProcurementApprovalInstance> Instances => Set<ProcurementApprovalInstance>();
    public DbSet<ProcurementApprovalInstanceStep> InstanceSteps => Set<ProcurementApprovalInstanceStep>();
    public DbSet<ProcurementApprovalHistory> History => Set<ProcurementApprovalHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcurementApprovalRule>(entity =>
        {
            entity.ToTable("procurement_approval_rules");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.DocumentType, x.Priority });
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.MinimumAmount).HasPrecision(18, 2);
            entity.Property(x => x.MaximumAmount).HasPrecision(18, 2);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProcurementApprovalRuleStep>(entity =>
        {
            entity.ToTable("procurement_approval_rule_steps");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.RuleId, x.SequenceNo }).IsUnique();
            entity.Property(x => x.RoleName).HasMaxLength(100).IsRequired();
            entity.HasOne(x => x.Rule).WithMany(x => x.Steps).HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProcurementApprovalInstance>(entity =>
        {
            entity.ToTable("procurement_approval_instances");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.DocumentType, x.DocumentId, x.Status });
            entity.Property(x => x.DocumentNumber).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProcurementApprovalInstanceStep>(entity =>
        {
            entity.ToTable("procurement_approval_instance_steps");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.InstanceId, x.SequenceNo });
            entity.Property(x => x.RoleName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ActionByName).HasMaxLength(200);
            entity.Property(x => x.Comment).HasMaxLength(1000);
            entity.HasOne(x => x.Instance).WithMany(x => x.Steps).HasForeignKey(x => x.InstanceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProcurementApprovalHistory>(entity =>
        {
            entity.ToTable("procurement_approval_history");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.InstanceId, x.ActionAtUtc });
            entity.Property(x => x.ActionByName).HasMaxLength(200);
            entity.Property(x => x.RoleName).HasMaxLength(100);
            entity.Property(x => x.IpAddress).HasMaxLength(100);
            entity.Property(x => x.Comment).HasMaxLength(1000);
            entity.HasOne(x => x.Instance).WithMany(x => x.History).HasForeignKey(x => x.InstanceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }
}
