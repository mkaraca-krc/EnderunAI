using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

public sealed class ProcurementDocumentDbContext(DbContextOptions<ProcurementDocumentDbContext> options) : DbContext(options)
{
    public DbSet<ProcurementDocumentRevision> Revisions => Set<ProcurementDocumentRevision>();
    public DbSet<ProcurementDocumentAttachment> Attachments => Set<ProcurementDocumentAttachment>();
    public DbSet<ProcurementDocumentComment> Comments => Set<ProcurementDocumentComment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProcurementDocumentRevision>(entity =>
        {
            entity.ToTable("procurement_document_revisions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.DocumentType, x.DocumentId, x.RevisionNumber }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.DocumentType, x.DocumentId });
            entity.Property(x => x.Action).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.Property(x => x.SnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.CreatedByName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProcurementDocumentAttachment>(entity =>
        {
            entity.ToTable("procurement_document_attachments");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.DocumentType, x.DocumentId });
            entity.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.StoredFileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.FilePath).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.UploadedByName).HasMaxLength(200).IsRequired();
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ProcurementDocumentComment>(entity =>
        {
            entity.ToTable("procurement_document_comments");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CompanyId, x.DocumentType, x.DocumentId });
            entity.Property(x => x.Comment).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.CreatedByName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }
}
