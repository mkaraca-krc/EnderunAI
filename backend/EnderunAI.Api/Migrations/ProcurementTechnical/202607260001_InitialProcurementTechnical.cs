using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations.ProcurementTechnical;

[DbContext(typeof(ProcurementTechnicalDbContext))]
[Migration("202607260001_InitialProcurementTechnical")]
public sealed class InitialProcurementTechnical : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS procurement_technical_specifications (
    "Id" uuid PRIMARY KEY,
    "CompanyId" uuid NOT NULL,
    "ProjectId" uuid NOT NULL,
    "RfqId" uuid NULL,
    "Code" character varying(80) NOT NULL,
    "Name" character varying(250) NOT NULL,
    "Description" character varying(1000) NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);

CREATE TABLE IF NOT EXISTS procurement_technical_criteria (
    "Id" uuid PRIMARY KEY,
    "TechnicalSpecificationId" uuid NOT NULL REFERENCES procurement_technical_specifications("Id") ON DELETE CASCADE,
    "RfqItemId" uuid NULL,
    "Code" character varying(80) NOT NULL,
    "Name" character varying(250) NOT NULL,
    "Type" integer NOT NULL,
    "ExpectedValue" character varying(1000) NULL,
    "NumericValue" numeric(18,4) NULL,
    "Unit" character varying(30) NULL,
    "IsMandatory" boolean NOT NULL,
    "Weight" numeric(10,4) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);

CREATE TABLE IF NOT EXISTS procurement_offer_technical_responses (
    "Id" uuid PRIMARY KEY,
    "SupplierOfferId" uuid NOT NULL,
    "SupplierOfferItemId" uuid NOT NULL,
    "TechnicalCriterionId" uuid NOT NULL,
    "OfferedValue" character varying(1000) NULL,
    "OfferedNumericValue" numeric(18,4) NULL,
    "IsProvided" boolean NULL,
    "EvidenceReference" character varying(1000) NULL,
    "Status" integer NOT NULL,
    "Score" numeric(10,2) NOT NULL,
    "EvaluationNote" character varying(1000) NULL,
    "EvaluatedByUserId" uuid NULL,
    "EvaluatedByName" character varying(200) NULL,
    "EvaluatedAtUtc" timestamp with time zone NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_procurement_technical_specifications_company_code
ON procurement_technical_specifications ("CompanyId", "Code") WHERE "IsDeleted" = false;

CREATE UNIQUE INDEX IF NOT EXISTS ix_procurement_technical_criteria_specification_code
ON procurement_technical_criteria ("TechnicalSpecificationId", "Code") WHERE "IsDeleted" = false;

CREATE UNIQUE INDEX IF NOT EXISTS ix_procurement_offer_technical_responses_item_criterion
ON procurement_offer_technical_responses ("SupplierOfferItemId", "TechnicalCriterionId") WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS ix_procurement_technical_specifications_rfq
ON procurement_technical_specifications ("RfqId", "IsActive");

CREATE INDEX IF NOT EXISTS ix_procurement_offer_technical_responses_offer
ON procurement_offer_technical_responses ("SupplierOfferId", "Status");
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DROP TABLE IF EXISTS procurement_offer_technical_responses;
DROP TABLE IF EXISTS procurement_technical_criteria;
DROP TABLE IF EXISTS procurement_technical_specifications;
""");
    }
}
