using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceDifferenceCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "price_difference_index_periods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    SourceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PeriodLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LaborIndex = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    FuelIndex = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    MaterialIndex = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    MachineryIndex = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    CementIndex = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    OtherIndex = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    CopperIndex = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    SteelIndex = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    ElectricityIndex = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    UsdRate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    EurRate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
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
                    table.PrimaryKey("PK_price_difference_index_periods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "price_difference_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CalculationType = table.Column<int>(type: "integer", nullable: false),
                    BaseYear = table.Column<int>(type: "integer", nullable: false),
                    BaseMonth = table.Column<int>(type: "integer", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsVatIncluded = table.Column<bool>(type: "boolean", nullable: false),
                    FormulaName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
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
                    table.PrimaryKey("PK_price_difference_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_price_difference_profiles_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_price_difference_profiles_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "price_difference_coefficients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceDifferenceProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    A = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    B1 = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    B2 = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    B3 = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    B4 = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    B5 = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    C = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
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
                    table.PrimaryKey("PK_price_difference_coefficients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_price_difference_coefficients_price_difference_profiles_Pri~",
                        column: x => x.PriceDifferenceProfileId,
                        principalTable: "price_difference_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "progress_payment_price_differences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgressPaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceDifferenceProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseIndexPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentIndexPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Pn = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    Delta = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    PriceDifferenceAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CalculationSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("PK_progress_payment_price_differences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_progress_payment_price_differences_price_difference_index_p~",
                        column: x => x.BaseIndexPeriodId,
                        principalTable: "price_difference_index_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_progress_payment_price_differences_price_difference_index_~1",
                        column: x => x.CurrentIndexPeriodId,
                        principalTable: "price_difference_index_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_progress_payment_price_differences_price_difference_profile~",
                        column: x => x.PriceDifferenceProfileId,
                        principalTable: "price_difference_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_progress_payment_price_differences_progress_payments_Progre~",
                        column: x => x.ProgressPaymentId,
                        principalTable: "progress_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_price_difference_coefficients_PriceDifferenceProfileId",
                table: "price_difference_coefficients",
                column: "PriceDifferenceProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_price_difference_index_periods_Year_Month_SourceName",
                table: "price_difference_index_periods",
                columns: new[] { "Year", "Month", "SourceName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_price_difference_profiles_CompanyId",
                table: "price_difference_profiles",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_price_difference_profiles_ProjectId_IsDefault",
                table: "price_difference_profiles",
                columns: new[] { "ProjectId", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_price_difference_profiles_ProjectId_ProfileName",
                table: "price_difference_profiles",
                columns: new[] { "ProjectId", "ProfileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_price_differences_BaseIndexPeriodId",
                table: "progress_payment_price_differences",
                column: "BaseIndexPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_price_differences_CurrentIndexPeriodId",
                table: "progress_payment_price_differences",
                column: "CurrentIndexPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_price_differences_PriceDifferenceProfileId",
                table: "progress_payment_price_differences",
                column: "PriceDifferenceProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_progress_payment_price_differences_ProgressPaymentId",
                table: "progress_payment_price_differences",
                column: "ProgressPaymentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "price_difference_coefficients");

            migrationBuilder.DropTable(
                name: "progress_payment_price_differences");

            migrationBuilder.DropTable(
                name: "price_difference_index_periods");

            migrationBuilder.DropTable(
                name: "price_difference_profiles");
        }
    }
}
