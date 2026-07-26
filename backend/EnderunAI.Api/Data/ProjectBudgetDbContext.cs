using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

public sealed class ProjectBudgetDbContext(DbContextOptions<ProjectBudgetDbContext> options)
    : DbContext(options)
{
    public DbSet<ProjectBudget> Budgets => Set<ProjectBudget>();
    public DbSet<ProjectBudgetItem> BudgetItems => Set<ProjectBudgetItem>();
    public DbSet<ProjectBudgetRevision> Revisions => Set<ProjectBudgetRevision>();
    public DbSet<ProjectBudgetConsumption> Consumptions => Set<ProjectBudgetConsumption>();
    public DbSet<ProjectBudgetAlert> Alerts => Set<ProjectBudgetAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProjectBudget>(entity =>
        {
            entity.ToTable("project_budgets");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.ProjectId, x.BudgetNumber }).IsUnique();
            entity.Property(x => x.BudgetNumber).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.BaseAmount).HasPrecision(18, 2);
            entity.Property(x => x.WarningThresholdPercent).HasPrecision(5, 2);
            entity.Property(x => x.CriticalThresholdPercent).HasPrecision(5, 2);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProjectBudgetItem>(entity =>
        {
            entity.ToTable("project_budget_items");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ProjectBudgetId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(120);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.PlannedAmount).HasPrecision(18, 2);
            entity.HasOne(x => x.ProjectBudget)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.ProjectBudgetId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProjectBudgetRevision>(entity =>
        {
            entity.ToTable("project_budget_revisions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ProjectBudgetId, x.RevisionNumber }).IsUnique();
            entity.Property(x => x.PreviousAmount).HasPrecision(18, 2);
            entity.Property(x => x.RevisedAmount).HasPrecision(18, 2);
            entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.CreatedByName).HasMaxLength(200).IsRequired();
            entity.HasOne(x => x.ProjectBudget)
                .WithMany(x => x.Revisions)
                .HasForeignKey(x => x.ProjectBudgetId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProjectBudgetConsumption>(entity =>
        {
            entity.ToTable("project_budget_consumptions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ProjectId, x.Type, x.ConsumptionDateUtc });
            entity.HasIndex(x => new { x.ReferenceType, x.ReferenceId, x.Type });
            entity.Property(x => x.ReferenceType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ReferenceNumber).HasMaxLength(100);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.ExchangeRate).HasPrecision(18, 6);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProjectBudgetAlert>(entity =>
        {
            entity.ToTable("project_budget_alerts");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ProjectId, x.IsResolved, x.Level });
            entity.Property(x => x.Code).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.BudgetAmount).HasPrecision(18, 2);
            entity.Property(x => x.UsedAmount).HasPrecision(18, 2);
            entity.Property(x => x.ProposedAmount).HasPrecision(18, 2);
            entity.Property(x => x.VarianceAmount).HasPrecision(18, 2);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }
}
