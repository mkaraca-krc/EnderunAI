using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class SyncSecurityAuditEventModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // security_audit_events already exists (created by
            // 20260801131244_AddSecurityAuditEventsTable for the raw-SQL
            // usage in ProcurementApprovalService). This migration only
            // adds the EF model/DbSet for it so the interceptor-based
            // audit log can use it too; it must not recreate the table.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — see Up().
        }
    }
}
