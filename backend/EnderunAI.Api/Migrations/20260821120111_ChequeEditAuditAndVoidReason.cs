using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class ChequeEditAuditAndVoidReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VoidReasonKind",
                table: "cheques",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VoidedFromClosedState",
                table: "cheques",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "cheque_change_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChequeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FieldLabel = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    OldValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AffectsAccounting = table.Column<bool>(type: "boolean", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_cheque_change_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cheque_change_logs_cheques_ChequeId",
                        column: x => x.ChequeId,
                        principalTable: "cheques",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cheque_change_logs_AffectsAccounting",
                table: "cheque_change_logs",
                column: "AffectsAccounting");

            migrationBuilder.CreateIndex(
                name: "IX_cheque_change_logs_ChequeId_ChangedAtUtc",
                table: "cheque_change_logs",
                columns: new[] { "ChequeId", "ChangedAtUtc" });

            /*
             * ÇEK YETKİLERİ VERİTABANINA AÇIKÇA YAZILIYOR.
             *
             * Tohumlayıcı da bu satırları yazıyor ama servis yeniden
             * başlayınca; migration doğrudan yazınca yetkiler şema ile
             * AYNI ANDA yerine oturuyor ve "ne zaman düştü" sorusu
             * migration geçmişinden cevaplanabiliyor.
             *
             * ON CONFLICT DO NOTHING: tohumlayıcı önce koşarsa çakışma
             * olmasın; migration tekrar koşarsa da bozulmasın.
             *
             * YALNIZ ÜÇ ROL: Admin, Genel Müdür, Finans Sorumlusu.
             * Diğer roller çeki görebilir ve normal akışını
             * yürütebilir ama geçmişe dönük düzeltme yapamaz, kapanmış
             * çeki iptal edemez.
             */
            migrationBuilder.Sql(
                """
                INSERT INTO permissions ("Id", "Key", "Module", "Name", "Description")
                VALUES
                  (gen_random_uuid(), 'cheque.edit', 'Finans', 'Çek Düzenleme',
                   'Portföydeki/yeni verilen çekin alanlarını düzeltir; tutar, para birimi ya da cari değişirse muhasebe fişi yeniden kesilir.'),
                  (gen_random_uuid(), 'cheque.void-closed', 'Finans', 'Çek — Kapanmış İptal',
                   'Tahsil edilmiş, ödenmiş, bankada/faktoringde, karşılıksız ya da iade alınmış çeki iptal eder. Gerçekleşmiş para hareketini storno ile geri alır ve çek numarasını yeniden kullanıma açar.')
                ON CONFLICT ("Key") DO NOTHING;

                INSERT INTO role_permissions ("RoleId", "PermissionId")
                SELECT r."Id", p."Id"
                FROM roles r
                CROSS JOIN permissions p
                WHERE r."Name" IN ('Admin', 'Genel Müdür', 'Finans Sorumlusu')
                  AND p."Key" IN ('cheque.edit', 'cheque.void-closed')
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Yetki satırları da geri alınıyor: migration geri sarılınca
            // rollerde açıklanamayan yetkiler kalmasın.
            migrationBuilder.Sql(
                """
                DELETE FROM role_permissions rp
                USING permissions p
                WHERE rp."PermissionId" = p."Id"
                  AND p."Key" IN ('cheque.edit', 'cheque.void-closed');

                DELETE FROM permissions
                WHERE "Key" IN ('cheque.edit', 'cheque.void-closed');
                """);

            migrationBuilder.DropTable(
                name: "cheque_change_logs");

            migrationBuilder.DropColumn(
                name: "VoidReasonKind",
                table: "cheques");

            migrationBuilder.DropColumn(
                name: "VoidedFromClosedState",
                table: "cheques");
        }
    }
}
