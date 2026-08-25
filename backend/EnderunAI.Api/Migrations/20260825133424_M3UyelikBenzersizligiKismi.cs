using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class M3UyelikBenzersizligiKismi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conversation_members_ConversationId_UserId",
                table: "conversation_members");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_members_aktif_benzersiz",
                table: "conversation_members",
                columns: new[] { "ConversationId", "UserId" },
                unique: true,
                filter: "\"LeftAtUtc\" IS NULL AND NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conversation_members_aktif_benzersiz",
                table: "conversation_members");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_members_ConversationId_UserId",
                table: "conversation_members",
                columns: new[] { "ConversationId", "UserId" },
                unique: true);
        }
    }
}
