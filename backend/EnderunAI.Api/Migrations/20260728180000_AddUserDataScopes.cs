using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations;

public partial class AddUserDataScopes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_data_scopes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ScopeType = table.Column<int>(type: "integer", nullable: false),
                CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false),
                CreatedByUserId = table.Column<Guid>(
                    type: "uuid",
                    nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true),
                UpdatedByUserId = table.Column<Guid>(
                    type: "uuid",
                    nullable: true),
                DeletedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true),
                DeletedByUserId = table.Column<Guid>(
                    type: "uuid",
                    nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_data_scopes", x => x.Id);
                table.ForeignKey(
                    name: "FK_user_data_scopes_branches_BranchId",
                    column: x => x.BranchId,
                    principalTable: "branches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_user_data_scopes_companies_CompanyId",
                    column: x => x.CompanyId,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_user_data_scopes_projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_user_data_scopes_users_UserId",
                    column: x => x.UserId,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_user_data_scopes_BranchId",
            table: "user_data_scopes",
            column: "BranchId");

        migrationBuilder.CreateIndex(
            name: "IX_user_data_scopes_CompanyId",
            table: "user_data_scopes",
            column: "CompanyId");

        migrationBuilder.CreateIndex(
            name: "IX_user_data_scopes_ProjectId",
            table: "user_data_scopes",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_user_data_scopes_UserId_ScopeType_CompanyId_BranchId_ProjectId",
            table: "user_data_scopes",
            columns:
            [
                "UserId",
                "ScopeType",
                "CompanyId",
                "BranchId",
                "ProjectId"
            ],
            unique: true);

        migrationBuilder.Sql(
            """
            INSERT INTO user_data_scopes (
                "Id", "UserId", "ScopeType", "IsActive", "IsDeleted", "CreatedAtUtc")
            SELECT
                md5("Id"::text || ':legacy-all-scope')::uuid,
                "Id",
                0,
                TRUE,
                FALSE,
                NOW()
            FROM users;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "user_data_scopes");
    }
}
