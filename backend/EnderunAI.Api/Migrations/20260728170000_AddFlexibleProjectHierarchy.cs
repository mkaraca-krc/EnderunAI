using System;
using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260728170000_AddFlexibleProjectHierarchy")]
public partial class AddFlexibleProjectHierarchy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "project_hierarchy_levels",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false),
                Name = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: false),
                SortOrder = table.Column<int>(
                    type: "integer",
                    nullable: false),
                IsRequired = table.Column<bool>(
                    type: "boolean",
                    nullable: false),
                IsActive = table.Column<bool>(
                    type: "boolean",
                    nullable: false),
                IsDeleted = table.Column<bool>(
                    type: "boolean",
                    nullable: false),
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
                table.PrimaryKey(
                    "PK_project_hierarchy_levels",
                    x => x.Id);
                table.ForeignKey(
                    name: "FK_project_hierarchy_levels_projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "project_hierarchy_nodes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                LevelId = table.Column<Guid>(type: "uuid", nullable: false),
                ParentNodeId = table.Column<Guid>(
                    type: "uuid",
                    nullable: true),
                Code = table.Column<string>(
                    type: "character varying(60)",
                    maxLength: 60,
                    nullable: false),
                Name = table.Column<string>(
                    type: "character varying(200)",
                    maxLength: 200,
                    nullable: false),
                Description = table.Column<string>(
                    type: "character varying(1000)",
                    maxLength: 1000,
                    nullable: true),
                SortOrder = table.Column<int>(
                    type: "integer",
                    nullable: false),
                IsActive = table.Column<bool>(
                    type: "boolean",
                    nullable: false),
                IsDeleted = table.Column<bool>(
                    type: "boolean",
                    nullable: false),
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
                table.PrimaryKey(
                    "PK_project_hierarchy_nodes",
                    x => x.Id);
                table.ForeignKey(
                    name: "FK_project_hierarchy_nodes_project_hierarchy_levels_LevelId",
                    column: x => x.LevelId,
                    principalTable: "project_hierarchy_levels",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_project_hierarchy_nodes_project_hierarchy_nodes_ParentNodeId",
                    column: x => x.ParentNodeId,
                    principalTable: "project_hierarchy_nodes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_project_hierarchy_nodes_projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "project_module_scopes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectHierarchyNodeId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                ModuleType = table.Column<int>(
                    type: "integer",
                    nullable: false),
                RecordId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                IsActive = table.Column<bool>(
                    type: "boolean",
                    nullable: false),
                IsDeleted = table.Column<bool>(
                    type: "boolean",
                    nullable: false),
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
                table.PrimaryKey(
                    "PK_project_module_scopes",
                    x => x.Id);
                table.ForeignKey(
                    name: "FK_project_module_scopes_project_hierarchy_nodes_ProjectHierarchyNodeId",
                    column: x => x.ProjectHierarchyNodeId,
                    principalTable: "project_hierarchy_nodes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_project_module_scopes_projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_project_hierarchy_levels_ProjectId_Code",
            table: "project_hierarchy_levels",
            columns: new[] { "ProjectId", "Code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_project_hierarchy_levels_ProjectId_SortOrder",
            table: "project_hierarchy_levels",
            columns: new[] { "ProjectId", "SortOrder" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_project_hierarchy_nodes_LevelId",
            table: "project_hierarchy_nodes",
            column: "LevelId");

        migrationBuilder.CreateIndex(
            name: "IX_project_hierarchy_nodes_ParentNodeId",
            table: "project_hierarchy_nodes",
            column: "ParentNodeId");

        migrationBuilder.CreateIndex(
            name: "IX_project_hierarchy_nodes_ProjectId_Code",
            table: "project_hierarchy_nodes",
            columns: new[] { "ProjectId", "Code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_project_hierarchy_nodes_ProjectId_ParentNodeId_LevelId_SortOrder",
            table: "project_hierarchy_nodes",
            columns: new[]
            {
                "ProjectId",
                "ParentNodeId",
                "LevelId",
                "SortOrder"
            });

        migrationBuilder.CreateIndex(
            name: "IX_project_module_scopes_ProjectHierarchyNodeId",
            table: "project_module_scopes",
            column: "ProjectHierarchyNodeId");

        migrationBuilder.CreateIndex(
            name: "IX_project_module_scopes_ProjectId_ModuleType_RecordId",
            table: "project_module_scopes",
            columns: new[] { "ProjectId", "ModuleType", "RecordId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "project_module_scopes");
        migrationBuilder.DropTable(name: "project_hierarchy_nodes");
        migrationBuilder.DropTable(name: "project_hierarchy_levels");
    }
}
