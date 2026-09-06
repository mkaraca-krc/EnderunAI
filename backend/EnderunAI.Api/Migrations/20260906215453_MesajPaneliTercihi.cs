using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class MesajPaneliTercihi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LastConversationId",
                table: "user_ui_preferences",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MessagePanelOpen",
                table: "user_ui_preferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastConversationId",
                table: "user_ui_preferences");

            migrationBuilder.DropColumn(
                name: "MessagePanelOpen",
                table: "user_ui_preferences");
        }
    }
}
