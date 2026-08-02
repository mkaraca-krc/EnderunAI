using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHrCompensationComponents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_compensation_components",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonnelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ComponentType = table.Column<int>(type: "integer", nullable: false),
                    CalculationType = table.Column<int>(type: "integer", nullable: false),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    EffectiveStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsAttendanceBased = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeInPayroll = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeInSgkBase = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeInIncomeTaxBase = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeInStampTaxBase = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeInProjectCost = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeInProgressPaymentCost = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_hr_compensation_components", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_hr_compensation_components_CompanyId_PersonnelId_ProjectId_~",
                table: "hr_compensation_components",
                columns: new[] { "CompanyId", "PersonnelId", "ProjectId", "Code", "EffectiveStartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_hr_compensation_components_PersonnelId_IsActive_EffectiveSt~",
                table: "hr_compensation_components",
                columns: new[] { "PersonnelId", "IsActive", "EffectiveStartDate", "EffectiveEndDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_compensation_components");
        }
    }
}
