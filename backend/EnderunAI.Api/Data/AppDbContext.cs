using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AppRole> Roles => Set<AppRole>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<CurrentAccount> CurrentAccounts => Set<CurrentAccount>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureSecurity(modelBuilder);
        ConfigureCompanies(modelBuilder);
        ConfigureBranches(modelBuilder);
        ConfigureCurrentAccounts(modelBuilder);
        ConfigureProjects(modelBuilder);
        ConfigureWarehouses(modelBuilder);
    }

    private static void ConfigureSecurity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Username).IsUnique();

            entity.Property(x => x.Username).HasMaxLength(100).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(200);
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.PasswordSalt).IsRequired();
        });

        modelBuilder.Entity<AppRole>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Name).IsUnique();

            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(300);
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("user_roles");
            entity.HasKey(x => new { x.UserId, x.RoleId });

            entity.HasOne(x => x.User)
                .WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Role)
                .WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureCompanies(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("companies");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
            entity.Property(x => x.TradeName).HasMaxLength(300);
            entity.Property(x => x.TaxOffice).HasMaxLength(100);
            entity.Property(x => x.TaxNumber).HasMaxLength(20);
            entity.Property(x => x.MersisNumber).HasMaxLength(30);
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Email).HasMaxLength(200);
            entity.Property(x => x.Website).HasMaxLength(250);
            entity.Property(x => x.LogoPath).HasMaxLength(500);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureBranches(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Branch>(entity =>
        {
            entity.ToTable("branches");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Email).HasMaxLength(200);

            entity.HasOne(x => x.Company)
                .WithMany(x => x.Branches)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureCurrentAccounts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CurrentAccount>(entity =>
        {
            entity.ToTable("current_accounts");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ShortName).HasMaxLength(150);
            entity.Property(x => x.TaxOffice).HasMaxLength(100);
            entity.Property(x => x.TaxNumber).HasMaxLength(20);
            entity.Property(x => x.MersisNumber).HasMaxLength(30);
            entity.Property(x => x.AuthorizedPerson).HasMaxLength(200);
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Email).HasMaxLength(200);
            entity.Property(x => x.PaymentTerm).HasMaxLength(100);
            entity.Property(x => x.CreditLimit).HasPrecision(18, 2);

            entity.HasOne(x => x.Company)
                .WithMany(x => x.CurrentAccounts)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureProjects(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ContractNumber).HasMaxLength(100);
            entity.Property(x => x.ContractAmount).HasPrecision(18, 2);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.VatRate).HasPrecision(5, 2);
            entity.Property(x => x.WithholdingRate).HasMaxLength(20);
            entity.Property(x => x.City).HasMaxLength(100);
            entity.Property(x => x.District).HasMaxLength(100);
            entity.Property(x => x.HealthReason).HasMaxLength(500);

            entity.HasOne(x => x.Company)
                .WithMany(x => x.Projects)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Branch)
                .WithMany(x => x.Projects)
                .HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.EmployerCurrentAccount)
                .WithMany(x => x.EmployerProjects)
                .HasForeignKey(x => x.EmployerCurrentAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

    private static void ConfigureWarehouses(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.ToTable("warehouses");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Branch)
                .WithMany(x => x.Warehouses)
                .HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Project)
                .WithMany(x => x.Warehouses)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }
}
