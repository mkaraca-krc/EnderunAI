using EnderunAI.Api.Data.HumanResources;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EnderunAI.Api.Migrations.HumanResources;

[DbContext(typeof(HrDbContext))]
[Migration("20260727093000_AlignHrPositionsLegacySchema")]
public sealed class AlignHrPositionsLegacySchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE public.hr_positions
                ADD COLUMN IF NOT EXISTS "CompanyId" uuid NULL,
                ADD COLUMN IF NOT EXISTS "Name" varchar(200) NULL,
                ADD COLUMN IF NOT EXISTS "Level" integer NULL;

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'hr_positions'
                      AND column_name = 'Title'
                ) THEN
                    EXECUTE 'UPDATE public.hr_positions
                             SET "Name" = COALESCE("Name", "Title")
                             WHERE "Name" IS NULL';
                    EXECUTE 'ALTER TABLE public.hr_positions
                             ALTER COLUMN "Title" DROP NOT NULL';
                END IF;
            END
            $$;

            UPDATE public.hr_positions
            SET "Name" = COALESCE(NULLIF("Name", ''), NULLIF("Code", ''), 'Pozisyon')
            WHERE "Name" IS NULL OR "Name" = '';

            UPDATE public.hr_positions
            SET "Level" = 0
            WHERE "Level" IS NULL;

            UPDATE public.hr_positions AS position
            SET "CompanyId" = department."CompanyId"
            FROM public.hr_departments AS department
            WHERE position."CompanyId" IS NULL
              AND position."DepartmentId" = department."Id";

            ALTER TABLE public.hr_positions
                ALTER COLUMN "Name" SET NOT NULL,
                ALTER COLUMN "Level" SET DEFAULT 0,
                ALTER COLUMN "Level" SET NOT NULL;

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM public.hr_positions
                    WHERE "CompanyId" IS NULL
                ) THEN
                    ALTER TABLE public.hr_positions
                        ALTER COLUMN "CompanyId" SET NOT NULL;
                END IF;
            END
            $$;

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_hr_positions_Company_Code"
                ON public.hr_positions ("CompanyId", "Code")
                WHERE "IsDeleted" = FALSE;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Compatibility migration preserves legacy columns and data on rollback.
    }
}
