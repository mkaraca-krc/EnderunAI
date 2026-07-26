using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations.SupplierPerformance;

[DbContext(typeof(SupplierPerformanceDbContext))]
[Migration("202607260001_InitialSupplierPerformance")]
public sealed class InitialSupplierPerformance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS supplier_performance_snapshots (
    "Id" uuid PRIMARY KEY,
    "CompanyId" uuid NOT NULL,
    "SupplierCurrentAccountId" uuid NOT NULL,
    "PeriodStartUtc" timestamp with time zone NOT NULL,
    "PeriodEndUtc" timestamp with time zone NOT NULL,
    "DeliveryScore" numeric(5,2) NOT NULL,
    "QualityScore" numeric(5,2) NOT NULL,
    "PriceScore" numeric(5,2) NOT NULL,
    "TechnicalScore" numeric(5,2) NOT NULL,
    "FinancialScore" numeric(5,2) NOT NULL,
    "CommunicationScore" numeric(5,2) NOT NULL,
    "OverallScore" numeric(5,2) NOT NULL,
    "RiskLevel" integer NOT NULL,
    "TotalOrderCount" integer NOT NULL,
    "CompletedOrderCount" integer NOT NULL,
    "LateOrderCount" integer NOT NULL,
    "TotalOrderAmountTry" numeric(18,2) NOT NULL,
    "OnTimeDeliveryRate" numeric(5,2) NOT NULL,
    "ReturnRate" numeric(5,2) NOT NULL,
    "Notes" character varying(1000) NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);

CREATE TABLE IF NOT EXISTS supplier_quality_records (
    "Id" uuid PRIMARY KEY,
    "CompanyId" uuid NOT NULL,
    "SupplierCurrentAccountId" uuid NOT NULL,
    "PurchaseOrderId" uuid NULL,
    "GoodsReceiptId" uuid NULL,
    "MaterialId" uuid NULL,
    "EventType" integer NOT NULL,
    "Quantity" numeric(18,4) NOT NULL,
    "ImpactScore" numeric(5,2) NOT NULL,
    "Description" character varying(1000) NULL,
    "CreatedByUserId" uuid NULL,
    "CreatedByName" character varying(200) NOT NULL,
    "EventDateUtc" timestamp with time zone NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);

CREATE TABLE IF NOT EXISTS supplier_manual_evaluations (
    "Id" uuid PRIMARY KEY,
    "CompanyId" uuid NOT NULL,
    "SupplierCurrentAccountId" uuid NOT NULL,
    "CommunicationScore" numeric(5,2) NOT NULL,
    "FinancialScore" numeric(5,2) NOT NULL,
    "QualityScore" numeric(5,2) NOT NULL,
    "TechnicalScore" numeric(5,2) NOT NULL,
    "Comment" character varying(1000) NULL,
    "EvaluatedByUserId" uuid NULL,
    "EvaluatedByName" character varying(200) NOT NULL,
    "EvaluationDateUtc" timestamp with time zone NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NULL,
    "IsDeleted" boolean NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_supplier_performance_snapshots_lookup
    ON supplier_performance_snapshots ("CompanyId", "SupplierCurrentAccountId", "PeriodEndUtc");
CREATE INDEX IF NOT EXISTS ix_supplier_quality_records_lookup
    ON supplier_quality_records ("CompanyId", "SupplierCurrentAccountId", "EventDateUtc");
CREATE INDEX IF NOT EXISTS ix_supplier_manual_evaluations_lookup
    ON supplier_manual_evaluations ("CompanyId", "SupplierCurrentAccountId", "EvaluationDateUtc");
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DROP TABLE IF EXISTS supplier_manual_evaluations;
DROP TABLE IF EXISTS supplier_quality_records;
DROP TABLE IF EXISTS supplier_performance_snapshots;
""");
    }
}
