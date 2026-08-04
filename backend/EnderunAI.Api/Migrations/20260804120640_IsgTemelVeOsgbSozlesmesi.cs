using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class IsgTemelVeOsgbSozlesmesi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PersonnelId",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "isg_osgb_contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BillingType = table.Column<int>(type: "integer", nullable: false),
                    MonthlyFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PerPersonFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_isg_osgb_contracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_isg_osgb_contracts_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_isg_osgb_contracts_current_accounts_CurrentAccountId",
                        column: x => x.CurrentAccountId,
                        principalTable: "current_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "isg_osgb_contract_experts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IsgOsgbContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpertType = table.Column<int>(type: "integer", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CertificateNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ExpertClass = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_isg_osgb_contract_experts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_isg_osgb_contract_experts_isg_osgb_contracts_IsgOsgbContrac~",
                        column: x => x.IsgOsgbContractId,
                        principalTable: "isg_osgb_contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_PersonnelId",
                table: "users",
                column: "PersonnelId",
                unique: true,
                filter: "\"PersonnelId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_isg_osgb_contract_experts_IsgOsgbContractId",
                table: "isg_osgb_contract_experts",
                column: "IsgOsgbContractId");

            migrationBuilder.CreateIndex(
                name: "IX_isg_osgb_contracts_CompanyId_ContractNumber",
                table: "isg_osgb_contracts",
                columns: new[] { "CompanyId", "ContractNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_isg_osgb_contracts_CompanyId_StartDate",
                table: "isg_osgb_contracts",
                columns: new[] { "CompanyId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_isg_osgb_contracts_CurrentAccountId",
                table: "isg_osgb_contracts",
                column: "CurrentAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_users_personnel_PersonnelId",
                table: "users",
                column: "PersonnelId",
                principalTable: "personnel",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_personnel_PersonnelId",
                table: "users");

            migrationBuilder.DropTable(
                name: "isg_osgb_contract_experts");

            migrationBuilder.DropTable(
                name: "isg_osgb_contracts");

            migrationBuilder.DropIndex(
                name: "IX_users_PersonnelId",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PersonnelId",
                table: "users");
        }
    }
}
