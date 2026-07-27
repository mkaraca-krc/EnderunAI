using EnderunAI.Api.Data.HumanResources;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EnderunAI.Api.Migrations.HumanResources;

[DbContext(typeof(HrDbContext))]
[Migration("20260727030000_RepairHrSchemaCompatibility")]
public sealed class RepairHrSchemaCompatibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE IF EXISTS hr_payroll_records
                ADD COLUMN IF NOT EXISTS "CompanyId" uuid,
                ADD COLUMN IF NOT EXISTS "PersonnelId" uuid,
                ADD COLUMN IF NOT EXISTS "Year" integer NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "Month" integer NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "GrossSalary" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "NormalWorkAmount" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "OvertimeAmount" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "SundayWorkAmount" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "PublicHolidayAmount" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "BonusAmount" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "MealAmount" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "TravelAmount" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "OtherEarningAmount" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "CompensationAmount" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "TotalEarnings" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "SgkEmployeeDeduction" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "IncomeTaxDeduction" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "StampTaxDeduction" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "AdvanceDeduction" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "OtherDeductionAmount" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "TotalDeductions" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "OfficialNetPayableAmount" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "ActualPayableAmount" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "NetPayableAmount" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "CurrencyCode" varchar(3) NOT NULL DEFAULT 'TRY',
                ADD COLUMN IF NOT EXISTS "Status" integer NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "ApprovedAtUtc" timestamptz,
                ADD COLUMN IF NOT EXISTS "ApprovedByUserId" uuid,
                ADD COLUMN IF NOT EXISTS "PaidAtUtc" timestamptz,
                ADD COLUMN IF NOT EXISTS "PaymentReference" varchar(150),
                ADD COLUMN IF NOT EXISTS "Description" varchar(2000),
                ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT TRUE,
                ADD COLUMN IF NOT EXISTS "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                ADD COLUMN IF NOT EXISTS "CreatedAtUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid,
                ADD COLUMN IF NOT EXISTS "UpdatedAtUtc" timestamptz,
                ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid,
                ADD COLUMN IF NOT EXISTS "DeletedAtUtc" timestamptz,
                ADD COLUMN IF NOT EXISTS "DeletedByUserId" uuid;

            ALTER TABLE IF EXISTS hr_salary_definitions
                ADD COLUMN IF NOT EXISTS "CompanyId" uuid,
                ADD COLUMN IF NOT EXISTS "PersonnelId" uuid,
                ADD COLUMN IF NOT EXISTS "EffectiveStartDate" date,
                ADD COLUMN IF NOT EXISTS "EffectiveEndDate" date,
                ADD COLUMN IF NOT EXISTS "GrossSalary" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "NetSalary" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "DailyRate" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "HourlyRate" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "OvertimeMultiplier" numeric(8,4) NOT NULL DEFAULT 1.5,
                ADD COLUMN IF NOT EXISTS "SundayMultiplier" numeric(8,4) NOT NULL DEFAULT 2,
                ADD COLUMN IF NOT EXISTS "PublicHolidayMultiplier" numeric(8,4) NOT NULL DEFAULT 2,
                ADD COLUMN IF NOT EXISTS "CurrencyCode" varchar(3) NOT NULL DEFAULT 'TRY',
                ADD COLUMN IF NOT EXISTS "Description" varchar(1000),
                ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT TRUE,
                ADD COLUMN IF NOT EXISTS "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                ADD COLUMN IF NOT EXISTS "CreatedAtUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid,
                ADD COLUMN IF NOT EXISTS "UpdatedAtUtc" timestamptz,
                ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid,
                ADD COLUMN IF NOT EXISTS "DeletedAtUtc" timestamptz,
                ADD COLUMN IF NOT EXISTS "DeletedByUserId" uuid;

            CREATE INDEX IF NOT EXISTS "IX_hr_payroll_records_CompanyId_Status_Year_Month"
                ON hr_payroll_records ("CompanyId", "Status", "Year", "Month");
            CREATE INDEX IF NOT EXISTS "IX_hr_salary_definitions_Company_Personnel"
                ON hr_salary_definitions ("CompanyId", "PersonnelId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Uyumluluk migration'ı mevcut verileri ve eski şemayı korumak için geri alınmaz.
    }
}
