using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations.ProjectBudget;

[DbContext(typeof(ProjectBudgetDbContext))]
[Migration("202607260001_InitialProjectBudget")]
public sealed class InitialProjectBudget : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS project_budgets (
    "Id" uuid PRIMARY KEY,
    "CompanyId" uuid NOT NULL,
    "ProjectId" uuid NOT NULL,
    "BudgetNumber" character varying(80) NOT NULL,
    "Name" character varying(250) NOT NULL,
    "CurrencyCode" character varying(3) NOT NULL,
    "BaseAmount" numeric(18,2) NOT NULL,
    "WarningThresholdPercent" numeric(5,2) NOT NULL,
    "CriticalThresholdPercent" numeric(5,2) NOT NULL,
    "Status" integer NOT NULL,
    "EffectiveDateUtc" timestamp with time zone NOT NULL,
    "Description" character varying(1000) NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);

CREATE TABLE IF NOT EXISTS project_budget_items (
    "Id" uuid PRIMARY KEY,
    "ProjectBudgetId" uuid NOT NULL REFERENCES project_budgets("Id") ON DELETE CASCADE,
    "Code" character varying(80) NOT NULL,
    "Name" character varying(250) NOT NULL,
    "MaterialId" uuid NULL,
    "Category" character varying(120) NULL,
    "PlannedAmount" numeric(18,2) NOT NULL,
    "CurrencyCode" character varying(3) NOT NULL,
    "SequenceNo" integer NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);

CREATE TABLE IF NOT EXISTS project_budget_revisions (
    "Id" uuid PRIMARY KEY,
    "ProjectBudgetId" uuid NOT NULL REFERENCES project_budgets("Id") ON DELETE CASCADE,
    "RevisionNumber" integer NOT NULL,
    "PreviousAmount" numeric(18,2) NOT NULL,
    "RevisedAmount" numeric(18,2) NOT NULL,
    "Reason" character varying(1000) NOT NULL,
    "CreatedByUserId" uuid NULL,
    "CreatedByName" character varying(200) NOT NULL,
    "RevisionDateUtc" timestamp with time zone NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);

CREATE TABLE IF NOT EXISTS project_budget_consumptions (
    "Id" uuid PRIMARY KEY,
    "CompanyId" uuid NOT NULL,
    "ProjectId" uuid NOT NULL,
    "ProjectBudgetId" uuid NOT NULL,
    "ProjectBudgetItemId" uuid NULL,
    "Type" integer NOT NULL,
    "ReferenceType" character varying(80) NOT NULL,
    "ReferenceId" uuid NULL,
    "ReferenceNumber" character varying(100) NULL,
    "Amount" numeric(18,2) NOT NULL,
    "CurrencyCode" character varying(3) NOT NULL,
    "ExchangeRate" numeric(18,6) NOT NULL,
    "ConsumptionDateUtc" timestamp with time zone NOT NULL,
    "Description" character varying(1000) NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);

CREATE TABLE IF NOT EXISTS project_budget_alerts (
    "Id" uuid PRIMARY KEY,
    "CompanyId" uuid NOT NULL,
    "ProjectId" uuid NOT NULL,
    "ProjectBudgetId" uuid NOT NULL,
    "ProjectBudgetItemId" uuid NULL,
    "Level" integer NOT NULL,
    "Code" character varying(80) NOT NULL,
    "Message" character varying(1000) NOT NULL,
    "BudgetAmount" numeric(18,2) NOT NULL,
    "UsedAmount" numeric(18,2) NOT NULL,
    "ProposedAmount" numeric(18,2) NOT NULL,
    "VarianceAmount" numeric(18,2) NOT NULL,
    "IsResolved" boolean NOT NULL,
    "ResolvedAtUtc" timestamp with time zone NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_project_budgets_number ON project_budgets ("CompanyId", "ProjectId", "BudgetNumber") WHERE "IsDeleted" = false;
CREATE UNIQUE INDEX IF NOT EXISTS ix_project_budget_items_code ON project_budget_items ("ProjectBudgetId", "Code") WHERE "IsDeleted" = false;
CREATE UNIQUE INDEX IF NOT EXISTS ix_project_budget_revisions_number ON project_budget_revisions ("ProjectBudgetId", "RevisionNumber") WHERE "IsDeleted" = false;
CREATE INDEX IF NOT EXISTS ix_project_budget_consumptions_project ON project_budget_consumptions ("ProjectId", "Type", "ConsumptionDateUtc");
CREATE INDEX IF NOT EXISTS ix_project_budget_consumptions_reference ON project_budget_consumptions ("ReferenceType", "ReferenceId", "Type");
CREATE INDEX IF NOT EXISTS ix_project_budget_alerts_open ON project_budget_alerts ("ProjectId", "IsResolved", "Level");
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DROP TABLE IF EXISTS project_budget_alerts;
DROP TABLE IF EXISTS project_budget_consumptions;
DROP TABLE IF EXISTS project_budget_revisions;
DROP TABLE IF EXISTS project_budget_items;
DROP TABLE IF EXISTS project_budgets;
""");
    }
}
