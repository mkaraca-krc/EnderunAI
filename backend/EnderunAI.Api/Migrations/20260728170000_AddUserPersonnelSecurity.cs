using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations;

public partial class AddUserPersonnelSecurity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "MustChangePassword",
            table: "users",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "PasswordChangedAtUtc",
            table: "users",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "PersonnelId",
            table: "users",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SecurityStamp",
            table: "users",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "");

        migrationBuilder.Sql(
            """
            UPDATE users
            SET "SecurityStamp" =
                md5(random()::text || clock_timestamp()::text || "Id"::text)
            WHERE "SecurityStamp" = '';
            """);

        migrationBuilder.CreateIndex(
            name: "IX_users_PersonnelId",
            table: "users",
            column: "PersonnelId",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_users_personnel_PersonnelId",
            table: "users",
            column: "PersonnelId",
            principalTable: "personnel",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_users_personnel_PersonnelId",
            table: "users");

        migrationBuilder.DropIndex(
            name: "IX_users_PersonnelId",
            table: "users");

        migrationBuilder.DropColumn(
            name: "MustChangePassword",
            table: "users");

        migrationBuilder.DropColumn(
            name: "PasswordChangedAtUtc",
            table: "users");

        migrationBuilder.DropColumn(
            name: "PersonnelId",
            table: "users");

        migrationBuilder.DropColumn(
            name: "SecurityStamp",
            table: "users");
    }
}
