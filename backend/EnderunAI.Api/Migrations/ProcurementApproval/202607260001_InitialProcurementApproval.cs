using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations.ProcurementApproval;

[DbContext(typeof(ProcurementApprovalDbContext))]
[Migration("202607260001_InitialProcurementApproval")]
public sealed class InitialProcurementApproval : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS procurement_approval_rules (
    "Id" uuid PRIMARY KEY,
    "CompanyId" uuid NOT NULL,
    "DocumentType" integer NOT NULL,
    "Name" character varying(200) NOT NULL,
    "MinimumAmount" numeric(18,2) NOT NULL,
    "MaximumAmount" numeric(18,2) NULL,
    "CurrencyCode" character varying(3) NOT NULL,
    "FlowMode" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "Priority" integer NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);

CREATE TABLE IF NOT EXISTS procurement_approval_rule_steps (
    "Id" uuid PRIMARY KEY,
    "RuleId" uuid NOT NULL REFERENCES procurement_approval_rules("Id") ON DELETE CASCADE,
    "SequenceNo" integer NOT NULL,
    "RoleName" character varying(100) NOT NULL,
    "IsRequired" boolean NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);

CREATE TABLE IF NOT EXISTS procurement_approval_instances (
    "Id" uuid PRIMARY KEY,
    "CompanyId" uuid NOT NULL,
    "DocumentType" integer NOT NULL,
    "DocumentId" uuid NOT NULL,
    "DocumentNumber" character varying(100) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "CurrencyCode" character varying(3) NOT NULL,
    "RuleId" uuid NOT NULL,
    "FlowMode" integer NOT NULL,
    "Status" integer NOT NULL,
    "SubmittedAtUtc" timestamp with time zone NOT NULL,
    "CompletedAtUtc" timestamp with time zone NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);

CREATE TABLE IF NOT EXISTS procurement_approval_instance_steps (
    "Id" uuid PRIMARY KEY,
    "InstanceId" uuid NOT NULL REFERENCES procurement_approval_instances("Id") ON DELETE CASCADE,
    "SequenceNo" integer NOT NULL,
    "RoleName" character varying(100) NOT NULL,
    "IsRequired" boolean NOT NULL,
    "Status" integer NOT NULL,
    "ActionByUserId" uuid NULL,
    "ActionByName" character varying(200) NULL,
    "ActionAtUtc" timestamp with time zone NULL,
    "Comment" character varying(1000) NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);

CREATE TABLE IF NOT EXISTS procurement_approval_history (
    "Id" uuid PRIMARY KEY,
    "InstanceId" uuid NOT NULL REFERENCES procurement_approval_instances("Id") ON DELETE CASCADE,
    "StepId" uuid NULL,
    "ActionType" integer NOT NULL,
    "ActionByUserId" uuid NULL,
    "ActionByName" character varying(200) NULL,
    "RoleName" character varying(100) NULL,
    "ActionAtUtc" timestamp with time zone NOT NULL,
    "IpAddress" character varying(100) NULL,
    "Comment" character varying(1000) NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_procurement_approval_rules_lookup ON procurement_approval_rules ("CompanyId", "DocumentType", "Priority");
CREATE UNIQUE INDEX IF NOT EXISTS ix_procurement_approval_rule_steps_sequence ON procurement_approval_rule_steps ("RuleId", "SequenceNo") WHERE "IsDeleted" = false;
CREATE INDEX IF NOT EXISTS ix_procurement_approval_instances_document ON procurement_approval_instances ("DocumentType", "DocumentId", "Status");
CREATE INDEX IF NOT EXISTS ix_procurement_approval_instance_steps_instance ON procurement_approval_instance_steps ("InstanceId", "SequenceNo");
CREATE INDEX IF NOT EXISTS ix_procurement_approval_history_instance ON procurement_approval_history ("InstanceId", "ActionAtUtc");
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DROP TABLE IF EXISTS procurement_approval_history;
DROP TABLE IF EXISTS procurement_approval_instance_steps;
DROP TABLE IF EXISTS procurement_approval_instances;
DROP TABLE IF EXISTS procurement_approval_rule_steps;
DROP TABLE IF EXISTS procurement_approval_rules;
""");
    }
}
