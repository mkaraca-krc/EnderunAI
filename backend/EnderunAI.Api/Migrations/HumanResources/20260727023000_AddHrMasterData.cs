using EnderunAI.Api.Data.HumanResources;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EnderunAI.Api.Migrations.HumanResources;

[DbContext(typeof(HrDbContext))]
[Migration("20260727023000_AddHrMasterData")]
public sealed class AddHrMasterData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS hr_salary_definitions (
                "Id" uuid PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "PersonnelId" uuid NOT NULL,
                "EffectiveStartDate" date NOT NULL,
                "EffectiveEndDate" date NULL,
                "GrossSalary" numeric(18,2) NOT NULL DEFAULT 0,
                "NetSalary" numeric(18,2) NOT NULL DEFAULT 0,
                "DailyRate" numeric(18,2) NOT NULL DEFAULT 0,
                "HourlyRate" numeric(18,2) NOT NULL DEFAULT 0,
                "OvertimeMultiplier" numeric(8,4) NOT NULL DEFAULT 1.5,
                "SundayMultiplier" numeric(8,4) NOT NULL DEFAULT 2,
                "PublicHolidayMultiplier" numeric(8,4) NOT NULL DEFAULT 2,
                "CurrencyCode" varchar(3) NOT NULL DEFAULT 'TRY',
                "Description" varchar(1000) NULL,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                "CreatedAtUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                "CreatedByUserId" uuid NULL,
                "UpdatedAtUtc" timestamptz NULL,
                "UpdatedByUserId" uuid NULL,
                "DeletedAtUtc" timestamptz NULL,
                "DeletedByUserId" uuid NULL
            );

            CREATE TABLE IF NOT EXISTS hr_departments (
                "Id" uuid PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "Code" varchar(40) NOT NULL,
                "Name" varchar(200) NOT NULL,
                "ParentDepartmentId" uuid NULL,
                "ManagerPersonnelId" uuid NULL,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                "CreatedAtUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                "CreatedByUserId" uuid NULL,
                "UpdatedAtUtc" timestamptz NULL,
                "UpdatedByUserId" uuid NULL,
                "DeletedAtUtc" timestamptz NULL,
                "DeletedByUserId" uuid NULL
            );

            CREATE TABLE IF NOT EXISTS hr_positions (
                "Id" uuid PRIMARY KEY,
                "DepartmentId" uuid NOT NULL,
                "Code" varchar(40) NOT NULL,
                "Title" varchar(200) NOT NULL,
                "Description" varchar(1000) NULL,
                "IsManagerial" boolean NOT NULL DEFAULT FALSE,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                "CreatedAtUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                "CreatedByUserId" uuid NULL,
                "UpdatedAtUtc" timestamptz NULL,
                "UpdatedByUserId" uuid NULL,
                "DeletedAtUtc" timestamptz NULL,
                "DeletedByUserId" uuid NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_hr_salary_definitions_Personnel_Start"
                ON hr_salary_definitions ("PersonnelId", "EffectiveStartDate")
                WHERE "IsDeleted" = FALSE;
            CREATE INDEX IF NOT EXISTS "IX_hr_salary_definitions_Company_Personnel"
                ON hr_salary_definitions ("CompanyId", "PersonnelId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_hr_departments_Company_Code"
                ON hr_departments ("CompanyId", "Code")
                WHERE "IsDeleted" = FALSE;
            CREATE INDEX IF NOT EXISTS "IX_hr_departments_Parent"
                ON hr_departments ("ParentDepartmentId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_hr_positions_Department_Code"
                ON hr_positions ("DepartmentId", "Code")
                WHERE "IsDeleted" = FALSE;
            CREATE INDEX IF NOT EXISTS "IX_hr_positions_Department"
                ON hr_positions ("DepartmentId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS hr_positions;
            DROP TABLE IF EXISTS hr_departments;
            DROP TABLE IF EXISTS hr_salary_definitions;
            """);
    }
}
