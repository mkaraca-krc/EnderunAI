using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations.ProcurementDocument;

public partial class AddProcurementDocumentHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "procurement_document_attachments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                DocumentType = table.Column<int>(type: "integer", nullable: false),
                DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StoredFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                FilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                FileSize = table.Column<long>(type: "bigint", nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                UploadedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_procurement_document_attachments", x => x.Id));

        migrationBuilder.CreateTable(
            name: "procurement_document_comments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                DocumentType = table.Column<int>(type: "integer", nullable: false),
                DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_procurement_document_comments", x => x.Id));

        migrationBuilder.CreateTable(
            name: "procurement_document_revisions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                DocumentType = table.Column<int>(type: "integer", nullable: false),
                DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_procurement_document_revisions", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_procurement_document_attachments_CompanyId_DocumentType_DocumentId",
            table: "procurement_document_attachments",
            columns: new[] { "CompanyId", "DocumentType", "DocumentId" });

        migrationBuilder.CreateIndex(
            name: "IX_procurement_document_comments_CompanyId_DocumentType_DocumentId",
            table: "procurement_document_comments",
            columns: new[] { "CompanyId", "DocumentType", "DocumentId" });

        migrationBuilder.CreateIndex(
            name: "IX_procurement_document_revisions_CompanyId_DocumentType_DocumentId",
            table: "procurement_document_revisions",
            columns: new[] { "CompanyId", "DocumentType", "DocumentId" });

        migrationBuilder.CreateIndex(
            name: "IX_procurement_document_revisions_DocumentType_DocumentId_RevisionNumber",
            table: "procurement_document_revisions",
            columns: new[] { "DocumentType", "DocumentId", "RevisionNumber" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "procurement_document_attachments");
        migrationBuilder.DropTable(name: "procurement_document_comments");
        migrationBuilder.DropTable(name: "procurement_document_revisions");
    }
}
