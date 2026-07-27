using EnderunAI.Api.Models.HumanResources;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data.HumanResources;

public sealed class HrDbContext(DbContextOptions<HrDbContext> options)
    : DbContext(options)
{
    public DbSet<HrLeaveRequest> LeaveRequests => Set<HrLeaveRequest>();
    public DbSet<HrOvertimeRequest> OvertimeRequests => Set<HrOvertimeRequest>();
    public DbSet<HrAdvanceRequest> AdvanceRequests => Set<HrAdvanceRequest>();
    public DbSet<HrPayrollRecord> PayrollRecords => Set<HrPayrollRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<HrLeaveRequest>(entity =>
        {
            entity.ToTable("hr_leave_requests");
            ConfigureBase(entity);
            entity.HasIndex(x => new { x.CompanyId, x.Status, x.StartDate });
            entity.Property(x => x.StartDate).HasColumnType("date");
            entity.Property(x => x.EndDate).HasColumnType("date");
            entity.Property(x => x.TotalDays).HasPrecision(8, 2);
            entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.DocumentPath).HasMaxLength(500);
            entity.Property(x => x.ApprovalNote).HasMaxLength(1000);
        });

        modelBuilder.Entity<HrOvertimeRequest>(entity =>
        {
            entity.ToTable("hr_overtime_requests");
            ConfigureBase(entity);
            entity.HasIndex(x => new { x.CompanyId, x.Status, x.WorkDate });
            entity.Property(x => x.WorkDate).HasColumnType("date");
            entity.Property(x => x.RequestedHours).HasPrecision(8, 2);
            entity.Property(x => x.ApprovedHours).HasPrecision(8, 2);
            entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.ApprovalNote).HasMaxLength(1000);
        });

        modelBuilder.Entity<HrAdvanceRequest>(entity =>
        {
            entity.ToTable("hr_advance_requests");
            ConfigureBase(entity);
            entity.HasIndex(x => new { x.CompanyId, x.Status, x.RequestDate });
            entity.Property(x => x.RequestDate).HasColumnType("date");
            entity.Property(x => x.FirstDeductionDate).HasColumnType("date");
            entity.Property(x => x.RequestedAmount).HasPrecision(18, 2);
            entity.Property(x => x.ApprovedAmount).HasPrecision(18, 2);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.PaymentReference).HasMaxLength(150);
            entity.Property(x => x.ApprovalNote).HasMaxLength(1000);
        });

        modelBuilder.Entity<HrPayrollRecord>(entity =>
        {
            entity.ToTable("hr_payroll_records");
            ConfigureBase(entity);
            entity.HasIndex(x => new { x.CompanyId, x.PersonnelId, x.Year, x.Month })
                .IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.Status, x.Year, x.Month });

            foreach (var property in new[]
                     {
                         nameof(HrPayrollRecord.GrossSalary),
                         nameof(HrPayrollRecord.NormalWorkAmount),
                         nameof(HrPayrollRecord.OvertimeAmount),
                         nameof(HrPayrollRecord.SundayWorkAmount),
                         nameof(HrPayrollRecord.PublicHolidayAmount),
                         nameof(HrPayrollRecord.BonusAmount),
                         nameof(HrPayrollRecord.MealAmount),
                         nameof(HrPayrollRecord.TravelAmount),
                         nameof(HrPayrollRecord.OtherEarningAmount),
                         nameof(HrPayrollRecord.CompensationAmount),
                         nameof(HrPayrollRecord.TotalEarnings),
                         nameof(HrPayrollRecord.SgkEmployeeDeduction),
                         nameof(HrPayrollRecord.IncomeTaxDeduction),
                         nameof(HrPayrollRecord.StampTaxDeduction),
                         nameof(HrPayrollRecord.AdvanceDeduction),
                         nameof(HrPayrollRecord.OtherDeductionAmount),
                         nameof(HrPayrollRecord.TotalDeductions),
                         nameof(HrPayrollRecord.OfficialNetPayableAmount),
                         nameof(HrPayrollRecord.ActualPayableAmount),
                         nameof(HrPayrollRecord.NetPayableAmount)
                     })
            {
                entity.Property<decimal>(property).HasPrecision(18, 2);
            }

            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.PaymentReference).HasMaxLength(150);
            entity.Property(x => x.Description).HasMaxLength(2000);
        });
    }

    private static void ConfigureBase<TEntity>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
        where TEntity : EnderunAI.Api.Models.BaseEntity
    {
        entity.HasKey(x => x.Id);
        entity.HasQueryFilter(x => !x.IsDeleted);
    }
}
