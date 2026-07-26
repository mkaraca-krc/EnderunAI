using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations.RfqInvitation;

public partial class AddRfqInvitations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS rfq_supplier_invitations (
    "Id" uuid PRIMARY KEY,
    "CompanyId" uuid NOT NULL,
    "RfqId" uuid NOT NULL,
    "SupplierCurrentAccountId" uuid NOT NULL,
    "RecipientEmail" varchar(250) NOT NULL,
    "RecipientName" varchar(200) NOT NULL,
    "TokenHash" varchar(64) NOT NULL,
    "ExpiresAtUtc" timestamptz NOT NULL,
    "SingleUse" boolean NOT NULL DEFAULT false,
    "Status" integer NOT NULL,
    "SentAtUtc" timestamptz NULL,
    "OpenedAtUtc" timestamptz NULL,
    "OfferSubmittedAtUtc" timestamptz NULL,
    "LastReminderAtUtc" timestamptz NULL,
    "ReminderCount" integer NOT NULL DEFAULT 0,
    "SendAttemptCount" integer NOT NULL DEFAULT 0,
    "LastError" varchar(2000) NULL,
    "IsActive" boolean NOT NULL DEFAULT true,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    "CreatedAtUtc" timestamptz NOT NULL,
    "CreatedByUserId" uuid NULL,
    "UpdatedAtUtc" timestamptz NULL,
    "UpdatedByUserId" uuid NULL,
    "DeletedAtUtc" timestamptz NULL,
    "DeletedByUserId" uuid NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_rfq_supplier_invitations_TokenHash" ON rfq_supplier_invitations ("TokenHash");
CREATE INDEX IF NOT EXISTS "IX_rfq_supplier_invitations_RfqId_Supplier_Status" ON rfq_supplier_invitations ("RfqId", "SupplierCurrentAccountId", "Status");

CREATE TABLE IF NOT EXISTS rfq_invitation_events (
    "Id" uuid PRIMARY KEY,
    "InvitationId" uuid NOT NULL,
    "EventType" varchar(80) NOT NULL,
    "EventDateUtc" timestamptz NOT NULL,
    "IpAddress" varchar(80) NULL,
    "UserAgent" varchar(500) NULL,
    "Detail" varchar(2000) NULL,
    "IsActive" boolean NOT NULL DEFAULT true,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    "CreatedAtUtc" timestamptz NOT NULL,
    "CreatedByUserId" uuid NULL,
    "UpdatedAtUtc" timestamptz NULL,
    "UpdatedByUserId" uuid NULL,
    "DeletedAtUtc" timestamptz NULL,
    "DeletedByUserId" uuid NULL
);
CREATE INDEX IF NOT EXISTS "IX_rfq_invitation_events_Invitation_Date" ON rfq_invitation_events ("InvitationId", "EventDateUtc");
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS rfq_invitation_events; DROP TABLE IF EXISTS rfq_supplier_invitations;");
    }
}
