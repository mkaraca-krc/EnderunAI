using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations;

public partial class AddSecurityAuditEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "security_audit_events",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                ActorUsername = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: true),
                Action = table.Column<string>(
                    type: "character varying(150)",
                    maxLength: 150,
                    nullable: false),
                EntityType = table.Column<string>(
                    type: "character varying(150)",
                    maxLength: 150,
                    nullable: false),
                EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                DetailsJson = table.Column<string>(
                    type: "jsonb",
                    nullable: true),
                IpAddress = table.Column<string>(
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: true),
                UserAgent = table.Column<string>(
                    type: "character varying(500)",
                    maxLength: 500,
                    nullable: true),
                OccurredAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_security_audit_events", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_security_audit_events_OccurredAtUtc",
            table: "security_audit_events",
            column: "OccurredAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_security_audit_events_ActorUserId_OccurredAtUtc",
            table: "security_audit_events",
            columns: ["ActorUserId", "OccurredAtUtc"]);

        migrationBuilder.CreateIndex(
            name: "IX_security_audit_events_EntityType_EntityId_OccurredAtUtc",
            table: "security_audit_events",
            columns: ["EntityType", "EntityId", "OccurredAtUtc"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "security_audit_events");
    }
}
