using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class M1IsAkisiCekirdegi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
             * M1 ÖNCESİ TEK KAYIT KAPATILIYOR.
             *
             * Canlıda tek bir görev vardı: GRV-2026-00001, "Test görev",
             * Hızır'ın 2026-08-01'de açtığı, ATANMAMIŞ ve durumu
             * Completed (4).
             *
             * Yeni akışta `Completed` "yapan bitirdi, GÖNDERENİN onayı
             * bekleniyor" demek. Bu kayıtta ATANAN YOK — yani "yapan"
             * diye biri yok. Onay kuyruğunda, kimin bitirdiği belirsiz
             * bir satır olarak asılı kalırdı.
             *
             * KOŞUL YALNIZ `AssignedToUserId IS NULL`: ilk yazdığımda
             * `AssignedByUserId IS NULL` şartını da koymuştum ve
             * güncelleme HİÇ ÇALIŞMADI — Hızır kaydı oluştururken
             * göndereni dolduruyor. Canlıda ölçmeseydim kayıt sessizce
             * bozuk kalacaktı.
             *
             * SİLİNMİYOR: sistemde silme yok kuralı görevler için de
             * ilk günden geçerli. Gerekçe kayda yazılıyor ki sonradan
             * "bu neden iptal edilmiş" sorusu çıkmasın.
             */
            migrationBuilder.Sql("""
                UPDATE "WorkTasks"
                SET "Status" = 5,
                    "CancelledAtUtc" = now(),
                    "CancellationReason" =
                        'M1 öncesi test kaydı; yeni akışta karşılığı yok. '
                        || 'Atanmamış olduğu için onay kuyruğunda, kimin '
                        || 'bitirdiği belirsiz bir satır olarak kalırdı.',
                    "UpdatedAtUtc" = now()
                WHERE "Status" IN (0, 3, 4)
                  AND "AssignedToUserId" IS NULL;
                """);

            /*
             * KALDIRILAN DURUMLARIN ARTIĞI: Draft(0) ve Waiting(3)
             * enum'dan çıktı. Yukarıdaki güncelleme atanmamış olanları
             * kapattı; atanmış ama bu durumlarda kalan bir satır varsa
             * `Open`'a çekiliyor — enum'da karşılığı olmayan bir sayı
             * bırakmak, okunduğunda tanımsız davranış demek.
             */
            migrationBuilder.Sql("""
                UPDATE "WorkTasks"
                SET "Status" = 1, "UpdatedAtUtc" = now()
                WHERE "Status" IN (0, 3);
                """);

            /*
             * NOT: EF burada `IX_employer_portal_links_Token`
             * düşürmeyi de önerdi. O indeks bir önceki migration'ın
             * (EmployerPortalLinkTokenHash) işi ve orada düşürülüyor;
             * snapshot artığı olarak buraya sızmıştı. Tekrar
             * düşürmek "index does not exist" ile patlıyordu.
             */

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "WorkTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                table: "WorkTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "WorkTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CenterType",
                table: "WorkTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DelegatedAtUtc",
                table: "WorkTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DelegatedFromUserId",
                table: "WorkTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DelegationCount",
                table: "WorkTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectSiteId",
                table: "WorkTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReturnCount",
                table: "WorkTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReturnReason",
                table: "WorkTasks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedAtUtc",
                table: "WorkTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReturnedByUserId",
                table: "WorkTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetUserId",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StoredName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    OriginalName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_attachments_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_recipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DismissedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_notification_recipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_recipients_notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    MentionedUserIds = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EditedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EditCount = table.Column<int>(type: "integer", nullable: false),
                    HiddenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HiddenByUserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_task_comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_comments_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_bana_atananlar",
                table: "WorkTasks",
                columns: new[] { "CompanyId", "AssignedToUserId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_gonderdiklerim",
                table: "WorkTasks",
                columns: new[] { "CompanyId", "AssignedByUserId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_kaynak_kayit",
                table: "WorkTasks",
                columns: new[] { "SourceModule", "SourceEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_attachments_CompanyId",
                table: "attachments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_attachments_dosya",
                table: "attachments",
                columns: new[] { "Category", "StoredName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attachments_kayit",
                table: "attachments",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_recipients_okunmamis",
                table: "notification_recipients",
                columns: new[] { "UserId", "ReadAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_recipients_tekil",
                table: "notification_recipients",
                columns: new[] { "NotificationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_comments_CompanyId",
                table: "task_comments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_task_comments_zaman_cizelgesi",
                table: "task_comments",
                columns: new[] { "EntityType", "EntityId", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachments");

            migrationBuilder.DropTable(
                name: "notification_recipients");

            migrationBuilder.DropTable(
                name: "task_comments");

            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_bana_atananlar",
                table: "WorkTasks");

            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_gonderdiklerim",
                table: "WorkTasks");

            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_kaynak_kayit",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "CenterType",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "DelegatedAtUtc",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "DelegatedFromUserId",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "DelegationCount",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "ProjectSiteId",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "ReturnCount",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "ReturnReason",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "ReturnedAtUtc",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "ReturnedByUserId",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "TargetUserId",
                table: "notifications");

        }
    }
}
