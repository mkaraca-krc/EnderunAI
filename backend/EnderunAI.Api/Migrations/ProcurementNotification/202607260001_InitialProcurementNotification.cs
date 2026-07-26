using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations.ProcurementNotification;

[DbContext(typeof(ProcurementNotificationDbContext))]
[Migration("202607260001_InitialProcurementNotification")]
public sealed class InitialProcurementNotification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS procurement_notifications (
    "Id" uuid PRIMARY KEY,
    "CompanyId" uuid NOT NULL,
    "UserId" uuid NULL,
    "RoleName" character varying(100) NULL,
    "Type" integer NOT NULL,
    "Severity" integer NOT NULL,
    "Title" character varying(250) NOT NULL,
    "Message" character varying(1000) NOT NULL,
    "DocumentType" character varying(80) NOT NULL,
    "DocumentId" uuid NULL,
    "DocumentNumber" character varying(100) NULL,
    "ApprovalInstanceId" uuid NULL,
    "ApprovalStepId" uuid NULL,
    "ActionUrl" character varying(500) NULL,
    "DueAtUtc" timestamp with time zone NULL,
    "ReadAtUtc" timestamp with time zone NULL,
    "DismissedAtUtc" timestamp with time zone NULL,
    "DeduplicationKey" character varying(250) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_procurement_notifications_dedup ON procurement_notifications ("DeduplicationKey");
CREATE INDEX IF NOT EXISTS ix_procurement_notifications_user ON procurement_notifications ("CompanyId", "UserId", "ReadAtUtc");
CREATE INDEX IF NOT EXISTS ix_procurement_notifications_role ON procurement_notifications ("CompanyId", "RoleName", "ReadAtUtc");
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS procurement_notifications;");
    }
}
