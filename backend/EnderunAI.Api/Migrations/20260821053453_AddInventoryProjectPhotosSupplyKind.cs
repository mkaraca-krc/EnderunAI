using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryProjectPhotosSupplyKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
             * S9 — TEKİL GÖRSEL ALANI GALERİYE DÖNÜŞTÜ.
             *
             * VERİ KAYBI YOK, ÖLÇÜLDÜ: taşımadan önce canlıda
             *   select count(*) from inventory_items
             *   where "ImagePath" is not null   ->   0
             * Kolon vardı ama hiçbir uç yazmıyordu ve ekranda karşılığı
             * yoktu. Taşınacak bir görsel olmadığı için veri taşıyan
             * adım YAZILMADI.
             *
             * Yerine `inventory_item_photos`: dekoratif bir üründe
             * montaj öncesi/sonrası, detay ve ölçü krokisi ayrı
             * görsellerdir; biri kapak olarak işaretlenir.
             */
            migrationBuilder.DropColumn(
                name: "ImagePath",
                table: "inventory_items");

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "inventory_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplyKind",
                table: "inventory_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "inventory_item_photos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoredName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    OriginalName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    IsCover = table.Column<bool>(type: "boolean", nullable: false),
                    Caption = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("PK_inventory_item_photos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventory_item_photos_inventory_items_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "inventory_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_ProjectId",
                table: "inventory_items",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_photos_InventoryItemId_IsCover",
                table: "inventory_item_photos",
                columns: new[] { "InventoryItemId", "IsCover" });

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_items_projects_ProjectId",
                table: "inventory_items",
                column: "ProjectId",
                principalTable: "projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_inventory_items_projects_ProjectId",
                table: "inventory_items");

            migrationBuilder.DropTable(
                name: "inventory_item_photos");

            migrationBuilder.DropIndex(
                name: "IX_inventory_items_ProjectId",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "SupplyKind",
                table: "inventory_items");

            migrationBuilder.AddColumn<string>(
                name: "ImagePath",
                table: "inventory_items",
                type: "text",
                nullable: true);
        }
    }
}
