using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260726235500_CompleteSecretariatModule")]
public partial class CompleteSecretariatModule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS "document_categories" (
                "Id" uuid NOT NULL,
                "CompanyId" uuid NOT NULL,
                "Code" character varying(40) NOT NULL,
                "Name" character varying(150) NOT NULL,
                "Description" character varying(500),
                "IsDefault" boolean NOT NULL,
                "IsActive" boolean NOT NULL,
                "IsDeleted" boolean NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "CreatedByUserId" uuid,
                "UpdatedAtUtc" timestamp with time zone,
                "UpdatedByUserId" uuid,
                "DeletedAtUtc" timestamp with time zone,
                "DeletedByUserId" uuid,
                CONSTRAINT "PK_document_categories" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_document_categories_CompanyId_Code"
                ON "document_categories" ("CompanyId", "Code");

            CREATE TABLE IF NOT EXISTS "incoming_documents" (
                "Id" uuid NOT NULL,
                "CompanyId" uuid NOT NULL,
                "ProjectId" uuid,
                "CategoryId" uuid,
                "DocumentNumber" character varying(80) NOT NULL,
                "ExternalDocumentNumber" character varying(100),
                "DocumentDate" timestamp with time zone NOT NULL,
                "RegisteredAtUtc" timestamp with time zone NOT NULL,
                "SenderName" character varying(200) NOT NULL,
                "SenderOrganization" character varying(250),
                "Subject" character varying(500) NOT NULL,
                "Description" character varying(2000),
                "DeliveryMethod" character varying(100),
                "Priority" integer NOT NULL,
                "Status" integer NOT NULL,
                "AssignedToUserId" uuid,
                "AssignedToName" character varying(200),
                "DueDate" timestamp with time zone,
                "CompletedAtUtc" timestamp with time zone,
                "ArchivedAtUtc" timestamp with time zone,
                "Notes" character varying(2000),
                "IsActive" boolean NOT NULL,
                "IsDeleted" boolean NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "CreatedByUserId" uuid,
                "UpdatedAtUtc" timestamp with time zone,
                "UpdatedByUserId" uuid,
                "DeletedAtUtc" timestamp with time zone,
                "DeletedByUserId" uuid,
                CONSTRAINT "PK_incoming_documents" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_incoming_documents_CompanyId_DocumentNumber"
                ON "incoming_documents" ("CompanyId", "DocumentNumber");
            CREATE INDEX IF NOT EXISTS "IX_incoming_documents_CompanyId_Status_DocumentDate"
                ON "incoming_documents" ("CompanyId", "Status", "DocumentDate");

            CREATE TABLE IF NOT EXISTS "outgoing_documents" (
                "Id" uuid NOT NULL,
                "CompanyId" uuid NOT NULL,
                "ProjectId" uuid,
                "CategoryId" uuid,
                "DocumentNumber" character varying(80) NOT NULL,
                "DocumentDate" timestamp with time zone NOT NULL,
                "RegisteredAtUtc" timestamp with time zone NOT NULL,
                "RecipientName" character varying(200) NOT NULL,
                "RecipientOrganization" character varying(250),
                "Subject" character varying(500) NOT NULL,
                "Description" character varying(2000),
                "DeliveryMethod" character varying(100),
                "ReferenceNumber" character varying(100),
                "SignedByName" character varying(200),
                "Priority" integer NOT NULL,
                "Status" integer NOT NULL,
                "SentAtUtc" timestamp with time zone,
                "CompletedAtUtc" timestamp with time zone,
                "ArchivedAtUtc" timestamp with time zone,
                "Notes" character varying(2000),
                "IsActive" boolean NOT NULL,
                "IsDeleted" boolean NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "CreatedByUserId" uuid,
                "UpdatedAtUtc" timestamp with time zone,
                "UpdatedByUserId" uuid,
                "DeletedAtUtc" timestamp with time zone,
                "DeletedByUserId" uuid,
                CONSTRAINT "PK_outgoing_documents" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_outgoing_documents_CompanyId_DocumentNumber"
                ON "outgoing_documents" ("CompanyId", "DocumentNumber");
            CREATE INDEX IF NOT EXISTS "IX_outgoing_documents_CompanyId_Status_DocumentDate"
                ON "outgoing_documents" ("CompanyId", "Status", "DocumentDate");

            CREATE TABLE IF NOT EXISTS "document_workflows" (
                "Id" uuid NOT NULL,
                "CompanyId" uuid NOT NULL,
                "Direction" integer NOT NULL,
                "DocumentId" uuid NOT NULL,
                "Action" integer NOT NULL,
                "FromUserId" uuid,
                "FromUserName" character varying(200),
                "ToUserId" uuid,
                "ToUserName" character varying(200),
                "Description" character varying(1000),
                "ActionAtUtc" timestamp with time zone NOT NULL,
                "IsActive" boolean NOT NULL,
                "IsDeleted" boolean NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "CreatedByUserId" uuid,
                "UpdatedAtUtc" timestamp with time zone,
                "UpdatedByUserId" uuid,
                "DeletedAtUtc" timestamp with time zone,
                "DeletedByUserId" uuid,
                CONSTRAINT "PK_document_workflows" PRIMARY KEY ("Id")
            );
            CREATE INDEX IF NOT EXISTS "IX_document_workflows_Direction_DocumentId_ActionAtUtc"
                ON "document_workflows" ("Direction", "DocumentId", "ActionAtUtc");

            CREATE TABLE IF NOT EXISTS "document_attachments" (
                "Id" uuid NOT NULL,
                "CompanyId" uuid NOT NULL,
                "Direction" integer NOT NULL,
                "DocumentId" uuid NOT NULL,
                "FileName" character varying(255) NOT NULL,
                "StoredFileName" character varying(255) NOT NULL,
                "FilePath" character varying(500) NOT NULL,
                "ContentType" character varying(150),
                "FileSize" bigint NOT NULL,
                "Description" character varying(500),
                "IsActive" boolean NOT NULL,
                "IsDeleted" boolean NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "CreatedByUserId" uuid,
                "UpdatedAtUtc" timestamp with time zone,
                "UpdatedByUserId" uuid,
                "DeletedAtUtc" timestamp with time zone,
                "DeletedByUserId" uuid,
                CONSTRAINT "PK_document_attachments" PRIMARY KEY ("Id")
            );
            CREATE INDEX IF NOT EXISTS "IX_document_attachments_Direction_DocumentId"
                ON "document_attachments" ("Direction", "DocumentId");

            CREATE TABLE IF NOT EXISTS "cargo_shipments" (
                "Id" uuid NOT NULL,
                "CompanyId" uuid NOT NULL,
                "ProjectId" uuid,
                "Direction" integer NOT NULL,
                "TrackingNumber" character varying(100) NOT NULL,
                "CargoCompany" character varying(150) NOT NULL,
                "SenderName" character varying(200),
                "RecipientName" character varying(200),
                "InstitutionName" character varying(250),
                "CargoDate" timestamp with time zone NOT NULL,
                "ExpectedDeliveryDate" timestamp with time zone,
                "DeliveredAtUtc" timestamp with time zone,
                "DeliveredToName" character varying(200),
                "Description" character varying(1000),
                "Status" integer NOT NULL,
                "IsActive" boolean NOT NULL,
                "IsDeleted" boolean NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "CreatedByUserId" uuid,
                "UpdatedAtUtc" timestamp with time zone,
                "UpdatedByUserId" uuid,
                "DeletedAtUtc" timestamp with time zone,
                "DeletedByUserId" uuid,
                CONSTRAINT "PK_cargo_shipments" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_cargo_shipments_CompanyId_TrackingNumber"
                ON "cargo_shipments" ("CompanyId", "TrackingNumber");
            CREATE INDEX IF NOT EXISTS "IX_cargo_shipments_CompanyId_Status_CargoDate"
                ON "cargo_shipments" ("CompanyId", "Status", "CargoDate");

            CREATE TABLE IF NOT EXISTS "visitor_records" (
                "Id" uuid NOT NULL,
                "CompanyId" uuid NOT NULL,
                "ProjectId" uuid,
                "FullName" character varying(200) NOT NULL,
                "IdentityNumber" character varying(20),
                "PhoneNumber" character varying(30),
                "Email" character varying(200),
                "CompanyName" character varying(250),
                "VehiclePlate" character varying(20),
                "VisitorCardNumber" character varying(50),
                "PersonToVisit" character varying(200) NOT NULL,
                "DepartmentName" character varying(150),
                "VisitPurpose" character varying(500) NOT NULL,
                "PlannedVisitAtUtc" timestamp with time zone NOT NULL,
                "CheckInAtUtc" timestamp with time zone,
                "CheckOutAtUtc" timestamp with time zone,
                "ApprovedByName" character varying(200),
                "ReceivedByName" character varying(200),
                "Description" character varying(1000),
                "Status" integer NOT NULL,
                "IsActive" boolean NOT NULL,
                "IsDeleted" boolean NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "CreatedByUserId" uuid,
                "UpdatedAtUtc" timestamp with time zone,
                "UpdatedByUserId" uuid,
                "DeletedAtUtc" timestamp with time zone,
                "DeletedByUserId" uuid,
                CONSTRAINT "PK_visitor_records" PRIMARY KEY ("Id")
            );
            CREATE INDEX IF NOT EXISTS "IX_visitor_records_CompanyId_Status_PlannedVisitAtUtc"
                ON "visitor_records" ("CompanyId", "Status", "PlannedVisitAtUtc");

            CREATE TABLE IF NOT EXISTS "phone_notes" (
                "Id" uuid NOT NULL,
                "CompanyId" uuid NOT NULL,
                "ProjectId" uuid,
                "CallerName" character varying(200) NOT NULL,
                "PhoneNumber" character varying(30),
                "InstitutionName" character varying(250),
                "Subject" character varying(300) NOT NULL,
                "Message" character varying(2000) NOT NULL,
                "ResponsibleName" character varying(200) NOT NULL,
                "ReceivedAtUtc" timestamp with time zone NOT NULL,
                "InformedAtUtc" timestamp with time zone,
                "ReturnedAtUtc" timestamp with time zone,
                "Status" integer NOT NULL,
                "Notes" character varying(1000),
                "IsActive" boolean NOT NULL,
                "IsDeleted" boolean NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "CreatedByUserId" uuid,
                "UpdatedAtUtc" timestamp with time zone,
                "UpdatedByUserId" uuid,
                "DeletedAtUtc" timestamp with time zone,
                "DeletedByUserId" uuid,
                CONSTRAINT "PK_phone_notes" PRIMARY KEY ("Id")
            );
            CREATE INDEX IF NOT EXISTS "IX_phone_notes_CompanyId_Status_ReceivedAtUtc"
                ON "phone_notes" ("CompanyId", "Status", "ReceivedAtUtc");

            CREATE TABLE IF NOT EXISTS "secretariat_schedule_entries" (
                "Id" uuid NOT NULL,
                "CompanyId" uuid NOT NULL,
                "ProjectId" uuid,
                "Type" integer NOT NULL,
                "Title" character varying(300) NOT NULL,
                "ContactName" character varying(200),
                "CompanyName" character varying(250),
                "Location" character varying(300),
                "StartAtUtc" timestamp with time zone NOT NULL,
                "EndAtUtc" timestamp with time zone,
                "OwnerName" character varying(200),
                "Participants" character varying(2000),
                "Description" character varying(2000),
                "ReminderAtUtc" timestamp with time zone,
                "CompletedAtUtc" timestamp with time zone,
                "Status" integer NOT NULL,
                "Notes" character varying(1000),
                "IsActive" boolean NOT NULL,
                "IsDeleted" boolean NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "CreatedByUserId" uuid,
                "UpdatedAtUtc" timestamp with time zone,
                "UpdatedByUserId" uuid,
                "DeletedAtUtc" timestamp with time zone,
                "DeletedByUserId" uuid,
                CONSTRAINT "PK_secretariat_schedule_entries" PRIMARY KEY ("Id")
            );
            CREATE INDEX IF NOT EXISTS "IX_secretariat_schedule_entries_CompanyId_Type_Status_StartAtUtc"
                ON "secretariat_schedule_entries" ("CompanyId", "Type", "Status", "StartAtUtc");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS "secretariat_schedule_entries";
            DROP TABLE IF EXISTS "phone_notes";
            DROP TABLE IF EXISTS "visitor_records";
            DROP TABLE IF EXISTS "cargo_shipments";
            DROP TABLE IF EXISTS "document_attachments";
            DROP TABLE IF EXISTS "document_workflows";
            DROP TABLE IF EXISTS "outgoing_documents";
            DROP TABLE IF EXISTS "incoming_documents";
            DROP TABLE IF EXISTS "document_categories";
            """);
    }
}
