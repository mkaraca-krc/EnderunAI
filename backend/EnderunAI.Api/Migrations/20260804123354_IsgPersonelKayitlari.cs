using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class IsgPersonelKayitlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "isg_certificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonnelId = table.Column<Guid>(type: "uuid", nullable: false),
                    CertificateType = table.Column<int>(type: "integer", nullable: false),
                    CustomTypeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CertificateNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IssuedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DocumentPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_isg_certificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_isg_certificates_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_isg_certificates_personnel_PersonnelId",
                        column: x => x.PersonnelId,
                        principalTable: "personnel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "isg_health_reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonnelId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsgOsgbContractId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportType = table.Column<int>(type: "integer", nullable: false),
                    ExamDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    DoctorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Restrictions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DoctorNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DocumentPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_isg_health_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_isg_health_reports_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_isg_health_reports_isg_osgb_contracts_IsgOsgbContractId",
                        column: x => x.IsgOsgbContractId,
                        principalTable: "isg_osgb_contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_isg_health_reports_personnel_PersonnelId",
                        column: x => x.PersonnelId,
                        principalTable: "personnel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "isg_trainings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonnelId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsgOsgbContractId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrainingType = table.Column<int>(type: "integer", nullable: false),
                    Topic = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TrainingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DurationHours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    TrainerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DocumentPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_isg_trainings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_isg_trainings_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_isg_trainings_isg_osgb_contracts_IsgOsgbContractId",
                        column: x => x.IsgOsgbContractId,
                        principalTable: "isg_osgb_contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_isg_trainings_personnel_PersonnelId",
                        column: x => x.PersonnelId,
                        principalTable: "personnel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_isg_certificates_CompanyId_ExpiryDate",
                table: "isg_certificates",
                columns: new[] { "CompanyId", "ExpiryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_isg_certificates_PersonnelId_CertificateType",
                table: "isg_certificates",
                columns: new[] { "PersonnelId", "CertificateType" });

            migrationBuilder.CreateIndex(
                name: "IX_isg_health_reports_CompanyId_ValidUntil",
                table: "isg_health_reports",
                columns: new[] { "CompanyId", "ValidUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_isg_health_reports_IsgOsgbContractId",
                table: "isg_health_reports",
                column: "IsgOsgbContractId");

            migrationBuilder.CreateIndex(
                name: "IX_isg_health_reports_PersonnelId_ExamDate",
                table: "isg_health_reports",
                columns: new[] { "PersonnelId", "ExamDate" });

            migrationBuilder.CreateIndex(
                name: "IX_isg_trainings_CompanyId_ValidUntil",
                table: "isg_trainings",
                columns: new[] { "CompanyId", "ValidUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_isg_trainings_IsgOsgbContractId",
                table: "isg_trainings",
                column: "IsgOsgbContractId");

            migrationBuilder.CreateIndex(
                name: "IX_isg_trainings_PersonnelId_TrainingDate",
                table: "isg_trainings",
                columns: new[] { "PersonnelId", "TrainingDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "isg_certificates");

            migrationBuilder.DropTable(
                name: "isg_health_reports");

            migrationBuilder.DropTable(
                name: "isg_trainings");
        }
    }
}
