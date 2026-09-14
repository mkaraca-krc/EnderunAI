START TRANSACTION;

CREATE TABLE audit_logs (
    "Id" uuid NOT NULL,
    "CompanyId" uuid NOT NULL,
    "UserId" uuid,
    "EntityType" character varying(100) NOT NULL,
    "EntityId" uuid,
    "DocumentNumber" character varying(100),
    "Action" character varying(100) NOT NULL,
    "Description" character varying(2000) NOT NULL,
    "OldValuesJson" jsonb,
    "NewValuesJson" jsonb,
    "IpAddress" character varying(100),
    "IsActive" boolean NOT NULL,
    "IsDeleted" boolean NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "CreatedByUserId" uuid,
    "UpdatedAtUtc" timestamp with time zone,
    "UpdatedByUserId" uuid,
    "DeletedAtUtc" timestamp with time zone,
    "DeletedByUserId" uuid,
    CONSTRAINT "PK_audit_logs" PRIMARY KEY ("Id")
);

CREATE INDEX "IX_audit_logs_CompanyId_CreatedAtUtc" ON audit_logs ("CompanyId", "CreatedAtUtc");

CREATE INDEX "IX_audit_logs_DocumentNumber_CreatedAtUtc" ON audit_logs ("DocumentNumber", "CreatedAtUtc");

CREATE INDEX "IX_audit_logs_EntityType_EntityId" ON audit_logs ("EntityType", "EntityId");

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20260913144150_YetimAuditLogsTablosuDusuruldu';

COMMIT;

