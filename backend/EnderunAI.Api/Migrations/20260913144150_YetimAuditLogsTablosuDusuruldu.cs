using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <summary>
    /// YETİM TABLO DÜŞÜRÜLDÜ: `audit_logs`.
    ///
    /// ═══ NEDEN (ölçüldü 2026-09-13) ═══
    ///
    /// Tablo `20260720083747_AddAuditLogInfrastructure` ile açıldı ve iki
    /// ay boyunca **TEK SATIR BİLE** yazılmadı. Sebebi ölçüldü:
    ///
    ///   · `AuditLog` diye bir varlık sınıfı YOK (dosya araması boş)
    ///   · `DbSet` YOK
    ///   · `AuditLogs.Add` / `new AuditLog` → 0 eşleşme
    ///   · `AppDbContextModelSnapshot` tabloyu HİÇ TANIMIYOR
    ///   · `pg_stat_user_tables.n_tup_ins = 0`, `stats_reset = HİÇ`
    ///
    /// Yani göç tabloyu kurmuş, model sonradan varlığı kaybetmiş, tablo
    /// canlıda kalmış. `sema-sapma-kapisi.sh`ın saydığı "modelin
    /// bilmediği nesne" sınıfının ta kendisi: üç indeksi o 95'in içinde.
    ///
    /// ═══ NEDEN TEHLİKELİ (asıl gerekçe) ═══
    ///
    /// Tek başına boş bir tablo zararsızdır. Tehlikeli olan, yanında
    /// duran `audit-log.view` izniyle birlikte kurduğu TUZAK:
    /// izin açıklaması "Sistem denetim kayıtlarını (kim ne yaptı)
    /// görüntüler" diyor ve 2 role verilmiş. Gelecekte biri "denetim
    /// kaydımız var, işte tablo" der ve BOŞ TABLOYA bakar.
    ///
    /// Gerçek denetim `security_audit_events`te ve çalışıyor; ekran da
    /// onu okuyor. Bu tablo yalnızca yanlış yere bakılmasına yol açar.
    ///
    /// ═══ VERİ KAYBI YOK ═══
    ///
    /// 0 satır (sayılarak), 0 ekleme (istatistikten, sayaç hiç
    /// sıfırlanmamış). `Down` tabloyu birebir geri kuruyor — ama geri
    /// kurulan şey yine boş bir tablo olur; veri geri gelmez çünkü hiç
    /// olmadı.
    /// </summary>
    public partial class YetimAuditLogsTablosuDusuruldu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "audit_logs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ÜÇ İNDEKSİYLE BİRLİKTE BİREBİR GERİ KURULUYOR —
            // `20260720083747_AddAuditLogInfrastructure` ile aynı tanım.
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    OldValuesJson = table.Column<string>(type: "jsonb", nullable: true),
                    NewValuesJson = table.Column<string>(type: "jsonb", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_CompanyId_CreatedAtUtc",
                table: "audit_logs",
                columns: new[] { "CompanyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_DocumentNumber_CreatedAtUtc",
                table: "audit_logs",
                columns: new[] { "DocumentNumber", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_EntityType_EntityId",
                table: "audit_logs",
                columns: new[] { "EntityType", "EntityId" });
        }
    }
}
