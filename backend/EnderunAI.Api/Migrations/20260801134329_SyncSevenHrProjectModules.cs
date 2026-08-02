using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class SyncSevenHrProjectModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill/sync migration: WorkTasks, attendance_records,
            // hr_compensation_components, hr_career_histories,
            // hr_shift_definitions, hr_shift_assignments,
            // hr_asset_assignments, project_measurements and
            // project_measurement_items all already exist in this database
            // (created by earlier orphaned migrations whose model classes
            // were never added to AppDbContext, so the snapshot never knew
            // about them). This migration exists only to bring the snapshot
            // back in sync with the actual schema; it must not attempt to
            // recreate tables that already exist, so it is intentionally a
            // no-op. No new columns were needed — every model property
            // matched an existing column exactly.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — see Up().
        }
    }
}
