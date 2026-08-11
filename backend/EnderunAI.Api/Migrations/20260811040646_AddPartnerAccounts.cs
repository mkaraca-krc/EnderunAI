using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PartnerAccountId",
                table: "expense_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "partner_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partner_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_partner_accounts_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "partner_account_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartnerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    EntryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ExpenseEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partner_account_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_partner_account_entries_expense_entries_ExpenseEntryId",
                        column: x => x.ExpenseEntryId,
                        principalTable: "expense_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_partner_account_entries_partner_accounts_PartnerAccountId",
                        column: x => x.PartnerAccountId,
                        principalTable: "partner_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expense_entries_PartnerAccountId",
                table: "expense_entries",
                column: "PartnerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_partner_account_entries_ExpenseEntryId",
                table: "partner_account_entries",
                column: "ExpenseEntryId",
                unique: true,
                filter: "\"ExpenseEntryId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_partner_account_entries_PartnerAccountId_EntryDate",
                table: "partner_account_entries",
                columns: new[] { "PartnerAccountId", "EntryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_partner_accounts_CompanyId",
                table: "partner_accounts",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_expense_entries_partner_accounts_PartnerAccountId",
                table: "expense_entries",
                column: "PartnerAccountId",
                principalTable: "partner_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_expense_entries_partner_accounts_PartnerAccountId",
                table: "expense_entries");

            migrationBuilder.DropTable(
                name: "partner_account_entries");

            migrationBuilder.DropTable(
                name: "partner_accounts");

            migrationBuilder.DropIndex(
                name: "IX_expense_entries_PartnerAccountId",
                table: "expense_entries");

            migrationBuilder.DropColumn(
                name: "PartnerAccountId",
                table: "expense_entries");
        }
    }
}
