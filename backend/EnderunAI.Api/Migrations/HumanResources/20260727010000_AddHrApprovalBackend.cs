using EnderunAI.Api.Data.HumanResources;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EnderunAI.Api.Migrations.HumanResources;

[DbContext(typeof(HrDbContext))]
[Migration("20260727010000_AddHrApprovalBackend")]
public sealed class AddHrApprovalBackend : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS hr_leave_requests (
                "Id" uuid PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "PersonnelId" uuid NOT NULL,
                "ProjectId" uuid NULL,
                "LeaveType" integer NOT NULL,
                "StartDate" date NOT NULL,
                "EndDate" date NOT NULL,
                "TotalDays" numeric(8,2) NOT NULL,
                "Reason" varchar(1000) NOT NULL,
                "DocumentPath" varchar(500) NULL,
                "Status" integer NOT NULL,
                "ApprovedByUserId" uuid NULL,
                "ApprovedAtUtc" timestamptz NULL,
                "ApprovalNote" varchar(1000) NULL,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                "CreatedAtUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                "CreatedByUserId" uuid NULL,
                "UpdatedAtUtc" timestamptz NULL,
                "UpdatedByUserId" uuid NULL,
                "DeletedAtUtc" timestamptz NULL,
                "DeletedByUserId" uuid NULL
            );

            CREATE TABLE IF NOT EXISTS hr_overtime_requests (
                "Id" uuid PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "PersonnelId" uuid NOT NULL,
                "ProjectId" uuid NULL,
                "WorkDate" date NOT NULL,
                "RequestedHours" numeric(8,2) NOT NULL,
                "ApprovedHours" numeric(8,2) NOT NULL DEFAULT 0,
                "IsSundayWork" boolean NOT NULL DEFAULT FALSE,
                "IsPublicHolidayWork" boolean NOT NULL DEFAULT FALSE,
                "IsNightWork" boolean NOT NULL DEFAULT FALSE,
                "Reason" varchar(1000) NOT NULL,
                "Status" integer NOT NULL,
                "ApprovedByUserId" uuid NULL,
                "ApprovedAtUtc" timestamptz NULL,
                "ApprovalNote" varchar(1000) NULL,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                "CreatedAtUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                "CreatedByUserId" uuid NULL,
                "UpdatedAtUtc" timestamptz NULL,
                "UpdatedByUserId" uuid NULL,
                "DeletedAtUtc" timestamptz NULL,
                "DeletedByUserId" uuid NULL
            );

            CREATE TABLE IF NOT EXISTS hr_advance_requests (
                "Id" uuid PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "PersonnelId" uuid NOT NULL,
                "ProjectId" uuid NULL,
                "RequestDate" date NOT NULL,
                "RequestedAmount" numeric(18,2) NOT NULL,
                "ApprovedAmount" numeric(18,2) NOT NULL DEFAULT 0,
                "CurrencyCode" varchar(3) NOT NULL DEFAULT 'TRY',
                "DeductionInstallmentCount" integer NOT NULL DEFAULT 1,
                "FirstDeductionDate" date NULL,
                "Reason" varchar(1000) NOT NULL,
                "Status" integer NOT NULL,
                "ApprovedByUserId" uuid NULL,
                "ApprovedAtUtc" timestamptz NULL,
                "PaidAtUtc" timestamptz NULL,
                "PaymentReference" varchar(150) NULL,
                "ApprovalNote" varchar(1000) NULL,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                "CreatedAtUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                "CreatedByUserId" uuid NULL,
                "UpdatedAtUtc" timestamptz NULL,
                "UpdatedByUserId" uuid NULL,
                "DeletedAtUtc" timestamptz NULL,
                "DeletedByUserId" uuid NULL
            );

            CREATE TABLE IF NOT EXISTS hr_payroll_records (
                "Id" uuid PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "PersonnelId" uuid NOT NULL,
                "Year" integer NOT NULL,
                "Month" integer NOT NULL,
                "GrossSalary" numeric(18,2) NOT NULL DEFAULT 0,
                "NormalWorkAmount" numeric(18,2) NOT NULL DEFAULT 0,
                "OvertimeAmount" numeric(18,2) NOT NULL DEFAULT 0,
                "SundayWorkAmount" numeric(18,2) NOT NULL DEFAULT 0,
                "PublicHolidayAmount" numeric(18,2) NOT NULL DEFAULT 0,
                "BonusAmount" numeric(18,2) NOT NULL DEFAULT 0,
                "MealAmount" numeric(18,2) NOT NULL DEFAULT 0,
                "TravelAmount" numeric(18,2) NOT NULL DEFAULT 0,
                "OtherEarningAmount" numeric(18,2) NOT NULL DEFAULT 0,
                "CompensationAmount" numeric(18,2) NOT NULL DEFAULT 0,
                "TotalEarnings" numeric(18,2) NOT NULL DEFAULT 0,
                "SgkEmployeeDeduction" numeric(18,2) NOT NULL DEFAULT 0,
                "IncomeTaxDeduction" numeric(18,2) NOT NULL DEFAULT 0,
                "StampTaxDeduction" numeric(18,2) NOT NULL DEFAULT 0,
                "AdvanceDeduction" numeric(18,2) NOT NULL DEFAULT 0,
                "OtherDeductionAmount" numeric(18,2) NOT NULL DEFAULT 0,
                "TotalDeductions" numeric(18,2) NOT NULL DEFAULT 0,
                "OfficialNetPayableAmount" numeric(18,2) NOT NULL DEFAULT 0,
                "ActualPayableAmount" numeric(18,2) NOT NULL DEFAULT 0,
                "NetPayableAmount" numeric(18,2) NOT NULL DEFAULT 0,
                "CurrencyCode" varchar(3) NOT NULL DEFAULT 'TRY',
                "Status" integer NOT NULL DEFAULT 0,
                "ApprovedAtUtc" timestamptz NULL,
                "ApprovedByUserId" uuid NULL,
                "PaidAtUtc" timestamptz NULL,
                "PaymentReference" varchar(150) NULL,
                "Description" varchar(2000) NULL,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                "CreatedAtUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                "CreatedByUserId" uuid NULL,
                "UpdatedAtUtc" timestamptz NULL,
                "UpdatedByUserId" uuid NULL,
                "DeletedAtUtc" timestamptz NULL,
                "DeletedByUserId" uuid NULL
            );

            CREATE INDEX IF NOT EXISTS "IX_hr_leave_requests_CompanyId_Status_StartDate"
                ON hr_leave_requests ("CompanyId", "Status", "StartDate");
            CREATE INDEX IF NOT EXISTS "IX_hr_overtime_requests_CompanyId_Status_WorkDate"
                ON hr_overtime_requests ("CompanyId", "Status", "WorkDate");
            CREATE INDEX IF NOT EXISTS "IX_hr_advance_requests_CompanyId_Status_RequestDate"
                ON hr_advance_requests ("CompanyId", "Status", "RequestDate");
            CREATE INDEX IF NOT EXISTS "IX_hr_payroll_records_CompanyId_Status_Year_Month"
                ON hr_payroll_records ("CompanyId", "Status", "Year", "Month");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_hr_payroll_records_CompanyId_PersonnelId_Year_Month"
                ON hr_payroll_records ("CompanyId", "PersonnelId", "Year", "Month")
                WHERE "IsDeleted" = FALSE;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS hr_payroll_records;
            DROP TABLE IF EXISTS hr_advance_requests;
            DROP TABLE IF EXISTS hr_overtime_requests;
            DROP TABLE IF EXISTS hr_leave_requests;
            """);
    }
}
