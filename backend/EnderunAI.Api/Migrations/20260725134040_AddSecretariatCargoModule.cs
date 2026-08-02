using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSecretariatCargoModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cargo_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CargoCompany = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SenderName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    RecipientName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    InstitutionName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CargoDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpectedDeliveryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredToName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_cargo_records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cargo_records_CompanyId_Direction_CargoDate",
                table: "cargo_records",
                columns: new[] { "CompanyId", "Direction", "CargoDate" });

            migrationBuilder.CreateIndex(
                name: "IX_cargo_records_CompanyId_TrackingNumber",
                table: "cargo_records",
                columns: new[] { "CompanyId", "TrackingNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cargo_records_ProjectId_Status",
                table: "cargo_records",
                columns: new[] { "ProjectId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cargo_records");
        }
    }
}
