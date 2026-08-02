using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHrRecruitmentModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // hr_job_postings, hr_job_candidates, hr_job_applications and
            // hr_candidate_interviews all already exist in this database
            // (created by an earlier orphaned migration whose model classes
            // were never added to AppDbContext — the same "migration ahead
            // of code" gap found repeatedly today). This migration only
            // brings the snapshot back in sync; it must not recreate tables
            // that already exist, so it is intentionally a no-op.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — see Up().
        }
    }
}
