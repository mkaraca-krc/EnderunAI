using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class OdemePlaniCekirdegi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "odeme_planlari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    HaftaBaslangici = table.Column<DateTime>(type: "date", nullable: false),
                    OdemeGunu = table.Column<DateTime>(type: "date", nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    HazirlayanUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OnayaSunulmaAnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OnaylayanUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OnaylanmaAnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    KapanmaAnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_odeme_planlari", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "plan_disi_odemeler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OdemeTarihi = table.Column<DateTime>(type: "date", nullable: false),
                    CashAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sebep = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ListelendigiHafta = table.Column<DateTime>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_plan_disi_odemeler", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "odeme_plani_hesap_bakiyeleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OdemePlaniId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    GosterilenBakiye = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Kaynak = table.Column<int>(type: "integer", nullable: false),
                    OlcumAnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OlcenUserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_odeme_plani_hesap_bakiyeleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_odeme_plani_hesap_bakiyeleri_odeme_planlari_OdemePlaniId",
                        column: x => x.OdemePlaniId,
                        principalTable: "odeme_planlari",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "odeme_plani_satirlari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OdemePlaniId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OnerilenTutar = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Yontem = table.Column<int>(type: "integer", nullable: false),
                    CekVadesi = table.Column<DateTime>(type: "date", nullable: true),
                    Oncelik = table.Column<int>(type: "integer", nullable: false),
                    CashAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Aciklama = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Karar = table.Column<int>(type: "integer", nullable: false),
                    KararVerenUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    KararAnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OnaylananTutar = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    OnayliCurrentAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    OnayliTutar = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    OnayliYontem = table.Column<int>(type: "integer", nullable: true),
                    OnayliCekVadesi = table.Column<DateTime>(type: "date", nullable: true),
                    OnayliOncelik = table.Column<int>(type: "integer", nullable: true),
                    OnayliCashAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    OdemeDurumu = table.Column<int>(type: "integer", nullable: false),
                    OdenenTutar = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UretilenChequeId = table.Column<Guid>(type: "uuid", nullable: true),
                    DevrededenSatirId = table.Column<Guid>(type: "uuid", nullable: true),
                    DevirHaftaSayisi = table.Column<int>(type: "integer", nullable: false),
                    KapanisSebebi = table.Column<int>(type: "integer", nullable: true),
                    KapanisAciklamasi = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SupplierInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_odeme_plani_satirlari", x => x.Id);
                    table.ForeignKey(
                        name: "FK_odeme_plani_satirlari_odeme_planlari_OdemePlaniId",
                        column: x => x.OdemePlaniId,
                        principalTable: "odeme_planlari",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_odeme_plani_hesap_bakiyeleri_OdemePlaniId_CashAccountId",
                table: "odeme_plani_hesap_bakiyeleri",
                columns: new[] { "OdemePlaniId", "CashAccountId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_odeme_plani_satirlari_CurrentAccountId",
                table: "odeme_plani_satirlari",
                column: "CurrentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_odeme_plani_satirlari_OdemePlaniId_Oncelik",
                table: "odeme_plani_satirlari",
                columns: new[] { "OdemePlaniId", "Oncelik" });

            migrationBuilder.CreateIndex(
                name: "IX_odeme_planlari_CompanyId_HaftaBaslangici",
                table: "odeme_planlari",
                columns: new[] { "CompanyId", "HaftaBaslangici" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_plan_disi_odemeler_CompanyId_OdemeTarihi",
                table: "plan_disi_odemeler",
                columns: new[] { "CompanyId", "OdemeTarihi" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "odeme_plani_hesap_bakiyeleri");

            migrationBuilder.DropTable(
                name: "odeme_plani_satirlari");

            migrationBuilder.DropTable(
                name: "plan_disi_odemeler");

            migrationBuilder.DropTable(
                name: "odeme_planlari");
        }
    }
}
